// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Core.Services.WinPe;

/// <summary>Describes how much of a USB disk's content the inventory could show.</summary>
public enum WinPeUsbVolumeSummary
{
    /// <summary>At least one volume with a readable file system was found and can be listed.</summary>
    ReadableVolumes,

    /// <summary>The partition query succeeded and the disk has no partition.</summary>
    NoPartition,

    /// <summary>The disk has partitions without a readable volume, or reading them failed. It may hold data.</summary>
    UnreadableVolumes
}
