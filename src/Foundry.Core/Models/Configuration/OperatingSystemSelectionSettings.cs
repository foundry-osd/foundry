namespace Foundry.Core.Models.Configuration;

/// <summary>
/// Stores administrator-authored constraints and defaults for the deployment OS catalog selectors.
/// </summary>
public sealed record OperatingSystemSelectionSettings
{
    /// <summary>
    /// Gets language codes that remain selectable in Foundry.Deploy. An empty list allows all catalog languages.
    /// </summary>
    public IReadOnlyList<string> AllowedLanguageCodes { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Gets the optional language code preselected in Foundry.Deploy.
    /// </summary>
    public string? DefaultLanguageCode { get; init; }

    /// <summary>
    /// Gets Windows release IDs that remain selectable in Foundry.Deploy. An empty list allows all supported releases.
    /// </summary>
    public IReadOnlyList<string> AllowedReleaseIds { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Gets the optional Windows release ID preselected in Foundry.Deploy.
    /// </summary>
    public string? DefaultReleaseId { get; init; }

    /// <summary>
    /// Gets license channel tokens that remain selectable in Foundry.Deploy. An empty list allows all catalog channels.
    /// </summary>
    public IReadOnlyList<string> AllowedLicenseChannels { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Gets the optional license channel token preselected in Foundry.Deploy.
    /// </summary>
    public string? DefaultLicenseChannel { get; init; }

    /// <summary>
    /// Gets target editions that remain selectable in Foundry.Deploy. An empty list allows all supported editions.
    /// </summary>
    public IReadOnlyList<string> AllowedEditions { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Gets the optional target edition preselected in Foundry.Deploy.
    /// </summary>
    public string? DefaultEdition { get; init; }
}
