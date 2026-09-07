// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.IO;
using System.Runtime.InteropServices;

namespace Foundry.Deploy.Services.Cache;

/// <summary>Reports observed volume readiness; null capacity never means sufficient space.</summary>
public sealed record VolumeStorageStatus(bool IsPresent, bool IsWritable, long? FreeBytes);

/// <summary>Inspects storage only when no verified payload can be reused.</summary>
public interface IVolumeStorageProbe
{
    VolumeStorageStatus Inspect(string directory);
}

/// <summary>Checks the actual destination volume and an owned temporary write before allocating a transfer.</summary>
public sealed class VolumeStorageProbe : IVolumeStorageProbe
{
    public VolumeStorageStatus Inspect(string directory)
    {
        try
        {
            directory = Path.GetFullPath(directory);
            Directory.CreateDirectory(directory);
            // Query the destination itself so volume GUIDs and mounted folders retain their identity.
            string capacityPath = Path.EndsInDirectorySeparator(directory) ? directory : directory + Path.DirectorySeparatorChar;
            if (!GetDiskFreeSpaceExW(capacityPath,
                out ulong availableBytes, out _, out _))
            {
                return new(Marshal.GetLastWin32Error() == 5, false, null);
            }
            long? freeBytes = availableBytes <= long.MaxValue ? (long)availableBytes : null;
            string probePath = Path.Combine(directory, $".foundry-write-{Guid.NewGuid():N}.tmp");
            using var stream = new FileStream(probePath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1, FileOptions.DeleteOnClose);
            stream.WriteByte(0);
            stream.Flush(flushToDisk: true);
            return new(true, true, freeBytes);
        }
        catch (DirectoryNotFoundException)
        {
            return new(false, false, null);
        }
        catch (UnauthorizedAccessException)
        {
            return new(true, false, null);
        }
        catch (IOException)
        {
            return new(false, false, null);
        }
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetDiskFreeSpaceExW(string directoryName, out ulong freeBytesAvailable,
        out ulong totalNumberOfBytes, out ulong totalNumberOfFreeBytes);
}
