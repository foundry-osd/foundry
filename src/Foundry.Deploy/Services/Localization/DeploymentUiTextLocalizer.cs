using System.Text.RegularExpressions;

namespace Foundry.Deploy.Services.Localization;

public static partial class DeploymentUiTextLocalizer
{
    public static string LocalizeStepName(string value)
    {
        return value switch
        {
            "Gather deployment variables" => LocalizationText.GetString("Step.GatherDeploymentVariables"),
            "Initialize deployment workspace" => LocalizationText.GetString("Step.InitializeDeploymentWorkspace"),
            "Validate target configuration" => LocalizationText.GetString("Step.ValidateTargetConfiguration"),
            "Resolve cache strategy" => LocalizationText.GetString("Step.ResolveCacheStrategy"),
            "Prepare target disk layout" => LocalizationText.GetString("Step.PrepareTargetDiskLayout"),
            "Download operating system image" => LocalizationText.GetString("Step.DownloadOperatingSystemImage"),
            "Apply operating system image" => LocalizationText.GetString("Step.ApplyOperatingSystemImage"),
            "Configure target computer name" => LocalizationText.GetString("Step.ConfigureTargetComputerName"),
            "Configure recovery environment" => LocalizationText.GetString("Step.ConfigureRecoveryEnvironment"),
            "Download driver pack" => LocalizationText.GetString("Step.DownloadDriverPack"),
            "Extract driver pack" => LocalizationText.GetString("Step.ExtractDriverPack"),
            "Apply driver pack" => LocalizationText.GetString("Step.ApplyDriverPack"),
            "Download firmware update" => LocalizationText.GetString("Step.DownloadFirmwareUpdate"),
            "Apply firmware update" => LocalizationText.GetString("Step.ApplyFirmwareUpdate"),
            "Seal recovery partition" => LocalizationText.GetString("Step.SealRecoveryPartition"),
            "Stage Autopilot configuration" => LocalizationText.GetString("Step.StageAutopilotConfiguration"),
            "Finalize deployment and write logs" => LocalizationText.GetString("Step.FinalizeDeploymentAndWriteLogs"),
            _ => LocalizeMessage(value)
        };
    }

    public static string LocalizeMessage(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return value switch
        {
            "Ready" => LocalizationText.GetString("Status.Ready"),
            "Waiting for deployment..." => LocalizationText.GetString("Status.WaitingForDeployment"),
            "Waiting for progress..." => LocalizationText.GetString("Status.WaitingForProgress"),
            "Preparing deployment..." => LocalizationText.GetString("Status.PreparingDeployment"),
            "Deployment started." => LocalizationText.GetString("Status.DeploymentStarted"),
            "Deployment completed." => LocalizationText.GetString("Status.DeploymentCompleted"),
            "Deployment orchestration completed." => LocalizationText.GetString("Status.DeploymentOrchestrationCompleted"),
            "Deployment cancelled." => LocalizationText.GetString("Status.DeploymentCancelled"),
            "Deployment failed." => LocalizationText.GetString("Status.DeploymentFailed"),
            "Debug preview: progress page." => LocalizationText.GetString("Debug.ProgressPage"),
            "Debug preview: success page." => LocalizationText.GetString("Debug.SuccessPage"),
            "Debug preview: error page." => LocalizationText.GetString("Debug.ErrorPage"),
            "Starting step..." => LocalizationText.GetString("Status.StartingStep"),
            "Step completed." => LocalizationText.GetString("Status.StepCompleted"),
            "Step failed." => LocalizationText.GetString("Status.StepFailed"),
            "Step skipped." => LocalizationText.GetString("Status.StepSkipped"),
            "In progress..." => LocalizationText.GetString("Status.InProgress"),
            "Loading catalogs..." => LocalizationText.GetString("Catalog.Loading"),
            "Loading target disks..." => LocalizationText.GetString("Preparation.LoadingTargetDisks"),
            "No disks detected." => LocalizationText.GetString("Preparation.NoDisksDetected"),
            "Select an operating system." => LocalizationText.GetString("Launch.SelectOperatingSystem"),
            "Enter a valid computer name." => LocalizationText.GetString("Launch.EnterValidComputerName"),
            "Select a target disk." => LocalizationText.GetString("Launch.SelectTargetDisk"),
            "Select a valid OEM model/version before starting deployment." => LocalizationText.GetString("Launch.SelectValidOemDriverPack"),
            "Select an Autopilot profile or disable Autopilot before starting deployment." => LocalizationText.GetString("Launch.SelectAutopilotProfile"),
            "Deployment cancelled by user." => LocalizationText.GetString("Launch.CancelledByUser"),
            "Deployment preparation completed." => LocalizationText.GetString("Launch.PreparationCompleted"),
            "Another operation is already running." => LocalizationText.GetString("Status.AnotherOperationRunning"),
            "Starting Foundry.Deploy orchestration." => LocalizationText.GetString("Status.StartingOrchestration"),
            "Gathering deployment variables..." => LocalizationText.GetString("StepMessage.GatheringDeploymentVariables"),
            "Collecting deployment context..." => LocalizationText.GetString("StepMessage.CollectingDeploymentContext"),
            "Deployment variables gathered." => LocalizationText.GetString("StepResult.DeploymentVariablesGathered"),
            "Deployment variables gathered (simulation)." => LocalizationText.GetString("StepResult.DeploymentVariablesGatheredSimulation"),
            "Initializing deployment workspace..." => LocalizationText.GetString("StepMessage.InitializingDeploymentWorkspace"),
            "Creating workspace folders..." => LocalizationText.GetString("StepMessage.CreatingWorkspaceFolders"),
            "Finalizing deployment..." => LocalizationText.GetString("StepMessage.FinalizingDeployment"),
            "Cleaning temporary workspace..." => LocalizationText.GetString("StepMessage.CleaningTemporaryWorkspace"),
            "Writing deployment summary..." => LocalizationText.GetString("StepMessage.WritingDeploymentSummary"),
            "Writing completion logs..." => LocalizationText.GetString("StepMessage.WritingCompletionLogs"),
            "Workspace initialized." => LocalizationText.GetString("StepResult.WorkspaceInitialized"),
            "Workspace initialized (simulation)." => LocalizationText.GetString("StepResult.WorkspaceInitializedSimulation"),
            "Validating target configuration..." => LocalizationText.GetString("StepMessage.ValidatingTargetConfiguration"),
            "Revalidating target disk..." => LocalizationText.GetString("StepMessage.RevalidatingTargetDisk"),
            "Detecting hardware profile..." => LocalizationText.GetString("StepMessage.DetectingHardwareProfile"),
            "Operating system URL is missing." => LocalizationText.GetString("StepResult.OperatingSystemUrlMissing"),
            "Target disk number is required." => LocalizationText.GetString("StepResult.TargetDiskNumberRequired"),
            "Target configuration validated." => LocalizationText.GetString("StepResult.TargetConfigurationValidated"),
            "Target configuration validated (simulation)." => LocalizationText.GetString("StepResult.TargetConfigurationValidatedSimulation"),
            "Resolving cache strategy..." => LocalizationText.GetString("StepMessage.ResolvingCacheStrategy"),
            "Checking cache disk conflict..." => LocalizationText.GetString("StepMessage.CheckingCacheDiskConflict"),
            "Preparing target disk layout..." => LocalizationText.GetString("StepMessage.PreparingTargetDiskLayout"),
            "Partitioning target disk..." => LocalizationText.GetString("StepMessage.PartitioningTargetDisk"),
            "Preparing target workspace..." => LocalizationText.GetString("StepMessage.PreparingTargetWorkspace"),
            "Creating simulated partitions..." => LocalizationText.GetString("StepMessage.CreatingSimulatedPartitions"),
            "Target disk layout prepared." => LocalizationText.GetString("StepResult.TargetDiskLayoutPrepared"),
            "Target disk layout prepared (simulation)." => LocalizationText.GetString("StepResult.TargetDiskLayoutPreparedSimulation"),
            "Target disk layout was not prepared." => LocalizationText.GetString("StepResult.TargetDiskLayoutNotPrepared"),
            "Downloading OS image..." => LocalizationText.GetString("StepMessage.DownloadingOperatingSystemImage"),
            "Checking cache..." => LocalizationText.GetString("StepMessage.CheckingCache"),
            "Operating system image ready (simulation)." => LocalizationText.GetString("StepResult.OperatingSystemImageReadySimulation"),
            "Operating system image was not downloaded." => LocalizationText.GetString("StepResult.OperatingSystemImageNotDownloaded"),
            "Applying OS image..." => LocalizationText.GetString("StepMessage.ApplyingOperatingSystemImage"),
            "Inspecting image..." => LocalizationText.GetString("StepMessage.InspectingImage"),
            "Applying image..." => LocalizationText.GetString("StepMessage.ApplyingImage"),
            "Configuring boot..." => LocalizationText.GetString("StepMessage.ConfiguringBoot"),
            "Verifying image..." => LocalizationText.GetString("StepMessage.VerifyingImage"),
            "Operating system image applied." => LocalizationText.GetString("StepResult.OperatingSystemImageApplied"),
            "Operating system image applied (simulation)." => LocalizationText.GetString("StepResult.OperatingSystemImageAppliedSimulation"),
            "Configuring target computer name..." => LocalizationText.GetString("StepMessage.ConfiguringTargetComputerName"),
            "Writing offline computer name..." => LocalizationText.GetString("StepMessage.WritingOfflineComputerName"),
            "Target Windows partition is unavailable." => LocalizationText.GetString("StepResult.TargetWindowsPartitionUnavailable"),
            "Target computer name configured." => LocalizationText.GetString("StepResult.TargetComputerNameConfigured"),
            "Target computer name configured (simulation)." => LocalizationText.GetString("StepResult.TargetComputerNameConfiguredSimulation"),
            "Configuring recovery environment..." => LocalizationText.GetString("StepMessage.ConfiguringRecoveryEnvironment"),
            "Preparing Windows Recovery Environment..." => LocalizationText.GetString("StepMessage.PreparingWindowsRecoveryEnvironment"),
            "Recovery partition is unavailable." => LocalizationText.GetString("StepResult.RecoveryPartitionUnavailable"),
            "Recovery environment configured." => LocalizationText.GetString("StepResult.RecoveryEnvironmentConfigured"),
            "Recovery environment configured (simulation)." => LocalizationText.GetString("StepResult.RecoveryEnvironmentConfiguredSimulation"),
            "Driver pack disabled (None selected)." => LocalizationText.GetString("StepResult.DriverPackDisabled"),
            "No driver pack download required." => LocalizationText.GetString("StepResult.NoDriverPackDownloadRequired"),
            "Downloading driver pack..." => LocalizationText.GetString("StepMessage.DownloadingDriverPack"),
            "Preparing download..." => LocalizationText.GetString("StepMessage.PreparingDownload"),
            "OEM driver pack mode selected but no driver pack was provided." => LocalizationText.GetString("StepResult.OemDriverPackMissing"),
            "Driver pack downloaded." => LocalizationText.GetString("StepResult.DriverPackDownloaded"),
            "Driver pack downloaded (simulation)." => LocalizationText.GetString("StepResult.DriverPackDownloadedSimulation"),
            "Extracting driver pack..." => LocalizationText.GetString("StepMessage.ExtractingDriverPack"),
            "Driver pack was not downloaded." => LocalizationText.GetString("StepResult.DriverPackNotDownloaded"),
            "Driver pack extracted." => LocalizationText.GetString("StepResult.DriverPackExtracted"),
            "Driver pack extracted (simulation)." => LocalizationText.GetString("StepResult.DriverPackExtractedSimulation"),
            "No driver pack operation is required." => LocalizationText.GetString("StepResult.NoDriverPackOperationRequired"),
            "Unsupported driver pack install mode." => LocalizationText.GetString("StepResult.UnsupportedDriverPackInstallMode"),
            "No extracted INF driver payload is available." => LocalizationText.GetString("StepResult.NoExtractedInfDriverPayload"),
            "Applying driver pack..." => LocalizationText.GetString("StepMessage.ApplyingDriverPack"),
            "Applying Windows drivers..." => LocalizationText.GetString("StepMessage.ApplyingWindowsDrivers"),
            "Mounting WinRE..." => LocalizationText.GetString("StepMessage.MountingWinRe"),
            "Applying WinRE drivers..." => LocalizationText.GetString("StepMessage.ApplyingWinReDrivers"),
            "Unmounting WinRE..." => LocalizationText.GetString("StepMessage.UnmountingWinRe"),
            "Staging package..." => LocalizationText.GetString("StepMessage.StagingPackage"),
            "Updating SetupComplete hook..." => LocalizationText.GetString("StepMessage.UpdatingSetupCompleteHook"),
            "Driver pack applied." => LocalizationText.GetString("StepResult.DriverPackApplied"),
            "Driver pack staged for first boot." => LocalizationText.GetString("StepResult.DriverPackStagedForFirstBoot"),
            "Driver pack applied (simulation)." => LocalizationText.GetString("StepResult.DriverPackAppliedSimulation"),
            "Downloading firmware update..." => LocalizationText.GetString("StepMessage.DownloadingFirmwareUpdate"),
            "Preparing Microsoft Update Catalog lookup..." => LocalizationText.GetString("StepMessage.PreparingMicrosoftUpdateCatalogLookup"),
            "Firmware updates are disabled." => LocalizationText.GetString("StepResult.FirmwareUpdatesDisabled"),
            "Firmware updates are disabled for virtual machines." => LocalizationText.GetString("StepResult.FirmwareUpdatesDisabledForVirtualMachines"),
            "Firmware updates are skipped while the device is running on battery power." => LocalizationText.GetString("StepResult.FirmwareUpdatesSkippedOnBattery"),
            "System firmware hardware identifier is unavailable." => LocalizationText.GetString("StepResult.SystemFirmwareHardwareIdentifierUnavailable"),
            "Firmware update downloaded." => LocalizationText.GetString("StepResult.FirmwareUpdateDownloaded"),
            "Firmware update downloaded (simulation)." => LocalizationText.GetString("StepResult.FirmwareUpdateDownloadedSimulation"),
            "No extracted firmware payload is available." => LocalizationText.GetString("StepResult.NoExtractedFirmwarePayload"),
            "The extracted firmware payload does not contain any INF files." => LocalizationText.GetString("StepResult.NoFirmwareInfFiles"),
            "Applying firmware update..." => LocalizationText.GetString("StepMessage.ApplyingFirmwareUpdate"),
            "Injecting firmware payload into offline Windows..." => LocalizationText.GetString("StepMessage.InjectingFirmwarePayload"),
            "Firmware update applied." => LocalizationText.GetString("StepResult.FirmwareUpdateApplied"),
            "Firmware update applied (simulation)." => LocalizationText.GetString("StepResult.FirmwareUpdateAppliedSimulation"),
            "Sealing recovery partition..." => LocalizationText.GetString("StepMessage.SealingRecoveryPartition"),
            "Removing recovery drive letter..." => LocalizationText.GetString("StepMessage.RemovingRecoveryDriveLetter"),
            "Recovery partition sealed." => LocalizationText.GetString("StepResult.RecoveryPartitionSealed"),
            "Recovery partition sealed (simulation)." => LocalizationText.GetString("StepResult.RecoveryPartitionSealedSimulation"),
            "Autopilot is disabled." => LocalizationText.GetString("StepResult.AutopilotDisabled"),
            "Autopilot is enabled but no profile was selected." => LocalizationText.GetString("StepResult.AutopilotProfileMissing"),
            "Target Windows partition is unavailable for Autopilot staging." => LocalizationText.GetString("StepResult.TargetWindowsPartitionUnavailableForAutopilot"),
            "Failed to resolve the target Autopilot directory." => LocalizationText.GetString("StepResult.TargetAutopilotDirectoryUnavailable"),
            "Staging Autopilot profile..." => LocalizationText.GetString("StepMessage.StagingAutopilotProfile"),
            "Copying AutopilotConfigurationFile.json..." => LocalizationText.GetString("StepMessage.CopyingAutopilotConfiguration"),
            "Writing dry-run Autopilot manifest..." => LocalizationText.GetString("StepMessage.WritingDryRunAutopilotManifest"),
            "Autopilot profile staged." => LocalizationText.GetString("StepResult.AutopilotProfileStaged"),
            "Autopilot profile staged (simulation)." => LocalizationText.GetString("StepResult.AutopilotProfileStagedSimulation"),
            "Rebooting now..." => LocalizationText.GetString("Status.RebootingNow"),
            "Reboot command failed." => LocalizationText.GetString("Status.RebootCommandFailed"),
            "Unknown step" => LocalizationText.GetString("Status.UnknownStep"),
            "No error details were provided." => LocalizationText.GetString("Status.NoErrorDetails"),
            "N/A" => LocalizationText.GetString("Common.NotAvailable"),
            "Unavailable" => LocalizationText.GetString("Common.Unavailable"),
            "None" => LocalizationText.GetString("Common.None"),
            "Microsoft Update Catalog" => LocalizationText.GetString("DriverPack.MicrosoftUpdateCatalog"),
            "OEM Driver Pack" => LocalizationText.GetString("DriverPack.OemDriverPack"),
            _ => LocalizeDynamicMessage(value)
        };
    }

    private static string LocalizeDynamicMessage(string value)
    {
        Match match = CatalogLoadedRegex().Match(value);
        if (match.Success)
        {
            return LocalizationText.Format(
                "Catalog.LoadedFormat",
                int.Parse(match.Groups["os"].Value),
                int.Parse(match.Groups["drivers"].Value));
        }

        if (TryLocalizeSingleSuffix(value, "Catalog load failed: ", "Catalog.LoadFailedFormat", out string localized))
        {
            return localized;
        }

        if (TryLocalizeSingleSuffix(value, "Target disk discovery failed: ", "Preparation.TargetDiskDiscoveryFailedFormat", out localized))
        {
            return localized;
        }

        if (TryLocalizeSingleSuffix(value, "Hardware detection failed: ", "Preparation.HardwareDetectionFailedFormat", out localized))
        {
            return localized;
        }

        if (TryLocalizeSingleSuffix(value, "Selected disk blocked: ", "Disk.SelectedBlockedFormat", out localized))
        {
            return localized;
        }

        if (TryLocalizeSingleSuffix(value, "Selected disk is blocked: ", "Disk.SelectedBlockedFormat", out localized))
        {
            return localized;
        }

        if (TryLocalizeSingleSuffix(value, "Deployment failed: ", "Status.DeploymentFailedFormat", out localized))
        {
            return localized;
        }

        if (TryLocalizeSingleSuffix(value, "Unable to open log file: ", "Status.UnableToOpenLogFileFormat", out localized))
        {
            return localized;
        }

        if (TryLocalizeSingleSuffix(value, "Reboot command failed: ", "Status.RebootCommandFailedFormat", out localized))
        {
            return localized;
        }

        if (value.StartsWith("Debug preview: DISM apply failed because the target partition is read-only.", StringComparison.Ordinal))
        {
            return LocalizationText.GetString("Debug.ErrorPreviewMessage");
        }

        match = ProfilesAvailableRegex().Match(value);
        if (match.Success)
        {
            return LocalizationText.Format("Preparation.AutopilotProfilesAvailableFormat", int.Parse(match.Groups["count"].Value));
        }

        match = TargetDisksLoadedRegex().Match(value);
        if (match.Success)
        {
            return LocalizationText.Format("Preparation.TargetDisksLoadedFormat", int.Parse(match.Groups["count"].Value));
        }

        match = StepCounterRegex().Match(value);
        if (match.Success)
        {
            return LocalizationText.Format(
                "Status.StepCounterFormat",
                int.Parse(match.Groups["current"].Value),
                int.Parse(match.Groups["total"].Value));
        }

        match = StartingStepRegex().Match(value);
        if (match.Success)
        {
            return LocalizationText.Format("Status.StartingStepFormat", LocalizeStepName(match.Groups["step"].Value));
        }

        match = StartingStepSubProgressRegex().Match(value);
        if (match.Success)
        {
            return LocalizationText.Format("Status.StartingStepSubProgressFormat", LocalizeStepName(match.Groups["step"].Value));
        }

        match = ApplyingImageProgressRegex().Match(value);
        if (match.Success)
        {
            return LocalizationText.Format("Debug.ApplyingImageProgressFormat", match.Groups["percent"].Value);
        }

        match = WpeUtilFailureRegex().Match(value);
        if (match.Success)
        {
            return LocalizationText.Format("Status.WpeUtilFailureFormat", match.Groups["code"].Value, match.Groups["diagnostic"].Value.Trim());
        }

        return value;
    }

    private static bool TryLocalizeSingleSuffix(string value, string prefix, string key, out string localized)
    {
        if (value.StartsWith(prefix, StringComparison.Ordinal))
        {
            localized = LocalizationText.Format(key, value[prefix.Length..].Trim());
            return true;
        }

        localized = string.Empty;
        return false;
    }

    [GeneratedRegex(@"^Catalogs loaded: (?<os>\d+) OS entries, (?<drivers>\d+) driver packs\.$")]
    private static partial Regex CatalogLoadedRegex();

    [GeneratedRegex(@"^Profiles available: (?<count>\d+)$")]
    private static partial Regex ProfilesAvailableRegex();

    [GeneratedRegex(@"^Target disks loaded: (?<count>\d+) detected\.$")]
    private static partial Regex TargetDisksLoadedRegex();

    [GeneratedRegex(@"^Step: (?<current>\d+) of (?<total>\d+)$")]
    private static partial Regex StepCounterRegex();

    [GeneratedRegex(@"^Starting (?<step>.+)\.$")]
    private static partial Regex StartingStepRegex();

    [GeneratedRegex(@"^Starting (?<step>.+)\.\.\.$")]
    private static partial Regex StartingStepSubProgressRegex();

    [GeneratedRegex(@"^Applying image: (?<percent>\d+(?:[.,]\d+)?)%$")]
    private static partial Regex ApplyingImageProgressRegex();

    [GeneratedRegex(@"^wpeutil\.exe failed with exit code (?<code>\d+)\. (?<diagnostic>.+)$")]
    private static partial Regex WpeUtilFailureRegex();
}
