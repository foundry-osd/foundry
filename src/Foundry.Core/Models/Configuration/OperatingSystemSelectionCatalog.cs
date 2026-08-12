// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Core.Models.Configuration;

/// <summary>
/// Describes a supported Windows release and its base build number.
/// </summary>
public sealed record OperatingSystemReleaseDefinition(string Id, int Build);

/// <summary>
/// Lists the supported OS catalog values that administrators can preconfigure for deployment.
/// </summary>
public static class OperatingSystemSelectionCatalog
{
    private static readonly OperatingSystemReleaseDefinition[] Releases =
    [
        new("25H2", 26200),
        new("24H2", 26100),
        new("23H2", 22631)
    ];

    /// <summary>
    /// Gets the default Windows release offered to deployment operators.
    /// </summary>
    public const string DefaultReleaseId = "25H2";

    /// <summary>
    /// Gets the default license channel offered to deployment operators.
    /// </summary>
    public const string DefaultLicenseChannel = "RET";

    /// <summary>
    /// Gets the default edition target offered to deployment operators.
    /// </summary>
    public const string DefaultEdition = "Pro";

    /// <summary>
    /// Gets the supported Windows release identifiers, ordered from newest to oldest.
    /// </summary>
    public static IReadOnlyList<string> SupportedReleaseIds { get; } = Releases.Select(release => release.Id).ToArray();

    /// <summary>
    /// Gets the supported Windows releases and their base builds.
    /// </summary>
    public static IReadOnlyList<OperatingSystemReleaseDefinition> SupportedReleases => Releases;

    /// <summary>
    /// Gets the supported catalog license channel tokens.
    /// </summary>
    public static IReadOnlyList<string> SupportedLicenseChannels => WindowsEditionCatalog.SupportedLicenseChannels;

    /// <summary>
    /// Gets the supported target editions shown in the deployment catalog.
    /// </summary>
    public static IReadOnlyList<string> SupportedEditions => WindowsEditionCatalog.SupportedEditions;

    /// <summary>
    /// Finds a supported release by its invariant identifier.
    /// </summary>
    public static OperatingSystemReleaseDefinition? FindRelease(string? releaseId)
    {
        if (string.IsNullOrWhiteSpace(releaseId))
        {
            return null;
        }

        return Releases.FirstOrDefault(release =>
            release.Id.Equals(releaseId.Trim(), StringComparison.OrdinalIgnoreCase));
    }
}
