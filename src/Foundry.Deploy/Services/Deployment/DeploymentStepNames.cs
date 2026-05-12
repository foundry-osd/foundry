namespace Foundry.Deploy.Services.Deployment;

/// <summary>
/// Defines the canonical deployment step names and their expected workflow order.
/// </summary>
public static class DeploymentStepNames
{
    public const string GatherDeploymentVariables = "Gather deployment variables";
    public const string InitializeDeploymentWorkspace = "Initialize deployment workspace";
    public const string ValidateTargetConfiguration = "Validate target configuration";
    public const string ResolveCacheStrategy = "Resolve cache strategy";
    public const string PrepareTargetDiskLayout = "Prepare target disk layout";
    public const string DownloadOperatingSystemImage = "Download operating system image";
    public const string DownloadDriverPack = "Download driver pack";
    public const string ExtractDriverPack = "Extract driver pack";
    public const string ApplyOperatingSystemImage = "Apply operating system image";
    public const string ConfigureTargetComputerName = "Configure target computer name";
    public const string ConfigureRecoveryEnvironment = "Configure recovery environment";
    public const string ApplyDriverPack = "Apply driver pack";
    public const string DownloadFirmwareUpdate = "Download firmware update";
    public const string ApplyFirmwareUpdate = "Apply firmware update";
    public const string SealRecoveryPartition = "Seal recovery partition";
    public const string StageAutopilotConfiguration = "Stage Autopilot configuration";
    public const string FinalizeDeploymentAndWriteLogs = "Finalize deployment and write logs";

    /// <summary>
    /// Gets the canonical deployment workflow order validated by <see cref="DeploymentOrchestrator"/>.
    /// </summary>
    public static readonly IReadOnlyList<string> All =
    [
        GatherDeploymentVariables,
        InitializeDeploymentWorkspace,
        ValidateTargetConfiguration,
        ResolveCacheStrategy,
        PrepareTargetDiskLayout,
        DownloadOperatingSystemImage,
        ApplyOperatingSystemImage,
        ConfigureTargetComputerName,
        ConfigureRecoveryEnvironment,
        DownloadDriverPack,
        ExtractDriverPack,
        ApplyDriverPack,
        DownloadFirmwareUpdate,
        ApplyFirmwareUpdate,
        SealRecoveryPartition,
        StageAutopilotConfiguration,
        FinalizeDeploymentAndWriteLogs
    ];
}
