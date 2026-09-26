// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using Foundry.Core.Models.PreOobe;
using Foundry.Core.Services.Packages;
using Foundry.Deploy.Services.Configuration;
using Foundry.Deploy.Services.Images;

namespace Foundry.Deploy.Services.Deployment.PreOobe;

/// <summary>Resolves only the runtime pinned by the authenticated Deploy companion descriptor.</summary>
public sealed class PreOobeRuntimeResolver
{
    public const string DescriptorFileName = "foundry.postinstall-runtime.json";
    public const long MaximumArchiveBytes = 256L * 1024 * 1024;
    public const long MaximumExpandedBytes = 512L * 1024 * 1024;
    private readonly HttpClient _httpClient;

    public PreOobeRuntimeResolver() : this(new HttpClient { Timeout = TimeSpan.FromMinutes(10) }) { }
    internal PreOobeRuntimeResolver(HttpClient httpClient) => _httpClient = httpClient;

    /// <summary>Reads trusted boot-owned metadata, never a descriptor supplied by a mutable source cache.</summary>
    internal static PreOobeRuntimeDescriptor ReadDescriptor(string path)
    {
        if (new FileInfo(path).Length is <= 0 or > 65536) throw new InvalidDataException("Post-installation runtime descriptor has an invalid size.");
        var descriptor = JsonSerializer.Deserialize<PreOobeRuntimeDescriptor>(File.ReadAllBytes(path), ConfigurationJsonDefaults.SerializerOptions)
            ?? throw new InvalidDataException("Post-installation runtime descriptor is missing.");
        if (descriptor.SchemaVersion != 1 || descriptor.ContractVersion != 1 || string.IsNullOrWhiteSpace(descriptor.ReleaseTag) ||
            descriptor.Assets.Count != 2 || descriptor.Assets.Select(asset => asset.RuntimeIdentifier).Distinct().Count() != descriptor.Assets.Count)
            throw new InvalidDataException("Post-installation runtime descriptor is incompatible.");
        foreach (var asset in descriptor.Assets) ValidateAsset(asset);
        return descriptor;
    }

    internal static void ValidateAsset(PreOobeRuntimeAsset asset)
    {
        if (asset.RuntimeIdentifier is not ("win-x64" or "win-arm64") || asset.AssetName != $"Foundry.PostInstall-{asset.RuntimeIdentifier}.zip" ||
            !IsHash(asset.ArchiveSha256) || asset.ArchiveLength is <= 0 or > MaximumArchiveBytes ||
            asset.ExpandedLength is <= 0 or > MaximumExpandedBytes || asset.EntryCount is <= 0 or > 1024)
            throw new InvalidDataException("Post-installation runtime metadata is invalid.");
    }

    internal static bool IsHash(string? value) => value?.Length == 64 && value.All(Uri.IsHexDigit);

    /// <summary>Reuses exact authenticated bytes; download publication never overwrites an unrelated valid cache entry.</summary>
    internal async Task<string> ResolveAsync(PreOobeRuntimeDescriptor descriptor, PreOobeRuntimeAsset asset,
        IEnumerable<string> candidates, string writableRoot, CancellationToken cancellationToken)
    {
        ValidateAsset(asset);
        foreach (string path in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!File.Exists(path)) continue;
            try { await VerifyAsync(path, asset, cancellationToken).ConfigureAwait(false); return path; }
            catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException) { }
        }
        if (!descriptor.ReleaseTag.StartsWith('v') || descriptor.ReleaseTag.Any(character => !char.IsAsciiLetterOrDigit(character) && character is not '.' and not '-'))
            throw new InvalidDataException("The matching local post-installation runtime is unavailable.");
        Directory.CreateDirectory(writableRoot);
        string destination = Path.Combine(writableRoot, "runtime.zip");
        string pending = Path.Combine(writableRoot, ".pending-" + Guid.NewGuid().ToString("N"));
        try
        {
            string url = $"https://github.com/foundry-osd/foundry/releases/download/{Uri.EscapeDataString(descriptor.ReleaseTag)}/{asset.AssetName}";
            using var response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength is long length && length != asset.ArchiveLength) throw new InvalidDataException("Runner download length differs from its authenticated descriptor.");
            await using (var source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false))
            await using (var output = new FileStream(pending, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                await CopyBoundedAsync(source, output, asset.ArchiveLength, cancellationToken).ConfigureAwait(false);
            await VerifyAsync(pending, asset, cancellationToken).ConfigureAwait(false);
            File.Move(pending, destination, overwrite: true);
            return destination;
        }
        finally { if (File.Exists(pending)) File.Delete(pending); }
    }

    internal static async Task VerifyAsync(string path, PreOobeRuntimeAsset asset, CancellationToken cancellationToken)
    {
        using var lease = await CustomImageSourceLease.AcquireAsync(path, asset.ArchiveLength, asset.ArchiveSha256, cancellationToken).ConfigureAwait(false);
        using var archive = ZipFile.OpenRead(path);
        ValidateEntries(archive, asset);
    }

    internal static void ValidateEntries(ZipArchive archive, PreOobeRuntimeAsset asset)
    {
        if (archive.Entries.Count > 1024) throw new InvalidDataException("Too many runtime archive entries.");
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        long total = 0;
        int files = 0;
        foreach (ZipArchiveEntry entry in archive.Entries)
        {
            string path = entry.FullName.TrimEnd('/');
            ValidateRelativePath(path);
            if (!paths.Add(path) || ((entry.ExternalAttributes >> 16) & 0xF000) == 0xA000 || (entry.ExternalAttributes & (int)FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("Runtime archive contains an unsafe entry.");
            if (entry.Name.Length == 0) continue;
            total = checked(total + entry.Length);
            files++;
            if (total > asset.ExpandedLength) throw new InvalidDataException("Runtime archive expands beyond its authenticated size.");
        }
        if (total != asset.ExpandedLength || files != asset.EntryCount || !paths.Contains("Foundry.PostInstall.exe") || !paths.Contains("Launch.cmd"))
            throw new InvalidDataException("Runtime archive does not match its descriptor.");
    }

    internal static void ValidateRelativePath(string path) => PreOobePackagePathPolicy.ValidateRelativePath(path);

    internal static async Task CopyBoundedAsync(Stream input, Stream output, long expected, CancellationToken cancellationToken)
    {
        byte[] buffer = new byte[81920];
        long total = 0;
        int count;
        while ((count = await input.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) != 0)
        {
            total = checked(total + count);
            if (total > expected) throw new InvalidDataException("Content exceeds its declared size.");
            await output.WriteAsync(buffer.AsMemory(0, count), cancellationToken).ConfigureAwait(false);
        }
        if (total != expected) throw new InvalidDataException("Content is incomplete.");
    }
}
