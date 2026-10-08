// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.ComponentModel;
using System.Diagnostics;

namespace Foundry.Services.Updates;

/// <summary>
/// Detects other running Foundry processes that applying an update would terminate.
/// </summary>
/// <remarks>
/// Velopack force-stops every process running from the install directory when it applies a package, so instances are
/// matched by executable path. The launcher stub and unrelated executables that share the file name run from other
/// paths and never match. Detection fails open: when processes cannot be inspected, no other instance is reported and
/// the update flow behaves as it does for a single instance.
/// </remarks>
internal static class RunningInstanceProbe
{
    /// <summary>
    /// Determines whether another process started from this process's executable path is running.
    /// </summary>
    /// <returns>
    /// <see langword="true"/> when another matching process is found; <see langword="false"/> when none is found or
    /// when detection fails for any reason. The method never throws.
    /// </returns>
    public static bool IsAnotherInstanceRunning()
    {
        try
        {
            string? ownPath = Environment.ProcessPath;
            if (string.IsNullOrEmpty(ownPath))
            {
                return false;
            }

            Process[] candidates = Process.GetProcessesByName(Path.GetFileNameWithoutExtension(ownPath));
            try
            {
                foreach (Process candidate in candidates)
                {
                    if (IsOtherInstance(candidate, ownPath))
                    {
                        return true;
                    }
                }

                return false;
            }
            finally
            {
                foreach (Process candidate in candidates)
                {
                    candidate.Dispose();
                }
            }
        }
        catch (Exception)
        {
            // Best-effort guard on the startup path: any failure is treated as no other instance running, which is
            // the behavior before this guard existed.
            return false;
        }
    }

    private static bool IsOtherInstance(Process candidate, string ownPath)
    {
        try
        {
            return candidate.Id != Environment.ProcessId
                && string.Equals(candidate.MainModule?.FileName, ownPath, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception exception) when (exception is InvalidOperationException or Win32Exception or NotSupportedException)
        {
            // The process exited or denied module access during inspection, so it cannot be confirmed as an instance.
            return false;
        }
    }
}
