// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Collections.Generic;
using Foundry.Core.Models.Configuration;

namespace Foundry.Deploy.Models;

internal static class OperatingSystemSupportMatrix
{
    public const string SupportedWindowsRelease = "11";
    public const string DefaultReleaseId = OperatingSystemSelectionCatalog.DefaultReleaseId;
    public const string DefaultLicenseChannel = "RET";
    public const string DefaultEdition = "Pro";

    private static readonly HashSet<string> SupportedReleaseIds = new(OperatingSystemSelectionCatalog.SupportedReleaseIds, StringComparer.OrdinalIgnoreCase);

    private static readonly string[] SupportedLicenseChannelOrder =
    [
        "RET",
        "VOL"
    ];

    public static IReadOnlyList<string> ReleaseSearchOrder => OperatingSystemSelectionCatalog.SupportedReleaseIds;

    public static IReadOnlyList<string> LicenseChannelOrder => SupportedLicenseChannelOrder;

    public static IReadOnlyList<string> EditionOrder => WindowsEditionCatalog.SupportedEditions;

    public static bool IsSupported(OperatingSystemCatalogItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        return IsSupportedWindowsRelease(item.WindowsRelease) && IsSupportedReleaseId(item.ReleaseId);
    }

    public static bool IsSupportedWindowsRelease(string windowsRelease)
    {
        return !string.IsNullOrWhiteSpace(windowsRelease) &&
               SupportedWindowsRelease.Equals(windowsRelease.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsSupportedReleaseId(string releaseId)
    {
        return !string.IsNullOrWhiteSpace(releaseId) &&
               SupportedReleaseIds.Contains(releaseId.Trim());
    }
}
