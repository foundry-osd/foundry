// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Utilities.Storage;

/// <summary>
/// Discovers file-system volumes visible to the current process.
/// </summary>
public interface IVolumeDiscovery
{
    /// <summary>
    /// Returns the currently visible volumes.
    /// </summary>
    IReadOnlyList<VolumeInfo> GetVolumes();
}
