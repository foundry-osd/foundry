// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Security.Cryptography;
using System.Text.Json;
using Foundry.Core.Models.Configuration;
using Foundry.Core.Models.Packages;
using Foundry.Core.Services.Configuration;
using Foundry.Core.Services.Images;
using Foundry.Core.Services.Packages;
using Foundry.Utilities.Storage;

namespace Foundry.Core.Services.WinPe;

/// <summary>
/// Publishes complete content package generations of one kind outside boot.wim without pruning other generations.
/// Temporary media staging supports long host paths; installer path limits apply separately to package imports and target staging.
/// </summary>
public sealed class WinPeContentPackageMediaService : IWinPeContentPackageMediaPublisher
{
    internal const long ReserveBytes = 64L * 1024 * 1024;
    private static readonly JsonSerializerOptions MediaJsonOptions = new(ConfigurationJsonDefaults.SerializerOptions) { WriteIndented = false };
    private readonly ContentPackageKind kind;
    private readonly Func<string, long> availableBytes;
    private readonly string stagingStatus;
    private readonly string verifiedStatus;

    /// <summary>Creates a publisher for one content package kind.</summary>
    /// <param name="kind">The content package kind that drives the media root and the media manifest limit.</param>
    public WinPeContentPackageMediaService(ContentPackageKind kind) : this(kind, WindowsVolumeStorage.GetAvailableBytes) { }

    internal WinPeContentPackageMediaService(ContentPackageKind kind, Func<string, long> availableBytes)
    {
        ArgumentNullException.ThrowIfNull(kind);
        ArgumentNullException.ThrowIfNull(availableBytes);
        this.kind = kind;
        this.availableBytes = availableBytes;
        // Statuses follow the kind so the progress text names the content being copied.
        (stagingStatus, verifiedStatus) = kind == ContentPackageKind.Drivers
            ? (WinPeProgressStatuses.StagingCustomDrivers, WinPeProgressStatuses.CustomDriversVerified)
            : (WinPeProgressStatuses.StagingPostInstallation, WinPeProgressStatuses.PostInstallationVerified);
    }

    /// <summary>Freezes the referenced package versions before boot configuration is generated.</summary>
    public async Task<WinPeContentPackageMediaLease> PrepareAsync(ContentPackageLibraryService library, IReadOnlyList<ContentPackageReference> references,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(library);
        ArgumentNullException.ThrowIfNull(references);
        if (library.Kind != kind) throw new ArgumentException($"The library holds '{library.Kind.Name}' packages but this service publishes '{kind.Name}' packages.", nameof(library));
        List<IDisposable> leases = [];
        try
        {
            List<ContentPackageMediaPackage> packages = [];
            List<WinPeContentPackageMediaFile> files = [];
            List<string> directories = [];
            long manifestBudget = 4096;
            foreach (ContentPackageReference reference in references.DistinctBy(package => package.ContentHash, StringComparer.OrdinalIgnoreCase))
            {
                ContentPackageLease lease = await library.AcquireAsync(reference.ContentHash, cancellationToken).ConfigureAwait(false);
                leases.Add(lease);
                if (lease.Reference.Length != reference.Length || lease.Reference.FileCount != reference.FileCount)
                    throw new InvalidDataException("ContentPackage.InvalidPackageReference");
                string prefix = $"{kind.MediaRoot}/Packages/{reference.ContentHash.ToLowerInvariant()}/files";
                var mediaPackage = new ContentPackageMediaPackage { ContentHash = reference.ContentHash.ToLowerInvariant(), RelativePath = prefix, Manifest = lease.Manifest };
                manifestBudget = checked(manifestBudget + JsonSerializer.SerializeToUtf8Bytes(mediaPackage, MediaJsonOptions).Length);
                if (manifestBudget > kind.MaximumMediaManifestBytes) throw new InvalidDataException("ContentPackage.MediaManifestTooLarge");
                packages.Add(mediaPackage);
                directories.Add(prefix);
                directories.AddRange(lease.Manifest.Directories.Select(directory => $"{prefix}/{directory}"));
                files.AddRange(lease.Files.Select(file => new WinPeContentPackageMediaFile(file.SourcePath, $"{prefix}/{file.RelativePath}", file.Length, file.Sha256)));
            }
            var manifest = new ContentPackageMediaManifest { Id = Guid.NewGuid().ToString("N"), Packages = packages };
            byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(manifest, MediaJsonOptions);
            if (bytes.Length > kind.MaximumMediaManifestBytes) throw new InvalidDataException("ContentPackage.MediaManifestTooLarge");
            return new(kind, manifest, bytes, files, directories, leases);
        }
        catch { foreach (IDisposable lease in leases.AsEnumerable().Reverse()) lease.Dispose(); throw; }
    }

    /// <summary>Verifies that every held source file still matches its recorded length and SHA-256.</summary>
    public async Task ValidateSourcesAsync(WinPeContentPackageMediaLease package, CancellationToken cancellationToken)
    {
        ThrowIfInvalidLease(package);
        foreach (WinPeContentPackageMediaFile file in package.Files)
            if (!await MatchesAsync(file.SourcePath, file.Length, file.ContentHash, cancellationToken).ConfigureAwait(false))
                throw new InvalidDataException("ContentPackage.PackageContentChanged");
    }

    /// <summary>Counts only new/replacement bytes while retaining prior files, generations and a staging reserve.</summary>
    public async Task<long> GetRequiredBytesAsync(WinPeContentPackageMediaLease package, string root, CancellationToken cancellationToken)
    {
        ThrowIfInvalidLease(package);
        long required = checked(ReserveBytes + package.ManifestBytes.LongLength);
        foreach (WinPeContentPackageMediaFile file in package.Files)
            if (!await MatchesAsync(CustomImagePathPolicy.ResolveRelativePath(root, file.RelativePath), file.Length, file.ContentHash, cancellationToken).ConfigureAwait(false))
                required = checked(required + file.Length);
        return required;
    }

    /// <summary>Throws when the media volume cannot hold the required bytes plus <paramref name="additionalBytes"/>.</summary>
    public async Task ValidateCapacityAsync(WinPeContentPackageMediaLease package, string root, long additionalBytes, CancellationToken cancellationToken)
    {
        ThrowIfInvalidLease(package);
        if (availableBytes(root) < checked(await GetRequiredBytesAsync(package, root, cancellationToken).ConfigureAwait(false) + additionalBytes))
            throw new IOException("ContentPackage.InsufficientMediaSpace");
    }

    /// <summary>Verifies staged files before exposing the manifest, so canceled publication leaves older boot configurations usable.</summary>
    public async Task PublishAsync(WinPeContentPackageMediaLease package, string root, CancellationToken cancellationToken,
        IProgress<WinPeMediaProgress>? progress = null)
    {
        ThrowIfInvalidLease(package);
        await ValidateSourcesAsync(package, cancellationToken).ConfigureAwait(false);
        string ownedRoot = CustomImagePathPolicy.ResolveRelativePath(root, kind.MediaRoot);
        Directory.CreateDirectory(ownedRoot);
        using var publicationLock = new FileStream(CustomImagePathPolicy.ResolveRelativePath(ownedRoot, ".publish.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        await ValidateCapacityAsync(package, root, 0, cancellationToken).ConfigureAwait(false);
        string pending = CustomImagePathPolicy.ResolveRelativePath(ownedRoot, ".pending-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(pending);
        try
        {
            List<(string Temporary, string Destination)> copied = [];
            long completed = 0;
            foreach (WinPeContentPackageMediaFile file in package.Files)
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
                    if (!await MatchesAsync(temporary, file.Length, file.ContentHash, cancellationToken).ConfigureAwait(false)) throw new InvalidDataException("ContentPackage.PackageContentChanged");
                    copied.Add((temporary, destination));
                }
                completed = checked(completed + file.Length);
                progress?.Report(new() { Percent = (int)(completed * 90.0 / Math.Max(1, package.TotalBytes)), Status = stagingStatus });
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
                    throw new InvalidDataException("ContentPackage.InvalidMediaBinding");
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
            progress?.Report(new() { Percent = 100, Status = verifiedStatus });
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

    private void ThrowIfInvalidLease(WinPeContentPackageMediaLease package)
    {
        ArgumentNullException.ThrowIfNull(package);
        package.ThrowIfDisposed();
        if (package.Kind != kind) throw new ArgumentException($"The lease holds '{package.Kind.Name}' packages but this service publishes '{kind.Name}' packages.", nameof(package));
    }

    private static async Task<bool> MatchesAsync(string path, long length, string hash, CancellationToken cancellationToken)
    {
        CustomImagePathPolicy.ValidateNoReparsePoints(path);
        if (!File.Exists(path)) return false;
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 128 * 1024, useAsync: true);
        return stream.Length == length && Convert.ToHexStringLower(await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false)).Equals(hash, StringComparison.OrdinalIgnoreCase);
    }
}

/// <summary>Publishes a prepared content package lease to external media.</summary>
internal interface IWinPeContentPackageMediaPublisher
{
    /// <summary>Verifies that every held source file still matches its recorded identity.</summary>
    Task ValidateSourcesAsync(WinPeContentPackageMediaLease package, CancellationToken cancellationToken);
    /// <summary>Returns the bytes the media volume needs to publish the lease.</summary>
    Task<long> GetRequiredBytesAsync(WinPeContentPackageMediaLease package, string root, CancellationToken cancellationToken);
    /// <summary>Throws when the media volume cannot hold the lease plus additional bytes.</summary>
    Task ValidateCapacityAsync(WinPeContentPackageMediaLease package, string root, long additionalBytes, CancellationToken cancellationToken);
    /// <summary>Copies the lease content to the media root and exposes its manifest last.</summary>
    Task PublishAsync(WinPeContentPackageMediaLease package, string root, CancellationToken cancellationToken, IProgress<WinPeMediaProgress>? progress = null);
}
