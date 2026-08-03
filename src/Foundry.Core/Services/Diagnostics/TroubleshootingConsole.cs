// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Diagnostics;
using Foundry.Core.Models.Configuration;

namespace Foundry.Core.Services.Diagnostics;

/// <summary>
/// Matches the configured troubleshooting-console shortcut and opens the console for it.
/// </summary>
/// <remarks>
/// Keyboard state is passed in as plain values so both WPF runtime apps can call this without Foundry.Core
/// taking a UI framework dependency.
/// </remarks>
public static class TroubleshootingConsole
{
    /// <summary>
    /// The console stays open after the operator's commands finish, and it does not run a profile so the
    /// WinPE environment is left as-is.
    /// </summary>
    internal const string LaunchArguments = "-NoExit -NoProfile -ExecutionPolicy Bypass";

    /// <summary>
    /// Determines whether a key press matches the configured shortcut.
    /// </summary>
    /// <param name="settings">The configured shortcut.</param>
    /// <param name="pressedKey">The key that was pressed (for example <c>F8</c>).</param>
    /// <param name="isControlDown">Whether a Control key is held.</param>
    /// <param name="isAltDown">Whether an Alt key is held.</param>
    /// <param name="isShiftDown">Whether a Shift key is held.</param>
    /// <returns><see langword="true"/> when the console should open.</returns>
    public static bool IsShortcut(
        TroubleshootingConsoleSettings? settings,
        string? pressedKey,
        bool isControlDown,
        bool isAltDown,
        bool isShiftDown)
    {
        if (settings is not { IsEnabled: true } || string.IsNullOrWhiteSpace(pressedKey))
        {
            return false;
        }

        if (!string.Equals(pressedKey.Trim(), settings.Key.ToString(), StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        // The modifier must match exactly, so Ctrl+F8 never triggers a shortcut configured as plain F8.
        return settings.Modifier switch
        {
            TroubleshootingConsoleModifier.None => !isControlDown && !isAltDown && !isShiftDown,
            TroubleshootingConsoleModifier.Control => isControlDown && !isAltDown && !isShiftDown,
            TroubleshootingConsoleModifier.Alt => isAltDown && !isControlDown && !isShiftDown,
            TroubleshootingConsoleModifier.Shift => isShiftDown && !isControlDown && !isAltDown,
            TroubleshootingConsoleModifier.ControlShift => isControlDown && isShiftDown && !isAltDown,
            TroubleshootingConsoleModifier.ControlAlt => isControlDown && isAltDown && !isShiftDown,
            _ => false
        };
    }

    /// <summary>
    /// Formats the shortcut for display (for example <c>Ctrl+Shift+F8</c>).
    /// </summary>
    /// <param name="settings">The configured shortcut.</param>
    /// <returns>The display text for the shortcut.</returns>
    public static string FormatShortcut(TroubleshootingConsoleSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        string prefix = settings.Modifier switch
        {
            TroubleshootingConsoleModifier.Control => "Ctrl+",
            TroubleshootingConsoleModifier.Alt => "Alt+",
            TroubleshootingConsoleModifier.Shift => "Shift+",
            TroubleshootingConsoleModifier.ControlShift => "Ctrl+Shift+",
            TroubleshootingConsoleModifier.ControlAlt => "Ctrl+Alt+",
            _ => string.Empty
        };

        return prefix + settings.Key;
    }

    /// <summary>
    /// Opens an interactive PowerShell console.
    /// </summary>
    /// <returns><see langword="true"/> when the console was started.</returns>
    public static bool TryLaunch()
    {
        try
        {
            using Process? process = Process.Start(new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = LaunchArguments,
                UseShellExecute = true,
                WindowStyle = ProcessWindowStyle.Normal
            });

            return process is not null;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return false;
        }
    }
}
