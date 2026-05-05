using Foundry.Deploy.Models;

namespace Foundry.Deploy.Services.Deployment;

public sealed record DeploymentLaunchRequest
{
    public required DeploymentMode Mode { get; init; }
    public required string CacheRootPath { get; init; }
    public required string TargetComputerName { get; init; }
    public string? DefaultTimeZoneId { get; init; }
    public required TargetDiskInfo? SelectedTargetDisk { get; init; }
    public required OperatingSystemCatalogItem? SelectedOperatingSystem { get; init; }
    public required DriverPackSelectionKind DriverPackSelectionKind { get; init; }
    public required DriverPackCatalogItem? SelectedDriverPack { get; init; }
    public required bool ApplyFirmwareUpdates { get; init; }
    public required bool IsAutopilotEnabled { get; init; }
    public required AutopilotProfileCatalogItem? SelectedAutopilotProfile { get; init; }
    public required bool IsDryRun { get; init; }
}
