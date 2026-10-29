// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.IO;

namespace Foundry.Connect.Configuration;

/// <summary>
/// Resolves provisioned Wi-Fi asset paths from the runtime configuration location.
/// </summary>
internal static class ProvisionedWifiProfileResolver
{
    /// <summary>
    /// Resolves a configured asset path relative to the configuration file directory.
    /// When no configuration file path is available, relative assets are resolved from the application base directory.
    /// </summary>
    /// <param name="value">Configured path value.</param>
    /// <param name="configurationPath">Configuration file path that anchors relative assets.</param>
    /// <returns>An absolute path, or <see langword="null"/> when no value is configured.</returns>
    public static string? ResolveAssetPath(string? value, string? configurationPath)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        string trimmed = value.Trim();
        if (Path.IsPathRooted(trimmed))
        {
            return Path.GetFullPath(trimmed);
        }

        string? configurationDirectoryPath = null;
        if (!string.IsNullOrWhiteSpace(configurationPath))
        {
            configurationDirectoryPath = Path.GetDirectoryName(configurationPath);
        }

        configurationDirectoryPath ??= AppContext.BaseDirectory;
        return Path.GetFullPath(Path.Combine(configurationDirectoryPath, trimmed));
    }
}
