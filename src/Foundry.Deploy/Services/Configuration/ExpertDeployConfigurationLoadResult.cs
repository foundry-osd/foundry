using Foundry.Deploy.Models.Configuration;

namespace Foundry.Deploy.Services.Configuration;

public sealed record ExpertDeployConfigurationLoadResult
{
    public string ConfigurationPath { get; init; } = ExpertDeployConfigurationService.DefaultConfigurationPath;
    public bool Exists { get; init; }
    public FoundryDeployConfigurationDocument? Document { get; init; }
    public string? FailureMessage { get; init; }
}
