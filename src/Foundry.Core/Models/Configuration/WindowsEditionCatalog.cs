// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Core.Models.Configuration;

/// <summary>
/// Describes a selectable Windows edition and its invariant deployment metadata.
/// </summary>
public sealed record WindowsEditionDefinition(
    string Name,
    string EditionId,
    IReadOnlyList<string> LicenseChannels,
    IReadOnlyList<string> Architectures);

/// <summary>
/// Provides the supported Windows edition, DISM edition ID, media channel, and architecture mappings.
/// </summary>
public static class WindowsEditionCatalog
{
    private static readonly WindowsEditionDefinition[] Definitions =
    [
        new("Home", "Core", ["RET"], ["x64", "arm64"]),
        new("Home N", "CoreN", ["RET"], ["x64", "arm64"]),
        new("Home Single Language", "CoreSingleLanguage", ["RET"], ["x64", "arm64"]),
        new("Home China", "CoreCountrySpecific", ["RET"], ["x64"]),
        new("Education", "Education", ["RET", "VOL"], ["x64", "arm64"]),
        new("Education N", "EducationN", ["RET", "VOL"], ["x64", "arm64"]),
        new("Pro", "Professional", ["RET", "VOL"], ["x64", "arm64"]),
        new("Pro N", "ProfessionalN", ["RET", "VOL"], ["x64", "arm64"]),
        new("Enterprise", "Enterprise", ["VOL"], ["x64", "arm64"]),
        new("Enterprise N", "EnterpriseN", ["VOL"], ["x64", "arm64"])
    ];

    /// <summary>
    /// Gets all supported edition definitions in display order.
    /// </summary>
    public static IReadOnlyList<WindowsEditionDefinition> SupportedDefinitions => Definitions;

    /// <summary>
    /// Gets all supported license channels in display order.
    /// </summary>
    public static IReadOnlyList<string> SupportedLicenseChannels { get; } = Definitions
        .SelectMany(definition => definition.LicenseChannels)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToArray();

    /// <summary>
    /// Gets all supported friendly edition names in display order.
    /// </summary>
    public static IReadOnlyList<string> SupportedEditions { get; } = Definitions.Select(definition => definition.Name).ToArray();

    /// <summary>
    /// Finds a supported edition by its friendly name.
    /// </summary>
    public static WindowsEditionDefinition? Find(string? edition)
    {
        if (string.IsNullOrWhiteSpace(edition))
        {
            return null;
        }

        return Definitions.FirstOrDefault(definition =>
            definition.Name.Equals(edition.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Gets the license channels supported by at least one selected edition.
    /// </summary>
    public static IReadOnlyList<string> GetCompatibleLicenseChannels(IEnumerable<string> editions)
    {
        HashSet<string> channels = editions
            .Select(Find)
            .Where(definition => definition is not null)
            .SelectMany(definition => definition!.LicenseChannels)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return SupportedLicenseChannels
            .Where(channels.Contains)
            .ToArray();
    }

    /// <summary>
    /// Gets the license channels required by selected editions that only exist in one channel.
    /// </summary>
    public static IReadOnlyList<string> GetRequiredLicenseChannels(IEnumerable<string> editions)
    {
        HashSet<string> channels = editions
            .Select(Find)
            .Where(definition => definition?.LicenseChannels.Count == 1)
            .Select(definition => definition!.LicenseChannels[0])
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return SupportedLicenseChannels
            .Where(channels.Contains)
            .ToArray();
    }

    /// <summary>
    /// Determines whether an edition is supported by a media channel and processor architecture.
    /// </summary>
    public static bool IsSupported(string edition, string licenseChannel, string architecture)
    {
        WindowsEditionDefinition? definition = Find(edition);
        return definition is not null &&
               definition.LicenseChannels.Contains(licenseChannel, StringComparer.OrdinalIgnoreCase) &&
               definition.Architectures.Contains(architecture, StringComparer.OrdinalIgnoreCase);
    }
}
