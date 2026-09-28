// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Security.Cryptography;
using System.Text.Json;
using Foundry.Core.Models.Configuration;
using Foundry.Core.Models.Configuration.Deploy;
using Foundry.Core.Models.PreOobe;
using Foundry.Core.Services.Configuration;
using Foundry.Core.Services.Images;
using Foundry.Core.Services.Packages;
using Foundry.Utilities.Storage;

namespace Foundry.Core.Services.WinPe;

/// <summary>
/// Publishes complete post-installation generations outside boot.wim without pruning other generations.
/// Temporary media staging supports long host paths; installer path limits apply separately to package imports and target staging.
/// </summary>
public sealed class WinPePreOobeMediaService : IWinPePreOobeMediaPublisher
{
    internal const long ReserveBytes = 64L * 1024 * 1024;
    private const int MaximumManifestBytes = 16 * 1024 * 1024;
    private static readonly JsonSerializerOptions MediaJsonOptions = new(ConfigurationJsonDefaults.SerializerOptions) { WriteIndented = false };
    private readonly Func<string, long> availableBytes;

    public WinPePreOobeMediaService() : this(WindowsVolumeStorage.GetAvailableBytes) { }
    internal WinPePreOobeMediaService(Func<string, long> availableBytes) => this.availableBytes = availableBytes;

    /// <summary>Freezes enabled package versions and already authenticated companion archives before boot configuration is generated.</summary>
    public async Task<WinPePreOobeMediaLease> PrepareAsync(PreOobePackageLibraryService library, PreOobeSettings settings,
        IReadOnlyDictionary<string, string> runtimeArchives, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(library);
        ArgumentNullException.ThrowIfNull(runtimeArchives);
        PreOobeConfigurationValidator.ThrowIfInvalid(settings);
        if (runtimeArchives.Count is 0 or > 2 || runtimeArchives.Keys.Any(rid => rid is not ("win-x64" or "win-arm64")))
            throw new InvalidDataException("PreOobe.InvalidRuntimeDescriptor");
        List<IDisposable> leases = [];
        try
        {
            List<PreOobeMediaPackage> packages = [];
            List<PreOobeMediaRuntime> runtimes = [];
            List<WinPePreOobeMediaFile> files = [];
            List<string> directories = [];
            long manifestBudget = 4096;
            foreach (PreOobePackageReference reference in settings.Actions.Where(action => settings.IsEnabled && action.IsEnabled && action.Package is not null)
                .Select(action => action.Package!).DistinctBy(package => package.ContentHash, StringComparer.OrdinalIgnoreCase))
            {
                PreOobePackageLease lease = await library.AcquireAsync(reference.ContentHash, cancellationToken).ConfigureAwait(false);
                leases.Add(lease);
                if (lease.Reference.Length != reference.Length || lease.Reference.FileCount != reference.FileCount)
                    throw new InvalidDataException("PreOobe.InvalidPackageReference");
                string prefix = $"Cache/PreOobe/Packages/{reference.ContentHash.ToLowerInvariant()}/files";
                var mediaPackage = new PreOobeMediaPackage { ContentHash = reference.ContentHash.ToLowerInvariant(), RelativePath = prefix, Manifest = lease.Manifest };
                manifestBudget = checked(manifestBudget + JsonSerializer.SerializeToUtf8Bytes(mediaPackage, MediaJsonOptions).Length);
                if (manifestBudget > MaximumManifestBytes) throw new InvalidDataException("PreOobe.MediaManifestTooLarge");
                packages.Add(mediaPackage);
                directories.Add(prefix);
                directories.AddRange(lease.Manifest.Directories.Select(directory => $"{prefix}/{directory}"));
                files.AddRange(lease.Files.Select(file => new WinPePreOobeMediaFile(file.SourcePath, $"{prefix}/{file.RelativePath}", file.Length, file.Sha256)));
            }
            foreach (PreOobeActionSettings action in settings.Actions.Where(action => settings.IsEnabled && action.IsEnabled && action.Package is not null))
            {
                PreOobeMediaPackage package = packages.Single(entry => entry.ContentHash.Equals(action.Package!.ContentHash, StringComparison.OrdinalIgnoreCase));
                if (package.Manifest.Files.Count != action.Package!.FileCount || package.Manifest.Files.Sum(file => file.Length) != action.Package.Length ||
                    (action.EntryPoint is not null && !package.Manifest.Files.Any(file => file.RelativePath.Equals(action.EntryPoint, StringComparison.OrdinalIgnoreCase))) ||
                    (action.WorkingDirectory is not null && !package.Manifest.Directories.Contains(action.WorkingDirectory, StringComparer.OrdinalIgnoreCase)))
                    throw new InvalidDataException("PreOobe.MissingEntryPoint");
            }
            foreach ((string rid, string archivePath) in runtimeArchives.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            {
                cancellationToken.ThrowIfCancellationRequested();
                CustomImagePathPolicy.ValidateNoReparsePoints(archivePath);
                var stream = new FileStream(archivePath, FileMode.Open, FileAccess.Read, FileShare.Read, 128 * 1024,
                    FileOptions.Asynchronous | FileOptions.SequentialScan);
                leases.Add(stream);
                if (stream.Length is <= 0 or > 256L * 1024 * 1024) throw new InvalidDataException("PreOobe.InvalidRuntimeArchive");
                string hash = Convert.ToHexStringLower(await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false));
                stream.Position = 0;
                string relative = $"Cache/PreOobe/Runtimes/{rid}/{hash}/runtime.zip";
                files.Add(new(archivePath, relative, stream.Length, hash));
                runtimes.Add(new() { RuntimeIdentifier = rid, RelativePath = relative, ArchiveSha256 = hash });
            }
            var manifest = new PreOobeMediaManifest { Id = Guid.NewGuid().ToString("N"), Packages = packages, Runtimes = runtimes };
            byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(manifest, MediaJsonOptions);
            if (bytes.Length > MaximumManifestBytes) throw new InvalidDataException("PreOobe.MediaManifestTooLarge");
            return new(manifest, bytes, files, directories, leases);
        }
        catch { foreach (IDisposable lease in leases.AsEnumerable().Reverse()) lease.Dispose(); throw; }
    }

    public static string BindConfiguration(WinPePreOobeMediaLease package, string deployConfigurationJson)
    {
        package.ThrowIfDisposed();
        var configuration = JsonSerializer.Deserialize<FoundryDeployConfigurationDocument>(deployConfigurationJson, ConfigurationJsonDefaults.SerializerOptions)
            ?? throw new InvalidDataException("PreOobe.InvalidConfiguration");
        string bound = JsonSerializer.Serialize(configuration with
        {
            PreOobe = configuration.PreOobe with { ManifestId = package.ManifestId, ManifestHash = package.ManifestHash }
        }, ConfigurationJsonDefaults.SerializerOptions);
        ValidateConfigurationBinding(package, bound);
        return bound;
    }

    public static void ValidateConfigurationBinding(WinPePreOobeMediaLease package, string deployConfigurationJson)
    {
        package.ThrowIfDisposed();
        var configuration = JsonSerializer.Deserialize<FoundryDeployConfigurationDocument>(deployConfigurationJson, ConfigurationJsonDefaults.SerializerOptions);
        DeployPreOobeSettings? settings = configuration?.PreOobe;
        if (settings is null || settings.ManifestId != package.ManifestId || !package.ManifestHash.Equals(settings.ManifestHash, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("PreOobe.InvalidMediaBinding");
        PreOobeConfigurationValidator.ThrowIfInvalid(new() { IsEnabled = settings.IsEnabled, Actions = settings.Actions });
        string[] required = settings.Actions.Where(action => settings.IsEnabled && action.IsEnabled && action.Package is not null)
            .Select(action => action.Package!.ContentHash).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToArray();
        string[] supplied = package.Manifest.Packages.Select(entry => entry.ContentHash).Order(StringComparer.OrdinalIgnoreCase).ToArray();
        if (!required.SequenceEqual(supplied, StringComparer.OrdinalIgnoreCase)) throw new InvalidDataException("PreOobe.InvalidMediaBinding");
    }

    public async Task ValidateSourcesAsync(WinPePreOobeMediaLease package, CancellationToken cancellationToken)
    {
        package.ThrowIfDisposed();
        foreach (WinPePreOobeMediaFile file in package.Files)
            if (!await MatchesAsync(file.SourcePath, file.Length, file.ContentHash, cancellationToken).ConfigureAwait(false))
                throw new InvalidDataException("PreOobe.PackageContentChanged");
    }

    /// <summary>Counts only new/replacement bytes while retaining prior files, generations and a staging reserve.</summary>
    public async Task<long> GetRequiredBytesAsync(WinPePreOobeMediaLease package, string root, CancellationToken cancellationToken)
    {
        package.ThrowIfDisposed();
        long required = checked(ReserveBytes + package.ManifestBytes.LongLength);
        foreach (WinPePreOobeMediaFile file in package.Files)
            if (!await MatchesAsync(CustomImagePathPolicy.ResolveRelativePath(root, file.RelativePath), file.Length, file.ContentHash, cancellationToken).ConfigureAwait(false))
                required = checked(required + file.Length);
        return required;
    }

    public async Task ValidateCapacityAsync(WinPePreOobeMediaLease package, string root, long additionalBytes, CancellationToken cancellationToken)
    {
        if (availableBytes(root) < checked(await GetRequiredBytesAsync(package, root, cancellationToken).ConfigureAwait(false) + additionalBytes))
            throw new IOException("PreOobe.InsufficientMediaSpace");
    }

    /// <summary>Verifies staged files before exposing the manifest, so canceled publication leaves older boot configurations usable.</summary>
    public async Task PublishAsync(WinPePreOobeMediaLease package, string root, CancellationToken cancellationToken,
        IProgress<WinPeMediaProgress>? progress = null)
    {
        await ValidateSourcesAsync(package, cancellationToken).ConfigureAwait(false);
        string ownedRoot = CustomImagePathPolicy.ResolveRelativePath(root, "Cache/PreOobe");
        Directory.CreateDirectory(ownedRoot);
        using var publicationLock = new FileStream(CustomImagePathPolicy.ResolveRelativePath(ownedRoot, ".publish.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        await ValidateCapacityAsync(package, root, 0, cancellationToken).ConfigureAwait(false);
        string pending = CustomImagePathPolicy.ResolveRelativePath(ownedRoot, ".pending-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(pending);
        try
        {
            List<(string Temporary, string Destination)> copied = [];
            long completed = 0;
            foreach (WinPePreOobeMediaFile file in package.Files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string destination = CustomImagePathPolicy.ResolveRelativePath(root, file.RelativePath);
                if (!await MatchesAsync(destination, file.Length, file.ContentHash, cancellationToken).ConfigureAwait(false))
                {
                    string temporary = Path.Combine(pending, Guid.NewGuid().ToString("N"));
                    await using (var input = new FileStream(file.SourcePath, FileMode.Open, FileAccess.Read, FileShare.Read, 128 * 1024, useAsync: true))
                    await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 128 * 1024, useAsync: true))
                    {
                        await input.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
                        await output.FlushAsync(cancellationToken).ConfigureAwait(false);
                        output.Flush(flushToDisk: true);
                    }
                    if (!await MatchesAsync(temporary, file.Length, file.ContentHash, cancellationToken).ConfigureAwait(false)) throw new InvalidDataException("PreOobe.PackageContentChanged");
                    copied.Add((temporary, destination));
                }
                completed = checked(completed + file.Length);
                progress?.Report(new() { Percent = (int)(completed * 90.0 / Math.Max(1, package.TotalBytes)), Status = "Staging post-installation content." });
            }
            foreach (string directory in package.Directories) Directory.CreateDirectory(CustomImagePathPolicy.ResolveRelativePath(root, directory));
            foreach ((string temporary, string destination) in copied)
            {
                cancellationToken.ThrowIfCancellationRequested();
                CustomImagePathPolicy.ValidateNoReparsePoints(destination);
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                File.Move(temporary, destination, overwrite: true);
            }
            string manifest = CustomImagePathPolicy.ResolveRelativePath(root, package.ManifestRelativePath);
            if (File.Exists(manifest))
            {
                if (!await MatchesAsync(manifest, package.ManifestBytes.Length, package.ManifestHash, cancellationToken).ConfigureAwait(false))
                    throw new InvalidDataException("PreOobe.InvalidMediaBinding");
            }
            else
            {
                string temporary = Path.Combine(pending, "manifest.json");
                await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true))
                {
                    await output.WriteAsync(package.ManifestBytes, cancellationToken).ConfigureAwait(false);
                    await output.FlushAsync(cancellationToken).ConfigureAwait(false);
                    output.Flush(flushToDisk: true);
                }
                cancellationToken.ThrowIfCancellationRequested();
                Directory.CreateDirectory(Path.GetDirectoryName(manifest)!);
                File.Move(temporary, manifest);
            }
            progress?.Report(new() { Percent = 100, Status = "Post-installation content verified." });
        }
        finally
        {
            CustomImagePathPolicy.ValidateNoReparsePoints(pending);
            foreach (string file in Directory.EnumerateFiles(pending))
            {
                CustomImagePathPolicy.ValidateNoReparsePoints(file);
                File.Delete(file);
            }
            Directory.Delete(pending, recursive: false);
        }
    }

    private static async Task<bool> MatchesAsync(string path, long length, string hash, CancellationToken cancellationToken)
    {
        CustomImagePathPolicy.ValidateNoReparsePoints(path);
        if (!File.Exists(path)) return false;
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 128 * 1024, useAsync: true);
        return stream.Length == length && Convert.ToHexStringLower(await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false)).Equals(hash, StringComparison.OrdinalIgnoreCase);
    }
}

internal interface IWinPePreOobeMediaPublisher
{
    Task ValidateSourcesAsync(WinPePreOobeMediaLease package, CancellationToken cancellationToken);
    Task<long> GetRequiredBytesAsync(WinPePreOobeMediaLease package, string root, CancellationToken cancellationToken);
    Task ValidateCapacityAsync(WinPePreOobeMediaLease package, string root, long additionalBytes, CancellationToken cancellationToken);
    Task PublishAsync(WinPePreOobeMediaLease package, string root, CancellationToken cancellationToken, IProgress<WinPeMediaProgress>? progress = null);
}
