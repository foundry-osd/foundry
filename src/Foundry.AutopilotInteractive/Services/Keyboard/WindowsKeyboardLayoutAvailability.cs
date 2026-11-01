// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.IO;
using System.Security;
using Microsoft.Win32;

namespace Foundry.AutopilotInteractive.Services.Keyboard;

/// <summary>
/// Reads keyboard layout availability from the local machine registry and the Windows system folder. Read-only: it never
/// changes a Windows input setting.
/// </summary>
internal sealed class WindowsKeyboardLayoutAvailability : IKeyboardLayoutAvailability
{
    private const string KeyboardLayoutsRegistryPath = @"SYSTEM\CurrentControlSet\Control\Keyboard Layouts";

    /// <inheritdoc />
    public string? GetRegisteredLayoutFile(string layoutId)
    {
        try
        {
            using RegistryKey? key = Registry.LocalMachine.OpenSubKey($@"{KeyboardLayoutsRegistryPath}\{layoutId}");
            return key?.GetValue("Layout File") as string;
        }
        catch (Exception exception) when (exception is SecurityException or UnauthorizedAccessException or IOException)
        {
            return null;
        }
    }

    /// <inheritdoc />
    public bool SystemFileExists(string layoutFile)
    {
        // A registry value naming a path instead of a file name is not a layout Windows loads from System32.
        return string.Equals(Path.GetFileName(layoutFile), layoutFile, StringComparison.Ordinal)
            && File.Exists(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32", layoutFile));
    }
}
