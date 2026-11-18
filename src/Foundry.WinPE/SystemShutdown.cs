// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Runtime.InteropServices;

namespace Foundry.WinPE;

/// <summary>Tells whether Windows is shutting down, as when Foundry Deploy restarts the computer at the end of a deployment.</summary>
internal static class SystemShutdown
{
    private const int SmShuttingDown = 0x2000;

    /// <summary>Returns whether the current session is shutting down (<c>GetSystemMetrics(SM_SHUTTINGDOWN)</c> is non-zero).</summary>
    internal static bool IsInProgress() => GetSystemMetrics(SmShuttingDown) != 0;

    [DllImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern int GetSystemMetrics(int index);
}
