// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Runtime.InteropServices;

namespace Foundry.PostInstall.Windows;

/// <summary>Hands a file to Session Manager, which deletes it early in the next boot before other processes can open it.</summary>
internal static class PendingFileDeletion
{
    private const uint MoveFileDelayUntilReboot = 0x4;

    /// <summary>
    /// Registers <paramref name="path"/> for deletion at the next restart. Requires an administrator or LocalSystem;
    /// success only means the request was recorded, not that the file is gone.
    /// </summary>
    public static bool ScheduleAtRestart(string path) => MoveFileEx(path, null, MoveFileDelayUntilReboot);

    [DllImport("kernel32.dll", EntryPoint = "MoveFileExW", ExactSpelling = true, CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool MoveFileEx(string existingFileName, string? newFileName, uint flags);
}
