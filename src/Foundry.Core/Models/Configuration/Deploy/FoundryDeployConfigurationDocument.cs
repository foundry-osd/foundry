using Foundry.Telemetry;

namespace Foundry.Core.Models.Configuration.Deploy;

/// <summary>
/// Represents the configuration consumed by Foundry.Deploy while applying Windows.
/// </summary>
public sealed record FoundryDeployConfigurationDocument
{
    /// <summary>
    /// Gets the current schema version for Foundry.Deploy configuration documents.
    /// </summary>
    public const int CurrentSchemaVersion = 3;

    /// <summary>
    /// Gets the schema version of this deployment configuration document.
    /// </summary>
    public int SchemaVersion { get; init; } = CurrentSchemaVersion;

    /// <summary>
    /// Gets Windows localization settings used during apply and unattend generation.
    /// </summary>
    public DeployLocalizationSettings Localization { get; init; } = new();

    /// <summary>
    /// Gets Windows customization settings applied during deployment.
    /// </summary>
    public DeployCustomizationSettings Customization { get; init; } = new();

    /// <summary>
    /// Gets Autopilot profile staging settings.
    /// </summary>
    public DeployAutopilotSettings Autopilot { get; init; } = new();

    /// <summary>
    /// Gets telemetry policy and runtime settings consumed by Foundry.Deploy.
    /// </summary>
    public TelemetrySettings Telemetry { get; init; } = new();
}
