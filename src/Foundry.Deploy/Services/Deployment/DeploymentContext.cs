using Foundry.Deploy.Models;

namespace Foundry.Deploy.Services.Deployment;

public sealed record DeploymentContext
{
    public required DeploymentMode Mode { get; init; }
    public required string CacheRootPath { get; init; }
    public required int TargetDiskNumber { get; init; }
    public required OperatingSystemCatalogItem OperatingSystem { get; init; }
    public DriverPackCatalogItem? DriverPack { get; init; }
    public bool AutoSelectDriverPackWhenEmpty { get; init; } = true;
    public bool UseFullAutopilot { get; init; }
    public bool AllowAutopilotDeferredCompletion { get; init; } = true;
    public bool IsDryRun { get; init; }
}
