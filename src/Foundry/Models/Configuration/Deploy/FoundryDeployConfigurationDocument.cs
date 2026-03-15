namespace Foundry.Models.Configuration.Deploy;

public sealed record FoundryDeployConfigurationDocument
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;
    public DeployLocalizationSettings Localization { get; init; } = new();
    public DeployCustomizationSettings Customization { get; init; } = new();
}
