// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Deploy.Services.Deployment;

/// <summary>
/// Defines stable step identities and the registration template. DeploymentPlan selects the actual execution order.
/// </summary>
public static class DeploymentStepNames
{
    public const string ValidateCustomUnattend = "Validate custom answer file";
    public const string StageCustomUnattend = "Stage custom answer file";
    public const string ValidateTargetConfiguration = "Validate target configuration";
    public const string ResolveCacheStrategy = "Resolve cache strategy";
    public const string PreflightDeployment = "Check deployment readiness";
    public const string PrepareTargetDiskLayout = "Prepare target disk layout";
    public const string DownloadOperatingSystemImage = "Download operating system image";
    public const string DownloadDriverPack = "Download driver pack";
    public const string ExtractDriverPack = "Extract driver pack";
    public const string ApplyOperatingSystemImage = "Apply operating system image";
    public const string CheckWindowsImage = "Check Windows image";
    public const string ConfigureWindowsBoot = "Configure Windows boot";
    public const string ConfigureAiPolicies = "Configure AI policies";
    public const string StageDriverInstaller = "Stage driver installer";
    public const string ExtractFirmwareUpdate = "Extract firmware update";
    public const string ConfigureTargetComputerName = "Configure target computer name";
    public const string ConfigureOobeSettings = "Configure OOBE settings";
    public const string ConfigureWindowsOptionalFeatures = "Configure Windows optional features";
    public const string ConfigureRecoveryEnvironment = "Configure recovery environment";
    public const string StagePreOobeCustomization = "Stage pre-OOBE customization";
    public const string ApplyDriverPack = "Apply driver pack";
    public const string ApplyRecoveryDrivers = "Apply recovery drivers";
    public const string DownloadFirmwareUpdate = "Download firmware update";
    public const string ApplyFirmwareUpdate = "Apply firmware update";
    public const string SealRecoveryPartition = "Seal recovery partition";
    public const string ProvisionAutopilot = "Provision Autopilot";
    public const string FinalizeDeploymentAndWriteLogs = "Finalize deployment and write logs";

    /// <summary>
    /// Gets the registration template validated by <see cref="DeploymentOrchestrator"/>.
    /// </summary>
    public static readonly IReadOnlyList<string> ExecutionOrder =
    [
        ValidateCustomUnattend,
        ValidateTargetConfiguration,
        ResolveCacheStrategy,
        PreflightDeployment,
        PrepareTargetDiskLayout,
        DownloadOperatingSystemImage,
        CheckWindowsImage,
        ApplyOperatingSystemImage,
        ConfigureWindowsBoot,
        StageCustomUnattend,
        DownloadDriverPack,
        ExtractDriverPack,
        ApplyDriverPack,
        StageDriverInstaller,
        DownloadFirmwareUpdate,
        ExtractFirmwareUpdate,
        ApplyFirmwareUpdate,
        ConfigureTargetComputerName,
        ConfigureOobeSettings,
        ConfigureAiPolicies,
        ConfigureWindowsOptionalFeatures,
        StagePreOobeCustomization,
        ConfigureRecoveryEnvironment,
        ApplyRecoveryDrivers,
        SealRecoveryPartition,
        ProvisionAutopilot,
        FinalizeDeploymentAndWriteLogs
    ];
}
