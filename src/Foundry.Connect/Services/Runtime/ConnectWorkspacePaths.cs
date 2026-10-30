// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.IO;
using Foundry.Core.Services.Runtime;
using Foundry.Utilities.Runtime;

namespace Foundry.Connect.Services.Runtime;

/// <summary>
/// Resolves Foundry.Connect configuration, log, and network profile roaming paths for WinPE and desktop runtimes.
/// </summary>
internal static class ConnectWorkspacePaths
{
    /// <summary>
    /// Gets a value indicating whether Foundry.Connect is running inside WinPE.
    /// </summary>
    public static bool IsWinPeRuntime()
    {
        return WinPeRuntimeDetector.IsWinPeRuntime();
    }

    /// <summary>
    /// Resolves a configuration file path from the WinPE workspace or application directory.
    /// </summary>
    /// <param name="fileName">Configuration file name.</param>
    /// <returns>The resolved configuration file path.</returns>
    public static string GetConfigFilePath(string fileName)
    {
        return IsWinPeRuntime()
            ? Path.Combine(WinPeWorkspaceLayout.ConfigDirectory, fileName)
            : Path.Combine(AppContext.BaseDirectory, fileName);
    }

    /// <summary>
    /// Enumerates directories that may contain startup logs for the current runtime.
    /// </summary>
    /// <returns>Candidate log directories in preferred search order.</returns>
    public static IEnumerable<string> EnumerateStartupLogDirectories()
    {
        if (IsWinPeRuntime())
        {
            yield return WinPeWorkspaceLayout.LogsDirectory;
        }

        yield return Path.Combine(Path.GetTempPath(), "Foundry", "Logs");
        yield return AppContext.BaseDirectory;
    }

    /// <summary>
    /// Resolves the runtime network profile roaming artifact directory.
    /// </summary>
    /// <returns>The artifact directory path.</returns>
    public static string GetNetworkProfileRoamingDirectory()
    {
        return IsWinPeRuntime()
            ? Path.Combine(WinPeWorkspaceLayout.RuntimeDirectory, "NetworkProfileRoaming")
            : Path.Combine(Path.GetTempPath(), "Foundry", "Runtime", "NetworkProfileRoaming");
    }
}
