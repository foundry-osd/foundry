// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Globalization;
using Foundry.Core.Models.Configuration;

namespace Foundry.Core.Services.Runtime;

/// <summary>Compares boot-media authoring and runtime releases when a runtime can receive an update.</summary>
public static class BootMediaFreshnessPolicy
{
    /// <summary>Parses a published YY.M.D.build release with a valid date and build number.</summary>
    public static bool TryParseReleaseVersion(string? value, out Version? version)
    {
        version = null;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        string[] components = value.Trim().Split('.');
        if (components.Length != 4 ||
            !TryParseComponent(components[0], out int year) || year > 99 ||
            !TryParseComponent(components[1], out int month) || month is < 1 or > 12 ||
            !TryParseComponent(components[2], out int day) || day < 1 || day > DateTime.DaysInMonth(2000 + year, month) ||
            !TryParseComponent(components[3], out int build) || build is < 1 or > 65534)
        {
            return false;
        }

        version = new Version(year, month, day, build);
        return true;
    }

    /// <summary>
    /// Recommends rebuilding eligible boot media when its authoring release is unknown, older than the running runtime,
    /// or older than the latest published Foundry release.
    /// </summary>
    /// <param name="authoringVersion">Foundry release that created the boot media, read from its configuration.</param>
    /// <param name="runtimeVersion">Release of the running runtime; an unpublished build never receives advice.</param>
    /// <param name="isEligibleRuntime">Whether the runtime environment can receive advice at all.</param>
    /// <param name="latestReleaseVersion">
    /// Latest published Foundry release, or <see langword="null"/> when it is unknown. Boot media run the release that
    /// created them, so only this input reveals that a newer release exists. It never replaces
    /// <see cref="BootMediaUpdateReason.UnknownAuthoringVersion"/>.
    /// </param>
    public static BootMediaUpdateReason Evaluate(
        string? authoringVersion,
        string? runtimeVersion,
        bool isEligibleRuntime,
        string? latestReleaseVersion = null)
    {
        if (!isEligibleRuntime || !TryParseReleaseVersion(runtimeVersion, out Version? runtimeRelease))
        {
            return BootMediaUpdateReason.None;
        }

        if (!TryParseReleaseVersion(authoringVersion, out Version? authoringRelease))
        {
            return BootMediaUpdateReason.UnknownAuthoringVersion;
        }

        bool isLatestReleaseNewer = TryParseReleaseVersion(latestReleaseVersion, out Version? latestRelease) &&
            latestRelease!.CompareTo(authoringRelease) > 0;
        return isLatestReleaseNewer || runtimeRelease!.CompareTo(authoringRelease) > 0
            ? BootMediaUpdateReason.NewerRelease
            : BootMediaUpdateReason.None;
    }

    private static bool TryParseComponent(string value, out int component)
    {
        component = 0;
        return value.Length > 0 &&
            value.All(char.IsAsciiDigit) &&
            int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out component);
    }
}
