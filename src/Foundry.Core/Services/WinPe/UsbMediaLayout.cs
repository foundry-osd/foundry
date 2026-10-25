// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Core.Services.WinPe;

/// <summary>
/// Selects how a new USB drive is partitioned.
/// </summary>
public enum UsbMediaLayout
{
    /// <summary>A FAT32 BOOT partition and an NTFS <c>Foundry Cache</c> partition that keeps runtimes and downloads.</summary>
    WithCache,

    /// <summary>One FAT32 partition with the content of an ISO; devices started from it download everything again.</summary>
    WithoutCache
}
