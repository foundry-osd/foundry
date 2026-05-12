namespace Foundry.Core.Models.Configuration.Deploy;

/// <summary>
/// Describes Autopilot profile staging settings consumed by Foundry.Deploy.
/// </summary>
public sealed record DeployAutopilotSettings
{
    /// <summary>
    /// Gets whether Autopilot staging is enabled.
    /// </summary>
    public bool IsEnabled { get; init; }

    /// <summary>
    /// Gets the profile folder name selected for staging.
    /// </summary>
    public string? DefaultProfileFolderName { get; init; }
}
