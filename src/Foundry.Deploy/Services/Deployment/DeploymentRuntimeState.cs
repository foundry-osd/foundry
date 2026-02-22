using Foundry.Deploy.Models;
using Foundry.Deploy.Services.Cache;

namespace Foundry.Deploy.Services.Deployment;

public sealed record DeploymentRuntimeState
{
    public DateTimeOffset StartedAtUtc { get; init; } = DateTimeOffset.UtcNow;
    public string CurrentStep { get; set; } = string.Empty;
    public DeploymentMode Mode { get; init; }
    public bool IsDryRun { get; init; }
    public string RequestedCacheRootPath { get; init; } = string.Empty;
    public int TargetDiskNumber { get; init; } = -1;
    public CacheResolution? ResolvedCache { get; set; }
    public HardwareProfile? HardwareProfile { get; set; }
    public string OperatingSystemFileName { get; init; } = string.Empty;
    public string OperatingSystemUrl { get; init; } = string.Empty;
    public string? DownloadedOperatingSystemPath { get; set; }
    public int? AppliedImageIndex { get; set; }
    public string? TargetSystemPartitionRoot { get; set; }
    public string? TargetWindowsPartitionRoot { get; set; }
    public string? DriverPackName { get; set; }
    public string? DriverPackUrl { get; set; }
    public string? DownloadedDriverPackPath { get; set; }
    public string? PreparedDriverPath { get; set; }
    public string? AutopilotWorkflowPath { get; set; }
    public List<string> CompletedSteps { get; init; } = [];
}
