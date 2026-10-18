// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Core.Services.WinPe;

/// <summary>Describes how much of a USB disk's content the inventory could show.</summary>
public enum WinPeUsbVolumeSummary
{
    /// <summary>Every partition that may hold data has a readable volume, and those volumes can be listed.</summary>
    ReadableVolumes,

    /// <summary>The partition query succeeded and the disk has no partition.</summary>
    NoPartition,

    /// <summary>The disk has partitions without a readable volume, or reading them failed. It may hold data.</summary>
    UnreadableVolumes,

    /// <summary>
    /// Some volumes can be listed, but other partitions have no readable volume or reading them failed. The disk may
    /// hold more data than the list shows.
    /// </summary>
    ReadableAndUnreadableVolumes
}
