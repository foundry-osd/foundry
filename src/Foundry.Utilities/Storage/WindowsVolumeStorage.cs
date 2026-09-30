// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;

namespace Foundry.Utilities.Storage;

/// <summary>Queries destination directories and UNC shares without requiring a mapped drive letter.</summary>
public static class WindowsVolumeStorage
{
    /// <summary>Identifies the resolved destination volume and label, using its root or a neutral placeholder when unavailable.</summary>
    public static string GetDisplayName(string path)
    {
        string root = "—";
        try
        {
            root = GetRoot(path);
            string directory = GetExistingDirectory(path);
            var volumePath = new char[Math.Max(directory.Length + 1, 261)];
            if (!GetVolumePathName(directory, volumePath, (uint)volumePath.Length))
                return root.TrimEnd(Path.DirectorySeparatorChar);

            root = ReadNativeString(volumePath);
            string location = root.TrimEnd(Path.DirectorySeparatorChar);
            var volumeLabel = new char[261];
            if (!GetVolumeInformation(root, volumeLabel, (uint)volumeLabel.Length, nint.Zero, nint.Zero, nint.Zero, nint.Zero, 0))
                return location;

            string label = ReadNativeString(volumeLabel);
            return string.IsNullOrWhiteSpace(label) ? location : $"{location} ({label})";
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return root.TrimEnd(Path.DirectorySeparatorChar);
        }
    }

    /// <summary>Returns caller-available free bytes on the nearest existing destination directory, including quota restrictions.</summary>
    public static long GetAvailableBytes(string path)
    {
        string directory = GetExistingDirectory(path);
        if (!GetDiskFreeSpaceEx(directory, out ulong availableBytes, out _, out _))
            throw QueryFailure("Available volume space could not be determined.");
        return checked((long)availableBytes);
    }

    /// <summary>Returns the filesystem name reported for a local drive or network share.</summary>
    public static string GetFileSystem(string path)
    {
        string root = GetRoot(path);
        var fileSystem = new char[261];
        if (!GetVolumeInformation(root, nint.Zero, 0, nint.Zero, nint.Zero, nint.Zero, fileSystem, (uint)fileSystem.Length))
            throw QueryFailure("The volume filesystem could not be determined.");
        return ReadNativeString(fileSystem);
    }

    /// <summary>Retains existing directory paths so native free-space queries follow mounted volumes and reparse targets.</summary>
    internal static string GetExistingDirectory(string path)
    {
        string directory = Path.GetFullPath(path);
        string root = GetRoot(directory);
        while (directory.Length > root.Length)
        {
            try
            {
                if (File.GetAttributes(directory).HasFlag(FileAttributes.Directory)) break;
            }
            catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException)
            {
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // Keep inaccessible targets for the native query instead of reporting an ancestor's free space.
                break;
            }

            directory = Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(directory)) ?? root;
        }
        return Path.EndsInDirectorySeparator(directory) ? directory : directory + Path.DirectorySeparatorChar;
    }

    private static string GetRoot(string path)
    {
        string root = Path.GetPathRoot(Path.GetFullPath(path)) ?? throw new IOException("The volume root could not be resolved.");
        return Path.EndsInDirectorySeparator(root) ? root : root + Path.DirectorySeparatorChar;
    }

    private static IOException QueryFailure(string message) => new(message, new Win32Exception(Marshal.GetLastWin32Error()));

    private static string ReadNativeString(char[] buffer)
    {
        int terminator = Array.IndexOf(buffer, '\0');
        return new string(buffer, 0, terminator >= 0 ? terminator : buffer.Length);
    }

    [DllImport("kernel32.dll", EntryPoint = "GetVolumePathNameW", ExactSpelling = true, CharSet = CharSet.Unicode, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetVolumePathName(string path, [Out] char[] volumePath, uint bufferLength);

    [DllImport("kernel32.dll", EntryPoint = "GetDiskFreeSpaceExW", ExactSpelling = true, CharSet = CharSet.Unicode, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetDiskFreeSpaceEx(string directory, out ulong availableBytes, out ulong totalBytes, out ulong freeBytes);

    [DllImport("kernel32.dll", EntryPoint = "GetVolumeInformationW", ExactSpelling = true, CharSet = CharSet.Unicode, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetVolumeInformation(string root, nint volumeName, uint volumeNameLength,
        nint serialNumber, nint maximumComponentLength, nint flags, [Out] char[] fileSystem, uint fileSystemLength);

    [DllImport("kernel32.dll", EntryPoint = "GetVolumeInformationW", ExactSpelling = true, CharSet = CharSet.Unicode, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetVolumeInformation(string root, [Out] char[] volumeName, uint volumeNameLength,
        nint serialNumber, nint maximumComponentLength, nint flags, nint fileSystem, uint fileSystemLength);
}
