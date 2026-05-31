namespace Foundry.Deploy.Models.Configuration;

/// <summary>
/// Carries deploy-runtime localization settings that are not OS catalog selectors.
/// </summary>
public sealed record DeployLocalizationSettings
{
    /// <summary>
    /// Gets the optional default Windows time-zone identifier.
    /// </summary>
    public string? DefaultTimeZoneId { get; init; }
}
