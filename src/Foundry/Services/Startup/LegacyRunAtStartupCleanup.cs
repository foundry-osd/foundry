// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Microsoft.Win32;
using Serilog;

namespace Foundry.Services.Startup;

/// <summary>
/// Legacy cleanup for the removed "Run at startup" option, which registered Foundry OSD under the current user's Run key.
/// </summary>
internal static class LegacyRunAtStartupCleanup
{
    private const string RunKeyPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";

    private static readonly ILogger Logger = Log.ForContext(typeof(LegacyRunAtStartupCleanup));

    /// <summary>
    /// Removes the legacy Run value when its command points to the current Foundry executable. Failures are ignored.
    /// </summary>
    public static void TryRemove()
    {
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
            string? command = key?.GetValue(FoundryApplicationInfo.AppName) as string;
            if (key is null || command?.Contains(FoundryApplicationInfo.ExecutablePath, StringComparison.OrdinalIgnoreCase) != true)
            {
                return;
            }

            key.DeleteValue(FoundryApplicationInfo.AppName, throwOnMissingValue: false);
            Logger.Debug("Removed the legacy Run at startup registry value. ValueName={ValueName}", FoundryApplicationInfo.AppName);
        }
        catch (Exception)
        {
            // Best effort only: the removed option must never affect application startup.
        }
    }
}
