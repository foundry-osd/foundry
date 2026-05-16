using Foundry.Core.Services.WinPe;

namespace Foundry.ViewModels;

/// <summary>
/// Tracks the active low-cardinality media creation stage while preserving existing UI progress forwarding.
/// </summary>
internal sealed class MediaCreationTelemetryProgressTracker
{
    private static readonly IReadOnlyList<StatusStepMapping> CustomizationStatusMappings =
    [
        new("Resolving WinRE source catalog", MediaCreationStepNames.ResolveWinReSourceCatalog),
        new("Selected WinRE source package", MediaCreationStepNames.SelectWinReSourcePackage),
        new("Preparing WinRE source package", MediaCreationStepNames.PrepareWinReSourcePackage),
        new("Resolving WinRE image index", MediaCreationStepNames.ResolveWinReImageIndex),
        new("Exporting Windows image", MediaCreationStepNames.ExportWinReSourceImage),
        new("Mounting WinRE source image", MediaCreationStepNames.MountWinReSourceImage),
        new("Staging WinRE Wi-Fi dependencies", MediaCreationStepNames.StageWinReWifiDependencies),
        new("Replacing boot image with WinRE", MediaCreationStepNames.ReplaceBootImageWithWinRe),
        new("Preparing boot image customization", MediaCreationStepNames.CustomizeBootImage),
        new("Mounting boot image", MediaCreationStepNames.MountBootImage),
        new("Injecting drivers", MediaCreationStepNames.InjectDriversIntoBootImage),
        new("Applying language", MediaCreationStepNames.ApplyLanguageAndOptionalComponents),
        new("Applying optional components", MediaCreationStepNames.ApplyLanguageAndOptionalComponents),
        new("Applying international settings", MediaCreationStepNames.ApplyLanguageAndOptionalComponents),
        new("Provisioning Foundry boot assets", MediaCreationStepNames.ProvisionBootAssets),
        new("Provisioning Foundry runtime payloads", MediaCreationStepNames.ProvisionRuntimePayloads),
        new("Committing image changes", MediaCreationStepNames.CommitBootImageChanges)
    ];

    private static readonly IReadOnlyList<StatusStepMapping> DownloadStatusMappings =
    [
        new("Downloading driver package", MediaCreationStepNames.DownloadWinPeDriverPackage),
        new("Downloading WinRE source package", MediaCreationStepNames.DownloadWinReSourcePackage),
        new("Downloading Foundry.Connect runtime payload", MediaCreationStepNames.DownloadConnectRuntimePayload),
        new("Downloading Foundry.Deploy runtime payload", MediaCreationStepNames.DownloadDeployRuntimePayload)
    ];

    private static readonly IReadOnlyList<StatusStepMapping> FinalMediaStatusMappings =
    [
        new("Preparing ISO output path", MediaCreationStepNames.PrepareIsoOutputPath),
        new("Preparing ISO workspace", MediaCreationStepNames.PrepareIsoWorkspace),
        new("Running MakeWinPEMedia for ISO", MediaCreationStepNames.RunMakeWinPeMediaForIso),
        new("Finalizing ISO output", MediaCreationStepNames.FinalizeIsoOutput),
        new("Validating USB target", MediaCreationStepNames.ValidateUsbTarget),
        new("Checking USB target safety", MediaCreationStepNames.CheckUsbTargetSafety),
        new("Partitioning and formatting USB target", MediaCreationStepNames.PartitionAndFormatUsbTarget),
        new("Copying WinPE media to USB", MediaCreationStepNames.CopyWinPeMediaToUsb),
        new("Configuring USB boot files", MediaCreationStepNames.ConfigureUsbBootFiles),
        new("Verifying USB boot media", MediaCreationStepNames.VerifyUsbBootMedia),
        new("Preparing USB cache partition", MediaCreationStepNames.PrepareUsbCachePartition),
        new("Provisioning USB runtime payloads", MediaCreationStepNames.ProvisionUsbRuntimePayloads)
    ];

    /// <summary>
    /// Gets the last media creation stage reported before the operation completed or failed.
    /// </summary>
    public string CurrentStepName { get; private set; } = MediaCreationStepNames.Unknown;

    /// <summary>
    /// Sets the active media creation stage for synchronous orchestration boundaries that do not report progress.
    /// </summary>
    public void SetCurrentStep(string stepName)
    {
        CurrentStepName = string.IsNullOrWhiteSpace(stepName) ? MediaCreationStepNames.Unknown : stepName;
    }

    /// <summary>
    /// Creates a progress adapter that captures WinPE workspace preparation stages before forwarding progress.
    /// </summary>
    public IProgress<WinPeWorkspacePreparationStage> CreateWorkspacePreparationProgress(
        IProgress<WinPeWorkspacePreparationStage> uiProgress)
    {
        return new TrackingProgress<WinPeWorkspacePreparationStage>(
            this,
            uiProgress,
            ResolveFailedStepName);
    }

    /// <summary>
    /// Creates a progress adapter that captures mounted boot image customization stages before forwarding progress.
    /// </summary>
    public IProgress<WinPeMountedImageCustomizationProgress> CreateCustomizationProgress(
        IProgress<WinPeMountedImageCustomizationProgress> uiProgress)
    {
        return new TrackingProgress<WinPeMountedImageCustomizationProgress>(
            this,
            uiProgress,
            ResolveFailedStepName);
    }

    /// <summary>
    /// Creates a progress adapter that captures dependency download stages without preserving path, URL, or byte-count details.
    /// </summary>
    public IProgress<WinPeDownloadProgress> CreateDownloadProgress(IProgress<WinPeDownloadProgress> uiProgress)
    {
        return new TrackingProgress<WinPeDownloadProgress>(
            this,
            uiProgress,
            ResolveFailedStepName);
    }

    /// <summary>
    /// Creates a progress adapter that captures final ISO or USB media creation stages before forwarding progress.
    /// </summary>
    public IProgress<WinPeMediaProgress> CreateFinalMediaProgress(IProgress<WinPeMediaProgress> uiProgress)
    {
        return new TrackingProgress<WinPeMediaProgress>(
            this,
            uiProgress,
            ResolveFailedStepName);
    }

    private static string ResolveFailedStepName(WinPeWorkspacePreparationStage stage)
    {
        return stage switch
        {
            WinPeWorkspacePreparationStage.ResolvingDrivers => MediaCreationStepNames.ResolveWinPeDrivers,
            WinPeWorkspacePreparationStage.CustomizingImage => MediaCreationStepNames.CustomizeBootImage,
            WinPeWorkspacePreparationStage.EvaluatingSignaturePolicy => MediaCreationStepNames.EvaluateSignaturePolicy,
            _ => MediaCreationStepNames.PrepareWinPeWorkspace
        };
    }

    private static string ResolveFailedStepName(WinPeMountedImageCustomizationProgress progress)
    {
        return ResolveStatusStepName(progress.Status, CustomizationStatusMappings, MediaCreationStepNames.CustomizeBootImage);
    }

    private static string ResolveFailedStepName(WinPeDownloadProgress progress)
    {
        return ResolveStatusStepName(progress.Status, DownloadStatusMappings, MediaCreationStepNames.DownloadMediaDependency);
    }

    private static string ResolveFailedStepName(WinPeMediaProgress progress)
    {
        return ResolveStatusStepName(progress.Status, FinalMediaStatusMappings, MediaCreationStepNames.CreateFinalMedia);
    }

    private static string ResolveStatusStepName(
        string status,
        IReadOnlyList<StatusStepMapping> mappings,
        string fallback)
    {
        foreach (StatusStepMapping mapping in mappings)
        {
            if (status.StartsWith(mapping.StatusPrefix, StringComparison.OrdinalIgnoreCase))
            {
                return mapping.StepName;
            }
        }

        return fallback;
    }

    private sealed class TrackingProgress<T>(
        MediaCreationTelemetryProgressTracker owner,
        IProgress<T> uiProgress,
        Func<T, string> resolveStepName) : IProgress<T>
    {
        public void Report(T value)
        {
            owner.SetCurrentStep(resolveStepName(value));
            uiProgress.Report(value);
        }
    }

    private sealed record StatusStepMapping(string StatusPrefix, string StepName);
}

/// <summary>
/// Defines stable boot media creation step names used by telemetry.
/// </summary>
internal static class MediaCreationStepNames
{
    public const string Unknown = "unknown";
    public const string ValidateUsbTarget = "Validate USB target";
    public const string ResolveWinPeTools = "Resolve WinPE tools";
    public const string PrepareRuntimePayloads = "Prepare runtime payloads";
    public const string CleanStaleWorkspaces = "Clean stale WinPE workspaces";
    public const string BuildWinPeWorkspace = "Build WinPE workspace";
    public const string GenerateProvisioningPayloads = "Generate provisioning payloads";
    public const string PrepareWinPeWorkspace = "Prepare WinPE workspace";
    public const string ResolveWinPeDrivers = "Resolve WinPE drivers";
    public const string CustomizeBootImage = "Customize boot image";
    public const string EvaluateSignaturePolicy = "Evaluate signature policy";
    public const string ResolveWinReSourceCatalog = "Resolve WinRE source catalog";
    public const string SelectWinReSourcePackage = "Select WinRE source package";
    public const string PrepareWinReSourcePackage = "Prepare WinRE source package";
    public const string DownloadWinReSourcePackage = "Download WinRE source package";
    public const string ResolveWinReImageIndex = "Resolve WinRE image index";
    public const string ExportWinReSourceImage = "Export WinRE source image";
    public const string MountWinReSourceImage = "Mount WinRE source image";
    public const string StageWinReWifiDependencies = "Stage WinRE Wi-Fi dependencies";
    public const string ReplaceBootImageWithWinRe = "Replace boot image with WinRE";
    public const string MountBootImage = "Mount boot image";
    public const string DownloadWinPeDriverPackage = "Download WinPE driver package";
    public const string InjectDriversIntoBootImage = "Inject drivers into boot image";
    public const string ApplyLanguageAndOptionalComponents = "Apply language and optional components";
    public const string ProvisionBootAssets = "Provision boot assets";
    public const string ProvisionRuntimePayloads = "Provision runtime payloads";
    public const string DownloadConnectRuntimePayload = "Download Connect runtime payload";
    public const string DownloadDeployRuntimePayload = "Download Deploy runtime payload";
    public const string CommitBootImageChanges = "Commit boot image changes";
    public const string DownloadMediaDependency = "Download media dependency";
    public const string PrepareIsoOutputPath = "Prepare ISO output path";
    public const string PrepareIsoWorkspace = "Prepare ISO workspace";
    public const string RunMakeWinPeMediaForIso = "Run MakeWinPEMedia for ISO";
    public const string FinalizeIsoOutput = "Finalize ISO output";
    public const string CheckUsbTargetSafety = "Check USB target safety";
    public const string PartitionAndFormatUsbTarget = "Partition and format USB target";
    public const string CopyWinPeMediaToUsb = "Copy WinPE media to USB";
    public const string ConfigureUsbBootFiles = "Configure USB boot files";
    public const string VerifyUsbBootMedia = "Verify USB boot media";
    public const string PrepareUsbCachePartition = "Prepare USB cache partition";
    public const string ProvisionUsbRuntimePayloads = "Provision USB runtime payloads";
    public const string CreateFinalMedia = "Create final media";
    public const string CreateIsoMedia = "Create ISO media";
    public const string CreateUsbMedia = "Create USB media";
}
