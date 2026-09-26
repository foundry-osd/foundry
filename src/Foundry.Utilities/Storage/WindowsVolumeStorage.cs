// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;

namespace Foundry.Utilities.Storage;

/// <summary>Queries local drives and UNC share roots without requiring a mapped drive letter.</summary>
public static class WindowsVolumeStorage
{
    /// <summary>Returns free bytes available to the caller, including any per-user quota restrictions.</summary>
    public static long GetAvailableBytes(string path)
    {
        string root = GetRoot(path);
        if (!GetDiskFreeSpaceEx(root, out ulong availableBytes, out _, out _))
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
        int terminator = Array.IndexOf(fileSystem, '\0');
        return new string(fileSystem, 0, terminator >= 0 ? terminator : fileSystem.Length);
    }

    private static string GetRoot(string path)
    {
        string root = Path.GetPathRoot(Path.GetFullPath(path)) ?? throw new IOException("The volume root could not be resolved.");
        return Path.EndsInDirectorySeparator(root) ? root : root + Path.DirectorySeparatorChar;
    }

    private static IOException QueryFailure(string message) => new(message, new Win32Exception(Marshal.GetLastWin32Error()));

    [DllImport("kernel32.dll", EntryPoint = "GetDiskFreeSpaceExW", ExactSpelling = true, CharSet = CharSet.Unicode, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetDiskFreeSpaceEx(string directory, out ulong availableBytes, out ulong totalBytes, out ulong freeBytes);

    [DllImport("kernel32.dll", EntryPoint = "GetVolumeInformationW", ExactSpelling = true, CharSet = CharSet.Unicode, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetVolumeInformation(string root, nint volumeName, uint volumeNameLength,
        nint serialNumber, nint maximumComponentLength, nint flags, [Out] char[] fileSystem, uint fileSystemLength);
}
