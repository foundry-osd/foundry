using Foundry.Deploy.Models;
using Foundry.Deploy.Models.Configuration;
using Foundry.Deploy.Services.Catalog;

namespace Foundry.Deploy.Services.Startup;

public sealed record DeploymentStartupSnapshot
{
    public required string CacheRootPath { get; init; }
    public required string? StartupStatusMessage { get; init; }
    public required FoundryDeployConfigurationDocument? ExpertConfigurationDocument { get; init; }
    public required IReadOnlyList<AutopilotProfileCatalogItem> AutopilotProfiles { get; init; }
    public required string EffectiveComputerName { get; init; }
    public required HardwareProfile? DetectedHardware { get; init; }
    public required string? HardwareDetectionFailureMessage { get; init; }
    public required IReadOnlyList<TargetDiskInfo> TargetDisks { get; init; }
    public required string? TargetDiskStatusMessage { get; init; }
    public required DeploymentCatalogSnapshot CatalogSnapshot { get; init; }
}
