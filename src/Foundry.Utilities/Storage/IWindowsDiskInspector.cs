// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Utilities.Storage;

/// <summary>
/// Provides raw Windows disk inspection.
/// </summary>
public interface IWindowsDiskInspector
{
    /// <summary>
    /// Gets the disks reported by Windows.
    /// </summary>
    Task<IReadOnlyList<DiskInfo>> GetDisksAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Resolves the physical disk number that contains a path.
    /// </summary>
    /// <returns>The disk number, or null when the path has no matching physical partition, such as a WinPE RAM drive.</returns>
    Task<int?> ResolveDiskNumberForPathAsync(
        string path,
        CancellationToken cancellationToken = default);
}
