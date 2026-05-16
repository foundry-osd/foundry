namespace Foundry.Core.Models.Configuration;

/// <summary>
/// Describes the selected Autopilot provisioning method and its persistent settings.
/// </summary>
public sealed record AutopilotSettings
{
    /// <summary>
    /// Gets whether Autopilot provisioning is enabled.
    /// </summary>
    public bool IsEnabled { get; init; }

    /// <summary>
    /// Gets the provisioning method used when Autopilot is enabled.
    /// </summary>
    public AutopilotProvisioningMode ProvisioningMode { get; init; } = AutopilotProvisioningMode.JsonProfile;

    /// <summary>
    /// Gets the profile ID selected as the default staged profile.
    /// </summary>
    public string? DefaultProfileId { get; init; }

    /// <summary>
    /// Gets the available imported or downloaded Autopilot profiles.
    /// </summary>
    public IReadOnlyList<AutopilotProfileSettings> Profiles { get; init; } = [];

    /// <summary>
    /// Gets tenant app registration metadata used by hardware hash upload mode.
    /// </summary>
    public AutopilotHardwareHashUploadSettings HardwareHashUpload { get; init; } = new();
}
