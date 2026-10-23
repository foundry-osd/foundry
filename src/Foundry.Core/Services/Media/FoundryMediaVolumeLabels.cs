// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Core.Services.Media;

/// <summary>
/// Volume labels that Foundry OSD writes on USB drives and that Foundry Deploy uses to find imported content.
/// </summary>
public static class FoundryMediaVolumeLabels
{
    /// <summary>The NTFS data partition of a USB drive with cache.</summary>
    public const string Cache = "Foundry Cache";

    /// <summary>The only partition of a USB drive without cache; FAT32 labels are at most 11 uppercase characters.</summary>
    public const string SingleVolume = "FOUNDRY";
}
