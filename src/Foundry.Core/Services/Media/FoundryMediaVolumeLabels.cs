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

    /// <summary>
    /// Returns whether a volume label belongs to a Foundry USB drive that can hold imported content. Optical media are
    /// recognized by drive type instead, because an ISO keeps the label chosen by whoever mastered it.
    /// </summary>
    public static bool IsUsbContentVolume(string? label) =>
        string.Equals(label, Cache, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(label, SingleVolume, StringComparison.OrdinalIgnoreCase);
}
