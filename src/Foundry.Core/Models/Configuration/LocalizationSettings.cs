namespace Foundry.Core.Models.Configuration;

/// <summary>
/// Stores user-authored localization preferences that are not OS catalog selectors.
/// </summary>
public sealed record LocalizationSettings
{
    /// <summary>
    /// Gets the optional default Windows time-zone identifier.
    /// </summary>
    public string? DefaultTimeZoneId { get; init; }
}
