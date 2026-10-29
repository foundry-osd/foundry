// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Security.Cryptography;
using Foundry.Core.Models.Images;
using Foundry.Core.Services.Images;
using Foundry.Utilities.Processes;
using Foundry.Utilities.Storage;

namespace Foundry.Core.Services.WinPe;

/// <summary>Publishes verified custom-image generations without deleting prior media or operator files.</summary>
public sealed partial class WinPeCustomImageMediaService : IWinPeCustomImageMediaPublisher
{
    internal const long DataReserveBytes = 64L * 1024 * 1024;
    private readonly Func<string, long> availableBytes;
    private readonly Func<string, CancellationToken, Task<int?>> resolveDisk;

    /// <summary>Uses Windows volume capacity and physical-disk discovery for media safeguards.</summary>
    public WinPeCustomImageMediaService()
        : this(WindowsVolumeStorage.GetAvailableBytes, new WindowsDiskInspector(new ProcessRunner()).ResolveDiskNumberForPathAsync)
    {
    }

    internal WinPeCustomImageMediaService(Func<string, long> availableBytes,
        Func<string, CancellationToken, Task<int?>> resolveDisk)
    {
        this.availableBytes = availableBytes;
        this.resolveDisk = resolveDisk;
    }

    /// <summary>
    /// Protects all prepared build inputs when their storage has been redirected onto removable media. The physical disk is
    /// resolved once per distinct drive root and reused for every input on it, because each resolution starts a PowerShell
    /// process and a driver set can hold thousands of files; paths without a usable root are resolved individually.
    /// </summary>
    public async Task ValidateInputDisksAsync(IEnumerable<string> paths, int targetDisk, CancellationToken cancellationToken)
    {
        var rootDisks = new Dictionary<string, int?>(StringComparer.OrdinalIgnoreCase);
        foreach (string path in paths.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();
            CustomImagePathPolicy.ValidateNoReparsePoints(path);
            string? pathRoot = Path.GetPathRoot(path);
            int? sourceDisk;
            if (string.IsNullOrEmpty(pathRoot))
                sourceDisk = await resolveDisk(path, cancellationToken).ConfigureAwait(false);
            else if (!rootDisks.TryGetValue(pathRoot, out sourceDisk))
            {
                sourceDisk = await resolveDisk(path, cancellationToken).ConfigureAwait(false);
                rootDisks[pathRoot] = sourceDisk;
            }
            if (!sourceDisk.HasValue || sourceDisk.Value == targetDisk)
                throw new InvalidDataException("A media input is on the target disk or its physical disk could not be verified.");
        }
    }

    /// <summary>Rejects drive-letter reuse or an unresolved data volume before writing external image content.</summary>
    public async Task ValidateDestinationDiskAsync(string root, int targetDisk, CancellationToken cancellationToken)
    {
        CustomImagePathPolicy.ValidateNoReparsePoints(root);
        if (await resolveDisk(root, cancellationToken).ConfigureAwait(false) != targetDisk)
            throw new InvalidDataException("The custom-image destination no longer belongs to the selected USB disk.");
    }

    /// <summary>Verifies every retained source before a destructive operation or output publication.</summary>
    public async Task ValidateSourcesAsync(WinPeCustomImageMediaLease package, CancellationToken cancellationToken = default)
    {
        package.ThrowIfDisposed();
        foreach (WinPeCustomImageMediaFile file in package.Files)
        {
            CustomImagePathPolicy.ValidateRelativePath(file.RelativePath);
            CustomImagePathPolicy.ValidateNoReparsePoints(file.SourcePath);
            if (!await MatchesAsync(file.SourcePath, file.Length, file.ContentHash, cancellationToken).ConfigureAwait(false))
                throw new InvalidDataException("A custom image input no longer matches its verified length and SHA256.");
        }
    }

    /// <summary>Checks the additional space for an update, retaining all previous and unmanaged content.</summary>
    public async Task<long> GetRequiredBytesAsync(WinPeCustomImageMediaLease package, string destinationRoot,
        CancellationToken cancellationToken = default)
    {
        package.ThrowIfDisposed();
        long required = checked(DataReserveBytes + package.ManifestBytes.LongLength);
        foreach (WinPeCustomImageMediaFile file in package.Files)
        {
            string destination = CustomImagePathPolicy.ResolveRelativePath(destinationRoot, file.RelativePath);
            if (!await MatchesAsync(destination, file.Length, file.ContentHash, cancellationToken).ConfigureAwait(false))
                required = checked(required + file.Length);
        }
        return required;
    }

    /// <summary>Reserves replacement runtime payloads as well as new image bytes before changing BOOT.</summary>
    public async Task ValidateCapacityAsync(WinPeCustomImageMediaLease package, string root, long additionalBytes, CancellationToken cancellationToken)
    {
        long required = checked(await GetRequiredBytesAsync(package, root, cancellationToken).ConfigureAwait(false) + additionalBytes);
        if (availableBytes(root) < required) throw new WinPeMediaCapacityException("The data volume has insufficient free space for the custom images and runtime payloads.");
    }

    /// <summary>Stages new files, verifies them, and publishes the manifest last; cancellation never removes old generations.</summary>
    public Task PublishAsync(WinPeCustomImageMediaLease package, string destinationRoot,
        CancellationToken cancellationToken = default, IProgress<WinPeMediaProgress>? progress = null) =>
        PublishCoreAsync(package, destinationRoot, isNetworkCacheShare: false, cancellationToken, progress);

    /// <summary>
    /// Publishes to a network cache share without reading the share back: an image already at its content path with the
    /// recorded length counts as present, and a copy is checked against the bytes read from the source. Foundry Deploy verifies
    /// the hash of the image it uses.
    /// </summary>
    public Task PublishToNetworkCacheAsync(WinPeCustomImageMediaLease package, string contentRoot,
        CancellationToken cancellationToken = default, IProgress<WinPeMediaProgress>? progress = null) =>
        PublishCoreAsync(package, contentRoot, isNetworkCacheShare: true, cancellationToken, progress);

    private async Task PublishCoreAsync(WinPeCustomImageMediaLease package, string destinationRoot, bool isNetworkCacheShare,
        CancellationToken cancellationToken, IProgress<WinPeMediaProgress>? progress)
    {
        package.ThrowIfDisposed();
        // On a share each copied image is checked against the bytes read from its source, and present images never read it.
        if (!isNetworkCacheShare) await ValidateSourcesAsync(package, cancellationToken).ConfigureAwait(false);
        string customRoot = CustomImagePathPolicy.ResolveRelativePath(destinationRoot, CustomImageMediaPaths.RelativeRoot);
        Directory.CreateDirectory(customRoot);
        string lockPath = CustomImagePathPolicy.ResolveRelativePath(customRoot, WinPeMediaFileTransfer.PublicationLockFileName);
        using var publicationLease = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        var byteProgress = WinPeMediaByteProgress.ForFiles(progress, package.Files.Select(file => file.Length), WinPeProgressStatuses.StagingCustomImages);
        // Each destination is read at most once: what already matches is reused, and only the rest counts against free space.
        var missing = new List<(WinPeCustomImageMediaFile File, string Destination)>();
        long required = checked(DataReserveBytes + package.ManifestBytes.LongLength);
        foreach (WinPeCustomImageMediaFile file in package.Files)
        {
            string destination = CustomImagePathPolicy.ResolveRelativePath(destinationRoot, file.RelativePath);
            bool matches = isNetworkCacheShare
                ? WinPeMediaFileTransfer.HasLength(destination, file.Length, byteProgress.Full)
                : await MatchesAsync(destination, file.Length, file.ContentHash, cancellationToken, byteProgress.Full).ConfigureAwait(false);
            byteProgress.EndOfFile();
            if (matches) continue;
            missing.Add((file, destination));
            required = checked(required + file.Length);
        }
        if (availableBytes(destinationRoot) < required) throw new WinPeMediaCapacityException("The data volume has insufficient free space for the custom images and staging reserve.");
        if (isNetworkCacheShare)
        {
            // Reused content and manifests keep their old write time; refreshing it under the lock keeps Clean up on another PC
            // from removing them before this publication writes the index that binds them.
            foreach (WinPeCustomImageMediaFile file in package.Files)
                WinPeMediaFileTransfer.MarkInUse(Path.GetDirectoryName(CustomImagePathPolicy.ResolveRelativePath(destinationRoot, file.RelativePath))!);
            WinPeMediaFileTransfer.MarkInUse(CustomImagePathPolicy.ResolveRelativePath(destinationRoot, package.ManifestRelativePath));
        }
        string pending = Path.Combine(customRoot, ".pending-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(pending);
        try
        {
            var staged = new List<(string Temporary, string Destination)>();
            foreach ((WinPeCustomImageMediaFile file, string destination) in missing)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string temporary = Path.Combine(pending, Guid.NewGuid().ToString("N"));
                await CopyVerifiedAsync(file, temporary, isNetworkCacheShare, byteProgress, cancellationToken).ConfigureAwait(false);
                byteProgress.EndOfFile();
                staged.Add((temporary, destination));
            }
            foreach ((string temporary, string destination) in staged)
            {
                cancellationToken.ThrowIfCancellationRequested();
                CustomImagePathPolicy.ValidateNoReparsePoints(destination);
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                File.Move(temporary, destination, overwrite: true);
            }
            string manifest = CustomImagePathPolicy.ResolveRelativePath(destinationRoot, package.ManifestRelativePath);
            if (File.Exists(manifest))
            {
                if (!await MatchesAsync(manifest, package.ManifestBytes.LongLength, package.ManifestHash, cancellationToken).ConfigureAwait(false))
                    throw new InvalidDataException("An existing media manifest has a conflicting build identity.");
            }
            else
            {
                string temporary = Path.Combine(pending, "manifest.json");
                await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true))
                {
                    await stream.WriteAsync(package.ManifestBytes, cancellationToken).ConfigureAwait(false);
                    stream.Flush(flushToDisk: true);
                }
                cancellationToken.ThrowIfCancellationRequested();
                Directory.CreateDirectory(Path.GetDirectoryName(manifest)!);
                File.Move(temporary, manifest);
            }
            byteProgress.Complete(100, WinPeProgressStatuses.CustomImagesVerified);
        }
        finally
        {
            try { Directory.Delete(pending, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    /// <summary>
    /// Copies an image and checks it: on a share against the bytes read from the source, elsewhere by reading the copy back.
    /// </summary>
    private static async Task CopyVerifiedAsync(WinPeCustomImageMediaFile file, string destination, bool isNetworkCacheShare,
        WinPeMediaByteProgress byteProgress, CancellationToken cancellationToken)
    {
        bool verified;
        if (isNetworkCacheShare)
        {
            WinPeCopiedFile copied = await WinPeMediaFileTransfer.CopyAsync(file.SourcePath, destination, byteProgress.Full, cancellationToken).ConfigureAwait(false);
            verified = copied.Matches(file.Length, file.ContentHash);
        }
        else
        {
            await WinPeMediaFileTransfer.CopyAsync(file.SourcePath, destination, byteProgress.Half, cancellationToken).ConfigureAwait(false);
            verified = await MatchesAsync(destination, file.Length, file.ContentHash, cancellationToken, byteProgress.Half).ConfigureAwait(false);
        }

        if (!verified) throw new InvalidDataException("A copied custom image failed length or SHA256 verification.");
    }

    private static async Task<bool> MatchesAsync(string path, long length, string hash, CancellationToken cancellationToken,
        Action<long>? onBytes = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        CustomImagePathPolicy.ValidateNoReparsePoints(path);
        FileStream stream;
        try { stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 128 * 1024, useAsync: true); }
        catch (FileNotFoundException) { return false; }
        catch (DirectoryNotFoundException) { return false; }
        await using (stream)
        {
            if (stream.Length != length) return false;
            string actual = onBytes is null
                ? Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false))
                : await WinPeMediaFileTransfer.HashAsync(stream, onBytes, cancellationToken).ConfigureAwait(false);
            return string.Equals(actual, hash, StringComparison.OrdinalIgnoreCase);
        }
    }
}
