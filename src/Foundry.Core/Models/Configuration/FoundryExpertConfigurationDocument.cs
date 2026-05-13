using Foundry.Telemetry;

namespace Foundry.Core.Models.Configuration;

/// <summary>
/// Represents the complete user-authored Expert Deploy configuration persisted by the Foundry app.
/// </summary>
public sealed record FoundryExpertConfigurationDocument
{
    /// <summary>
    /// Gets the current schema version for Expert Deploy configuration documents.
    /// </summary>
    public const int CurrentSchemaVersion = 4;

    /// <summary>
    /// Gets the schema version of this configuration document.
    /// </summary>
    public int SchemaVersion { get; init; } = CurrentSchemaVersion;

    /// <summary>
    /// Gets general media and source settings.
    /// </summary>
    public GeneralSettings General { get; init; } = new();

    /// <summary>
    /// Gets network provisioning settings used by Foundry.Connect media.
    /// </summary>
    public NetworkSettings Network { get; init; } = new();

    /// <summary>
    /// Gets user-authored OS localization settings used when deployment configuration is generated.
    /// </summary>
    public LocalizationSettings Localization { get; init; } = new();

    /// <summary>
    /// Gets user-authored customization settings used when deployment configuration is generated.
    /// </summary>
    public CustomizationSettings Customization { get; init; } = new();

    /// <summary>
    /// Gets Autopilot profile settings staged for OOBE.
    /// </summary>
    public AutopilotSettings Autopilot { get; init; } = new();

    /// <summary>
    /// Gets telemetry policy and runtime settings propagated into generated media.
    /// </summary>
    public TelemetrySettings Telemetry { get; init; } = new();
}
