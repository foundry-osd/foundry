namespace Foundry.Core.Models.Configuration;

/// <summary>
/// Stores user-authored OS language and time-zone preferences for deployment.
/// </summary>
public sealed record LocalizationSettings
{
    /// <summary>
    /// Gets the language codes exposed to deployment selection logic.
    /// </summary>
    public IReadOnlyList<string> VisibleLanguageCodes { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Gets the optional default language code selected for the deployed OS.
    /// </summary>
    public string? DefaultLanguageCodeOverride { get; init; }

    /// <summary>
    /// Gets the optional default Windows time-zone identifier.
    /// </summary>
    public string? DefaultTimeZoneId { get; init; }

    /// <summary>
    /// Gets a value indicating whether deployment should expose only the default language.
    /// </summary>
    public bool ForceSingleVisibleLanguage { get; init; }
}
