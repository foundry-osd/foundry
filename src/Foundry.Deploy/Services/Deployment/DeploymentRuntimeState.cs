using Foundry.Deploy.Models;
using Foundry.Deploy.Services.Cache;
using Foundry.Deploy.Services.DriverPacks;

namespace Foundry.Deploy.Services.Deployment;

public sealed record DeploymentRuntimeState
{
    public DateTimeOffset StartedAtUtc { get; init; } = DateTimeOffset.UtcNow;
    public string WorkspaceRoot { get; init; } = string.Empty;
    public string CurrentStep { get; set; } = string.Empty;
    public DeploymentMode Mode { get; init; }
    public bool IsDryRun { get; init; }
    public string RequestedCacheRootPath { get; init; } = string.Empty;
    public int TargetDiskNumber { get; init; } = -1;
    public string TargetComputerName { get; set; } = string.Empty;
    public CacheResolution? ResolvedCache { get; set; }
    public HardwareProfile? HardwareProfile { get; set; }
    public string OperatingSystemFileName { get; init; } = string.Empty;
    public string OperatingSystemUrl { get; init; } = string.Empty;
    public string? DownloadedOperatingSystemPath { get; set; }
    public int? AppliedImageIndex { get; set; }
    public string? TargetSystemPartitionRoot { get; set; }
    public string? TargetWindowsPartitionRoot { get; set; }
    public string? TargetRecoveryPartitionRoot { get; set; }
    public char? TargetRecoveryPartitionLetter { get; set; }
    public bool WinReConfigured { get; set; }
    public DriverPackSelectionKind DriverPackSelectionKind { get; set; } = DriverPackSelectionKind.None;
    public string? DriverPackName { get; set; }
    public string? DriverPackUrl { get; set; }
    public string? DownloadedDriverPackPath { get; set; }
    public DriverPackInstallMode DriverPackInstallMode { get; set; } = DriverPackInstallMode.None;
    public string? DriverPackExtractionMethod { get; set; }
    public string? ExtractedDriverPackPath { get; set; }
    public string? DeferredDriverPackagePath { get; set; }
    public string? DriverPackSetupCompleteHookPath { get; set; }
    public bool ApplyFirmwareUpdates { get; set; } = true;
    public string? DownloadedFirmwarePath { get; set; }
    public string? ExtractedFirmwarePath { get; set; }
    public string? FirmwareUpdateId { get; set; }
    public string? FirmwareUpdateTitle { get; set; }
    public bool IsAutopilotEnabled { get; set; }
    public string? SelectedAutopilotProfileFolderName { get; set; }
    public string? SelectedAutopilotProfileDisplayName { get; set; }
    public string? StagedAutopilotConfigurationPath { get; set; }
    public string? TargetFoundryRoot { get; set; }
    public string? DeploymentSummaryPath { get; set; }
    public List<string> CompletedSteps { get; init; } = [];
}
