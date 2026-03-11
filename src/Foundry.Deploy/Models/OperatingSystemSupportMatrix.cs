using System.Collections.Generic;

namespace Foundry.Deploy.Models;

internal static class OperatingSystemSupportMatrix
{
    public const string SupportedWindowsRelease = "11";
    public const string DefaultReleaseId = "25H2";

    private static readonly string[] SupportedReleaseIdOrder =
    [
        "25H2",
        "24H2",
        "23H2"
    ];

    private static readonly HashSet<string> SupportedReleaseIds = new(SupportedReleaseIdOrder, StringComparer.OrdinalIgnoreCase);

    public static IReadOnlyList<string> ReleaseSearchOrder => SupportedReleaseIdOrder;

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
