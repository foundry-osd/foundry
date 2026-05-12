using Foundry.Deploy.Models;
using Foundry.Deploy.Services.Cache;
using Foundry.Deploy.Services.DriverPacks;

namespace Foundry.Deploy.Services.Deployment;

/// <summary>
/// Captures mutable deployment state persisted to logs and reused across deployment steps.
/// </summary>
public sealed record DeploymentRuntimeState
{
    /// <summary>
    /// Gets the UTC time when deployment started.
    /// </summary>
    public DateTimeOffset StartedAtUtc { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Gets the active deployment workspace root.
    /// </summary>
    public string WorkspaceRoot { get; init; } = string.Empty;

    /// <summary>
    /// Gets or sets the current deployment step name.
    /// </summary>
    public string CurrentStep { get; set; } = string.Empty;

    /// <summary>
    /// Gets the deployment source mode.
    /// </summary>
    public DeploymentMode Mode { get; init; }

    /// <summary>
    /// Gets a value indicating whether deployment runs without mutating a real target disk.
    /// </summary>
    public bool IsDryRun { get; init; }

    /// <summary>
    /// Gets the cache root path requested by the deployment context.
    /// </summary>
    public string RequestedCacheRootPath { get; init; } = string.Empty;

    /// <summary>
    /// Gets the selected target disk number.
    /// </summary>
    public int TargetDiskNumber { get; init; } = -1;

    /// <summary>
    /// Gets or sets the target computer name written to unattended setup.
    /// </summary>
    public string TargetComputerName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the resolved cache strategy.
    /// </summary>
    public CacheResolution? ResolvedCache { get; set; }

    /// <summary>
    /// Gets or sets the detected hardware profile used for package matching.
    /// </summary>
    public HardwareProfile? HardwareProfile { get; set; }

    /// <summary>
    /// Gets the expected operating system package file name.
    /// </summary>
    public string OperatingSystemFileName { get; init; } = string.Empty;

    /// <summary>
    /// Gets the operating system source URL.
    /// </summary>
    public string OperatingSystemUrl { get; init; } = string.Empty;

    /// <summary>
    /// Gets or sets the downloaded operating system package path.
    /// </summary>
    public string? DownloadedOperatingSystemPath { get; set; }

    /// <summary>
    /// Gets or sets the image index applied from the downloaded OS package.
    /// </summary>
    public int? AppliedImageIndex { get; set; }

    /// <summary>
    /// Gets or sets the target EFI system partition root.
    /// </summary>
    public string? TargetSystemPartitionRoot { get; set; }

    /// <summary>
    /// Gets or sets the target Windows partition root.
    /// </summary>
    public string? TargetWindowsPartitionRoot { get; set; }

    /// <summary>
    /// Gets or sets the target recovery partition root.
    /// </summary>
    public string? TargetRecoveryPartitionRoot { get; set; }

    /// <summary>
    /// Gets or sets the target recovery partition letter before it is sealed.
    /// </summary>
    public char? TargetRecoveryPartitionLetter { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether Windows RE was configured.
    /// </summary>
    public bool WinReConfigured { get; set; }

    /// <summary>
    /// Gets or sets the selected driver pack source.
    /// </summary>
    public DriverPackSelectionKind DriverPackSelectionKind { get; set; } = DriverPackSelectionKind.None;

    /// <summary>
    /// Gets or sets the selected driver pack name.
    /// </summary>
    public string? DriverPackName { get; set; }

    /// <summary>
    /// Gets or sets the selected driver pack source URL.
    /// </summary>
    public string? DriverPackUrl { get; set; }

    /// <summary>
    /// Gets or sets the downloaded driver pack path.
    /// </summary>
    public string? DownloadedDriverPackPath { get; set; }

    /// <summary>
    /// Gets or sets the resolved driver pack install mode.
    /// </summary>
    public DriverPackInstallMode DriverPackInstallMode { get; set; } = DriverPackInstallMode.None;

    /// <summary>
    /// Gets or sets the resolved driver pack extraction method.
    /// </summary>
    public string? DriverPackExtractionMethod { get; set; }

    /// <summary>
    /// Gets or sets the extracted driver pack root path.
    /// </summary>
    public string? ExtractedDriverPackPath { get; set; }

    /// <summary>
    /// Gets or sets the offline path to the driver package staged for first-boot installation.
    /// </summary>
    public string? DeferredDriverPackagePath { get; set; }

    /// <summary>
    /// Gets or sets the offline SetupComplete.cmd path used by deferred driver provisioning.
    /// </summary>
    public string? DriverPackSetupCompleteHookPath { get; set; }

    /// <summary>
    /// Gets or sets the offline SetupComplete.cmd path used to launch the pre-OOBE runner.
    /// </summary>
    public string? PreOobeSetupCompletePath { get; set; }

    /// <summary>
    /// Gets or sets the offline path to the generated pre-OOBE PowerShell runner.
    /// </summary>
    public string? PreOobeRunnerPath { get; set; }

    /// <summary>
    /// Gets or sets the offline path to the generated pre-OOBE execution manifest.
    /// </summary>
    public string? PreOobeManifestPath { get; set; }

    /// <summary>
    /// Gets or sets the offline paths to staged pre-OOBE PowerShell scripts.
    /// </summary>
    public IReadOnlyList<string> PreOobeScriptPaths { get; set; } = [];

    /// <summary>
    /// Gets or sets a value indicating whether firmware updates should be applied.
    /// </summary>
    public bool ApplyFirmwareUpdates { get; set; } = true;

    /// <summary>
    /// Gets or sets the downloaded firmware package path.
    /// </summary>
    public string? DownloadedFirmwarePath { get; set; }

    /// <summary>
    /// Gets or sets the extracted firmware package path.
    /// </summary>
    public string? ExtractedFirmwarePath { get; set; }

    /// <summary>
    /// Gets or sets the selected firmware update identifier.
    /// </summary>
    public string? FirmwareUpdateId { get; set; }

    /// <summary>
    /// Gets or sets the selected firmware update title.
    /// </summary>
    public string? FirmwareUpdateTitle { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether Autopilot staging is enabled.
    /// </summary>
    public bool IsAutopilotEnabled { get; set; }

    /// <summary>
    /// Gets or sets the selected Autopilot profile folder name.
    /// </summary>
    public string? SelectedAutopilotProfileFolderName { get; set; }

    /// <summary>
    /// Gets or sets the selected Autopilot profile display name.
    /// </summary>
    public string? SelectedAutopilotProfileDisplayName { get; set; }

    /// <summary>
    /// Gets or sets the offline path to the staged Autopilot configuration file.
    /// </summary>
    public string? StagedAutopilotConfigurationPath { get; set; }

    /// <summary>
    /// Gets or sets the transient Foundry workspace on the target Windows partition.
    /// Finalization rebinds retained artifacts under Windows\Temp\Foundry and removes this root.
    /// </summary>
    public string? TargetFoundryRoot { get; set; }

    /// <summary>
    /// Gets or sets the generated deployment summary path.
    /// </summary>
    public string? DeploymentSummaryPath { get; set; }

    /// <summary>
    /// Gets completed deployment step names in execution order.
    /// </summary>
    public List<string> CompletedSteps { get; init; } = [];
}
