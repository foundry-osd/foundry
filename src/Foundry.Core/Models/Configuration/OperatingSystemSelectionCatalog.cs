namespace Foundry.Core.Models.Configuration;

/// <summary>
/// Lists the supported OS catalog values that administrators can preconfigure for deployment.
/// </summary>
public static class OperatingSystemSelectionCatalog
{
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
    public static IReadOnlyList<string> SupportedReleaseIds { get; } =
    [
        "25H2",
        "24H2",
        "23H2"
    ];

    /// <summary>
    /// Gets the supported catalog license channel tokens.
    /// </summary>
    public static IReadOnlyList<string> SupportedLicenseChannels { get; } =
    [
        "RET",
        "VOL"
    ];

    /// <summary>
    /// Gets the supported target editions shown in the deployment catalog.
    /// </summary>
    public static IReadOnlyList<string> SupportedEditions { get; } =
    [
        "Home",
        "Home N",
        "Home Single Language",
        "Education",
        "Education N",
        "Pro",
        "Pro N",
        "Enterprise",
        "Enterprise N"
    ];
}
