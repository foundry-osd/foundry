namespace Foundry.Core.Models.Configuration;

public sealed record FoundryExpertConfigurationDocument
{
    public const int CurrentSchemaVersion = 4;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;
    public GeneralSettings General { get; init; } = new();
    public NetworkSettings Network { get; init; } = new();
    public LocalizationSettings Localization { get; init; } = new();
    public CustomizationSettings Customization { get; init; } = new();
    public AutopilotSettings Autopilot { get; init; } = new();
}
