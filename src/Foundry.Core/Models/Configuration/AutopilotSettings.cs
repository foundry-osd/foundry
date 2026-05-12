namespace Foundry.Core.Models.Configuration;

/// <summary>
/// Describes Autopilot profiles selected for staging into a deployed Windows image.
/// </summary>
public sealed record AutopilotSettings
{
    /// <summary>
    /// Gets whether Autopilot staging is enabled.
    /// </summary>
    public bool IsEnabled { get; init; }

    /// <summary>
    /// Gets the profile ID selected as the default staged profile.
    /// </summary>
    public string? DefaultProfileId { get; init; }

    /// <summary>
    /// Gets the available imported or downloaded Autopilot profiles.
    /// </summary>
    public IReadOnlyList<AutopilotProfileSettings> Profiles { get; init; } = [];
}
