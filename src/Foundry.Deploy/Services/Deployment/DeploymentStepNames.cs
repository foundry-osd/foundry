namespace Foundry.Deploy.Services.Deployment;

public static class DeploymentStepNames
{
    public const string GatherDeploymentVariables = "Gather deployment variables";
    public const string InitializeDeploymentWorkspace = "Initialize deployment workspace";
    public const string ValidateTargetConfiguration = "Validate target configuration";
    public const string ResolveCacheStrategy = "Resolve cache strategy";
    public const string PrepareTargetDiskLayout = "Prepare target disk layout";
    public const string DownloadOperatingSystemImage = "Download operating system image";
    public const string DownloadAndPrepareDriverPack = "Download and prepare driver pack";
    public const string ApplyOperatingSystemImage = "Apply operating system image";
    public const string ConfigureTargetComputerName = "Configure target computer name";
    public const string ConfigureRecoveryEnvironment = "Configure recovery environment";
    public const string ApplyOfflineDrivers = "Apply offline drivers";
    public const string SealRecoveryPartition = "Seal recovery partition";
    public const string ExecuteFullAutopilotWorkflow = "Execute full Autopilot workflow";
    public const string FinalizeDeploymentAndWriteLogs = "Finalize deployment and write logs";

    public static readonly IReadOnlyList<string> All =
    [
        GatherDeploymentVariables,
        InitializeDeploymentWorkspace,
        ValidateTargetConfiguration,
        ResolveCacheStrategy,
        PrepareTargetDiskLayout,
        DownloadOperatingSystemImage,
        DownloadAndPrepareDriverPack,
        ApplyOperatingSystemImage,
        ConfigureTargetComputerName,
        ConfigureRecoveryEnvironment,
        ApplyOfflineDrivers,
        SealRecoveryPartition,
        ExecuteFullAutopilotWorkflow,
        FinalizeDeploymentAndWriteLogs
    ];
}
