using Foundry.Telemetry;

namespace Foundry.Deploy.Models.Configuration;

/// <summary>
/// Represents the runtime configuration consumed by Foundry.Deploy inside WinPE.
/// </summary>
public sealed record FoundryDeployConfigurationDocument
{
    /// <summary>
    /// Gets the current configuration schema version.
    /// </summary>
    public const int CurrentSchemaVersion = 3;

    /// <summary>
    /// Gets the schema version of this configuration.
    /// </summary>
    public int SchemaVersion { get; init; } = CurrentSchemaVersion;

    /// <summary>
    /// Gets Windows localization settings used during deployment.
    /// </summary>
    public DeployLocalizationSettings Localization { get; init; } = new();

    /// <summary>
    /// Gets Windows customization settings applied during deployment.
    /// </summary>
    public DeployCustomizationSettings Customization { get; init; } = new();

    /// <summary>
    /// Gets Autopilot provisioning settings.
    /// </summary>
    public DeployAutopilotSettings Autopilot { get; init; } = new();

    /// <summary>
    /// Gets telemetry policy and runtime settings for Foundry.Deploy.
    /// </summary>
    public TelemetrySettings Telemetry { get; init; } = new();
}
