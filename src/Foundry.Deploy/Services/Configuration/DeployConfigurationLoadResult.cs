using Foundry.Deploy.Models.Configuration;

namespace Foundry.Deploy.Services.Configuration;

public sealed record DeployConfigurationLoadResult
{
    public string ConfigurationPath { get; init; } = DeployConfigurationService.DefaultConfigurationPath;
    public bool Exists { get; init; }
    public FoundryDeployConfigurationDocument? Document { get; init; }
    public string? FailureMessage { get; init; }
}
