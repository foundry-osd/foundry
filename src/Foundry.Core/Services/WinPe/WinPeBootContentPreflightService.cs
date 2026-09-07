// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Core.Services.WinPe;

/// <summary>Measures finalized boot files under read leases and rejects infeasible USB layouts before formatting.</summary>
internal static class WinPeBootContentPreflightService
{
    internal const ulong Mebibyte = 1024 * 1024;
    internal const ulong MinimumBootBytes = 2048 * Mebibyte;
    internal const ulong MaximumBootBytes = 32768 * Mebibyte;
    internal const ulong CacheReserveBytes = 256 * Mebibyte;
    internal const ulong PartitionOverheadBytes = 2 * Mebibyte;

    /// <summary>Retains source leases in the successful result until the caller finishes copying media.</summary>
    public static WinPeResult<WinPeBootContentPreflightResult> Evaluate(
        WinPeBuildArtifact artifact, IEnumerable<long> runtimeLengths, ulong diskBytes,
        ulong? existingBootBytes, ulong? existingCacheFreeBytes, bool useBootEx,
        CancellationToken cancellationToken)
    {
        var handles = new List<FileStream>();
        var measuredFiles = new List<WinPeMeasuredBootFile>();
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (useBootEx)
            {
                handles.Add(OpenSource(Path.Combine(artifact.WorkingDirectoryPath, "bootbins", "bootmgfw_EX.efi")));
                if (handles[^1].Length == 0) { return Failure("The required BootEx EFI source is empty."); }
                WinPeResult bootEx = WinPeUsbMediaService.ConfigureBootFiles(artifact.MediaDirectoryPath, artifact);
                if (!bootEx.IsSuccess) { return WinPeResult<WinPeBootContentPreflightResult>.Failure(bootEx.Error!); }
            }
            WinPeResult requiredFiles = WinPeUsbMediaService.VerifyBootArtifacts(artifact.MediaDirectoryPath, artifact.Architecture);
            if (!requiredFiles.IsSuccess) { return WinPeResult<WinPeBootContentPreflightResult>.Failure(requiredFiles.Error!); }
            WinPeResult layout = WinPeUsbMediaService.VerifyBootPartitionLayout(artifact.MediaDirectoryPath);
            if (!layout.IsSuccess) { return WinPeResult<WinPeBootContentPreflightResult>.Failure(layout.Error!); }

            string mediaRoot = Path.GetFullPath(artifact.MediaDirectoryPath);
            HashSet<string> requiredPaths = new(StringComparer.OrdinalIgnoreCase)
            {
                Path.Combine(mediaRoot, "sources", "boot.wim"),
                Path.Combine(mediaRoot, "boot", "BCD"),
                Path.Combine(mediaRoot, "EFI", "Boot", artifact.Architecture.ToBootEfiName())
            };
            HashSet<string> measuredPaths = new(StringComparer.OrdinalIgnoreCase);
            var lengths = new List<long>();
            var directories = new Stack<string>();
            directories.Push(mediaRoot);
            while (directories.TryPop(out string? directory))
            {
                cancellationToken.ThrowIfCancellationRequested();
                RejectReparsePoint(directory);
                foreach (string path in Directory.EnumerateFileSystemEntries(directory))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    FileAttributes attributes = RejectReparsePoint(path);
                    if ((attributes & FileAttributes.Directory) != 0) { directories.Push(path); }
                    else
                    {
                        FileStream stream = OpenSource(path);
                        handles.Add(stream);
                        measuredPaths.Add(path);
                        if (stream.Length == 0 && requiredPaths.Contains(path))
                        {
                            return Failure("A required final BOOT file is empty.");
                        }
                        lengths.Add(stream.Length);
                        measuredFiles.Add(new WinPeMeasuredBootFile(Path.GetRelativePath(mediaRoot, path), stream));
                    }
                }
            }

            if (!requiredPaths.IsSubsetOf(measuredPaths))
            {
                return Failure("Required final BOOT files changed during measurement.");
            }

            WinPeResult<WinPeBootContentPreflightResult> result = EvaluateSizes(
                lengths, runtimeLengths, diskBytes, existingBootBytes, existingCacheFreeBytes);
            if (result.IsSuccess)
            {
                result.Value!.SourceHandles.AddRange(handles);
                result.Value.Files.AddRange(measuredFiles);
                handles.Clear();
            }
            return result;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Security.SecurityException or ArgumentException or NotSupportedException)
        {
            return Failure("Final boot media cannot be read and measured safely.");
        }
        finally
        {
            foreach (FileStream stream in handles) { stream.Dispose(); }
        }
    }

    internal static WinPeResult<WinPeBootContentPreflightResult> EvaluateSizes(
        IEnumerable<long> bootLengths, IEnumerable<long> runtimeLengths, ulong diskBytes,
        ulong? existingBootBytes = null, ulong? existingCacheFreeBytes = null)
    {
        try
        {
            if (diskBytes < WinPeUsbMediaService.MinimumUsbDiskSizeBytes)
            {
                return Failure("USB media requires a disk of at least 16 GiB.");
            }
            ulong bootBytes = 0, largest = 0, runtimeBytes = 0;
            foreach (long length in bootLengths)
            {
                if (length < 0 || (ulong)length > uint.MaxValue)
                {
                    return Failure("A final BOOT file exceeds the FAT32 single-file limit or has an unknown size.");
                }
                bootBytes = checked(bootBytes + (ulong)length);
                largest = Math.Max(largest, (ulong)length);
            }
            if (bootBytes == 0) { return Failure("Final BOOT content is empty."); }
            foreach (long length in runtimeLengths)
            {
                if (length < 0) { return Failure("A prepared runtime file has an unknown size."); }
                runtimeBytes = checked(runtimeBytes + (ulong)length);
            }
            ulong reserve = Math.Max(CacheReserveBytes, checked((bootBytes + 9) / 10));
            ulong requiredBoot = checked(bootBytes + reserve);
            ulong bootPartition = Math.Max(MinimumBootBytes,
                checked((requiredBoot + Mebibyte - 1) / Mebibyte * Mebibyte));
            ulong requiredCache = checked(runtimeBytes + CacheReserveBytes);
            if (existingBootBytes.HasValue != existingCacheFreeBytes.HasValue)
            {
                return Failure("Existing BOOT capacity and CACHE free space must both be available.");
            }
            if (existingBootBytes.HasValue)
            {
                bootPartition = existingBootBytes.Value;
                if (requiredBoot > bootPartition || requiredCache > existingCacheFreeBytes!.Value)
                {
                    return Failure("Prepared content does not fit the existing BOOT partition and available CACHE space.");
                }
            }
            if (bootPartition < MinimumBootBytes || bootPartition > MaximumBootBytes ||
                checked(bootPartition + requiredCache + PartitionOverheadBytes) > diskBytes)
            {
                return Failure("Prepared content and reserve do not fit a supported FAT32 BOOT and CACHE layout.");
            }
            return WinPeResult<WinPeBootContentPreflightResult>.Success(new()
            {
                BootFileBytes = bootBytes,
                LargestBootFileBytes = largest,
                RuntimeCacheBytes = runtimeBytes,
                BootPartitionSizeBytes = bootPartition,
                RequiredBootBytes = requiredBoot,
                RequiredCacheFreeBytes = requiredCache
            });
        }
        catch (OverflowException) { return Failure("Prepared media sizes overflow the supported capacity calculation."); }
    }

    private static FileStream OpenSource(string path)
    {
        RejectReparsePoint(path);
        return new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
    }

    private static FileAttributes RejectReparsePoint(string path)
    {
        FileAttributes attributes = File.GetAttributes(path);
        if ((attributes & FileAttributes.ReparsePoint) != 0) { throw new IOException("Boot media contains a reparse point."); }
        return attributes;
    }

    private static WinPeResult<WinPeBootContentPreflightResult> Failure(string message) =>
        WinPeResult<WinPeBootContentPreflightResult>.Failure(WinPeErrorCodes.ValidationFailed, message);
}
