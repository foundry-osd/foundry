using Foundry.Deploy.Models.Configuration;

namespace Foundry.Deploy.Services.Configuration;

public sealed record DeployConfigurationLoadResult
{
    public string ConfigurationPath { get; init; } = DeployConfigurationService.DefaultConfigurationPath;
    public bool Exists { get; init; }
    public FoundryDeployConfigurationDocument? Document { get; init; }
    public int? SchemaVersion { get; init; }
    public int CurrentSchemaVersion { get; init; } = FoundryDeployConfigurationDocument.CurrentSchemaVersion;
    public bool IsBootMediaUpdateRecommended { get; init; }
    public string? FailureMessage { get; init; }
}
