// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Security.Cryptography;
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
        : this(GetAvailableBytes, new WindowsDiskInspector(new ProcessRunner()).ResolveDiskNumberForPathAsync)
    {
    }

    internal WinPeCustomImageMediaService(Func<string, long> availableBytes,
        Func<string, CancellationToken, Task<int?>> resolveDisk)
    {
        this.availableBytes = availableBytes;
        this.resolveDisk = resolveDisk;
    }

    /// <summary>Protects all prepared build inputs when their storage has been redirected onto removable media.</summary>
    public async Task ValidateInputDisksAsync(IEnumerable<string> paths, int targetDisk, CancellationToken cancellationToken)
    {
        foreach (string path in paths.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();
            EnsureNoReparsePoints(path);
            int? sourceDisk = await resolveDisk(path, cancellationToken).ConfigureAwait(false);
            if (!sourceDisk.HasValue || sourceDisk.Value == targetDisk)
                throw new InvalidDataException("A media input is on the target disk or its physical disk could not be verified.");
        }
    }

    /// <summary>Rejects drive-letter reuse or an unresolved data volume before writing external image content.</summary>
    public async Task ValidateDestinationDiskAsync(string root, int targetDisk, CancellationToken cancellationToken)
    {
        EnsureNoReparsePoints(root);
        if (await resolveDisk(root, cancellationToken).ConfigureAwait(false) != targetDisk)
            throw new InvalidDataException("The custom-image destination no longer belongs to the selected USB disk.");
    }

    /// <summary>Verifies every retained source before a destructive operation or output publication.</summary>
    public async Task ValidateSourcesAsync(WinPeCustomImageMediaLease package, CancellationToken cancellationToken = default)
    {
        package.ThrowIfDisposed();
        foreach (WinPeCustomImageMediaFile file in package.Files)
        {
            ValidateRelativePath(file.RelativePath);
            EnsureNoReparsePoints(file.SourcePath);
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
            string destination = ResolveDestination(destinationRoot, file.RelativePath);
            if (!await MatchesAsync(destination, file.Length, file.ContentHash, cancellationToken).ConfigureAwait(false))
                required = checked(required + file.Length);
        }
        return required;
    }

    /// <summary>Reserves replacement runtime payloads as well as new image bytes before changing BOOT.</summary>
    public async Task ValidateCapacityAsync(WinPeCustomImageMediaLease package, string root, long additionalBytes, CancellationToken cancellationToken)
    {
        long required = checked(await GetRequiredBytesAsync(package, root, cancellationToken).ConfigureAwait(false) + additionalBytes);
        if (availableBytes(root) < required) throw new IOException("The data volume has insufficient free space for the custom images and runtime payloads.");
    }

    /// <summary>Stages new files, verifies them, and publishes the manifest last; cancellation never removes old generations.</summary>
    public async Task PublishAsync(WinPeCustomImageMediaLease package, string destinationRoot,
        CancellationToken cancellationToken = default, IProgress<WinPeMediaProgress>? progress = null)
    {
        package.ThrowIfDisposed();
        await ValidateSourcesAsync(package, cancellationToken).ConfigureAwait(false);
        string customRoot = ResolveDestination(destinationRoot, Path.Combine("Foundry", "Images", "Custom"));
        Directory.CreateDirectory(customRoot);
        string lockPath = ResolveDestination(customRoot, ".publish.lock");
        using var publicationLease = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        long required = await GetRequiredBytesAsync(package, destinationRoot, cancellationToken).ConfigureAwait(false);
        if (availableBytes(destinationRoot) < required) throw new IOException("The data volume has insufficient free space for the custom images and staging reserve.");
        string pending = Path.Combine(customRoot, ".pending-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(pending);
        try
        {
            var staged = new List<(string Temporary, string Destination)>();
            long completed = 0;
            foreach (WinPeCustomImageMediaFile file in package.Files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string destination = ResolveDestination(destinationRoot, file.RelativePath);
                if (!await MatchesAsync(destination, file.Length, file.ContentHash, cancellationToken).ConfigureAwait(false))
                {
                    string temporary = Path.Combine(pending, Guid.NewGuid().ToString("N"));
                    await CopyVerifiedAsync(file, temporary, cancellationToken).ConfigureAwait(false);
                    staged.Add((temporary, destination));
                }
                completed = checked(completed + file.Length);
                progress?.Report(new WinPeMediaProgress { Percent = (int)(completed * 90.0 / Math.Max(1, package.TotalBytes)), Status = "Staging custom Windows images." });
            }
            foreach ((string temporary, string destination) in staged)
            {
                cancellationToken.ThrowIfCancellationRequested();
                EnsureNoReparsePoints(destination);
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                File.Move(temporary, destination, overwrite: true);
            }
            string manifest = ResolveDestination(destinationRoot, package.ManifestRelativePath);
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
            progress?.Report(new WinPeMediaProgress { Percent = 100, Status = "Custom Windows images verified." });
        }
        finally
        {
            try { Directory.Delete(pending, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private static async Task CopyVerifiedAsync(WinPeCustomImageMediaFile file, string destination, CancellationToken cancellationToken)
    {
        await using (var source = new FileStream(file.SourcePath, FileMode.Open, FileAccess.Read, FileShare.Read, 128 * 1024, useAsync: true))
        await using (var target = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None, 128 * 1024, useAsync: true))
        {
            await source.CopyToAsync(target, cancellationToken).ConfigureAwait(false);
            target.Flush(flushToDisk: true);
        }
        if (!await MatchesAsync(destination, file.Length, file.ContentHash, cancellationToken).ConfigureAwait(false))
            throw new InvalidDataException("A copied custom image failed length or SHA256 verification.");
    }

    private static async Task<bool> MatchesAsync(string path, long length, string hash, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureNoReparsePoints(path);
        FileStream stream;
        try { stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 128 * 1024, useAsync: true); }
        catch (FileNotFoundException) { return false; }
        catch (DirectoryNotFoundException) { return false; }
        await using (stream)
        {
            if (stream.Length != length) return false;
            string actual = Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false));
            return string.Equals(actual, hash, StringComparison.OrdinalIgnoreCase);
        }
    }

    internal static string ResolveDestination(string root, string relativePath)
    {
        ValidateRelativePath(relativePath);
        string fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        string path = Path.GetFullPath(Path.Combine(fullRoot, relativePath));
        if (!path.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Custom image media path escapes the destination root.");
        EnsureNoReparsePoints(path);
        return path;
    }

    private static void ValidateRelativePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || Path.IsPathRooted(path) || path.Contains(':') ||
            path.Split(['/', '\\']).Any(segment => segment is "" or "." or ".."))
            throw new InvalidDataException("Custom image media paths must be canonical relative paths.");
    }

    internal static void EnsureNoReparsePoints(string path)
    {
        for (string? current = Path.GetFullPath(path); current is not null; current = Path.GetDirectoryName(current))
        {
            try
            {
                if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidDataException("Custom image media paths must not traverse reparse points.");
            }
            catch (FileNotFoundException) { }
            catch (DirectoryNotFoundException) { }
        }
    }

    internal static long GetAvailableBytes(string path)
    {
        string root = Path.GetPathRoot(Path.GetFullPath(path)) ?? throw new IOException("Volume capacity could not be resolved.");
        return new DriveInfo(root).AvailableFreeSpace;
    }
}
