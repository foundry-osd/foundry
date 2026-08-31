// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.IO;
using Foundry.Utilities.Storage;

namespace Foundry.Utilities.Diagnostics;

/// <summary>
/// Selects a persistent external destination for diagnostic support bundles.
/// </summary>
public static class SupportBundleDestinationPolicy
{
    private const string FoundryCacheVolumeLabel = "Foundry Cache";

    /// <summary>
    /// Selects a ready Foundry Cache volume or, when unavailable, a ready removable volume.
    /// </summary>
    /// <param name="volumes">The volumes visible to the current process.</param>
    /// <returns>The selected volume root, or <see langword="null"/> when no safe destination is available.</returns>
    public static string? SelectExternalDestination(IEnumerable<VolumeInfo> volumes)
    {
        ArgumentNullException.ThrowIfNull(volumes);

        VolumeInfo[] candidates = volumes
            .Where(static volume =>
                volume.IsReady &&
                !string.IsNullOrWhiteSpace(volume.RootPath) &&
                !IsWinPeRamDisk(volume.RootPath))
            .ToArray();

        return candidates.FirstOrDefault(static volume =>
                   string.Equals(volume.VolumeLabel, FoundryCacheVolumeLabel, StringComparison.OrdinalIgnoreCase))
               ?.RootPath
               ?? candidates.FirstOrDefault(static volume => volume.DriveType == DriveType.Removable)?.RootPath;
    }

    private static bool IsWinPeRamDisk(string rootPath)
    {
        string? pathRoot = Path.GetPathRoot(rootPath);
        return string.Equals(pathRoot?.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), "X:", StringComparison.OrdinalIgnoreCase);
    }
}
