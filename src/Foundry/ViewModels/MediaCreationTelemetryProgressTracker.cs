using Foundry.Core.Services.WinPe;

namespace Foundry.ViewModels;

/// <summary>
/// Tracks the active low-cardinality media creation stage while preserving existing UI progress forwarding.
/// </summary>
internal sealed class MediaCreationTelemetryProgressTracker
{
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
        string status = progress.Status;
        if (status.StartsWith("Resolving WinRE source catalog", StringComparison.OrdinalIgnoreCase))
        {
            return MediaCreationStepNames.ResolveWinReSourceCatalog;
        }

        if (status.StartsWith("Selected WinRE source package", StringComparison.OrdinalIgnoreCase))
        {
            return MediaCreationStepNames.SelectWinReSourcePackage;
        }

        if (status.StartsWith("Preparing WinRE source package", StringComparison.OrdinalIgnoreCase))
        {
            return MediaCreationStepNames.PrepareWinReSourcePackage;
        }

        if (status.StartsWith("Resolving WinRE image index", StringComparison.OrdinalIgnoreCase))
        {
            return MediaCreationStepNames.ResolveWinReImageIndex;
        }

        if (status.StartsWith("Exporting Windows image", StringComparison.OrdinalIgnoreCase))
        {
            return MediaCreationStepNames.ExportWinReSourceImage;
        }

        if (status.StartsWith("Mounting WinRE source image", StringComparison.OrdinalIgnoreCase))
        {
            return MediaCreationStepNames.MountWinReSourceImage;
        }

        if (status.StartsWith("Staging WinRE Wi-Fi dependencies", StringComparison.OrdinalIgnoreCase))
        {
            return MediaCreationStepNames.StageWinReWifiDependencies;
        }

        if (status.StartsWith("Replacing boot image with WinRE", StringComparison.OrdinalIgnoreCase))
        {
            return MediaCreationStepNames.ReplaceBootImageWithWinRe;
        }

        if (status.StartsWith("Preparing boot image customization", StringComparison.OrdinalIgnoreCase))
        {
            return MediaCreationStepNames.CustomizeBootImage;
        }

        if (status.StartsWith("Mounting boot image", StringComparison.OrdinalIgnoreCase))
        {
            return MediaCreationStepNames.MountBootImage;
        }

        if (status.StartsWith("Injecting drivers", StringComparison.OrdinalIgnoreCase))
        {
            return MediaCreationStepNames.InjectDriversIntoBootImage;
        }

        if (status.StartsWith("Applying language", StringComparison.OrdinalIgnoreCase) ||
            status.StartsWith("Applying optional components", StringComparison.OrdinalIgnoreCase) ||
            status.StartsWith("Applying international settings", StringComparison.OrdinalIgnoreCase))
        {
            return MediaCreationStepNames.ApplyLanguageAndOptionalComponents;
        }

        if (status.StartsWith("Provisioning Foundry boot assets", StringComparison.OrdinalIgnoreCase))
        {
            return MediaCreationStepNames.ProvisionBootAssets;
        }

        if (status.StartsWith("Provisioning Foundry runtime payloads", StringComparison.OrdinalIgnoreCase))
        {
            return MediaCreationStepNames.ProvisionRuntimePayloads;
        }

        if (status.StartsWith("Committing image changes", StringComparison.OrdinalIgnoreCase))
        {
            return MediaCreationStepNames.CommitBootImageChanges;
        }

        return MediaCreationStepNames.CustomizeBootImage;
    }

    private static string ResolveFailedStepName(WinPeDownloadProgress progress)
    {
        string status = progress.Status;
        if (status.StartsWith("Downloading driver package", StringComparison.OrdinalIgnoreCase))
        {
            return MediaCreationStepNames.DownloadWinPeDriverPackage;
        }

        if (status.StartsWith("Downloading WinRE source package", StringComparison.OrdinalIgnoreCase))
        {
            return MediaCreationStepNames.DownloadWinReSourcePackage;
        }

        if (status.StartsWith("Downloading Foundry.Connect runtime payload", StringComparison.OrdinalIgnoreCase))
        {
            return MediaCreationStepNames.DownloadConnectRuntimePayload;
        }

        if (status.StartsWith("Downloading Foundry.Deploy runtime payload", StringComparison.OrdinalIgnoreCase))
        {
            return MediaCreationStepNames.DownloadDeployRuntimePayload;
        }

        return MediaCreationStepNames.DownloadMediaDependency;
    }

    private static string ResolveFailedStepName(WinPeMediaProgress progress)
    {
        string status = progress.Status;
        if (status.StartsWith("Preparing ISO output path", StringComparison.OrdinalIgnoreCase))
        {
            return MediaCreationStepNames.PrepareIsoOutputPath;
        }

        if (status.StartsWith("Preparing ISO workspace", StringComparison.OrdinalIgnoreCase))
        {
            return MediaCreationStepNames.PrepareIsoWorkspace;
        }

        if (status.StartsWith("Running MakeWinPEMedia for ISO", StringComparison.OrdinalIgnoreCase))
        {
            return MediaCreationStepNames.RunMakeWinPeMediaForIso;
        }

        if (status.StartsWith("Finalizing ISO output", StringComparison.OrdinalIgnoreCase))
        {
            return MediaCreationStepNames.FinalizeIsoOutput;
        }

        if (status.StartsWith("Validating USB target", StringComparison.OrdinalIgnoreCase))
        {
            return MediaCreationStepNames.ValidateUsbTarget;
        }

        if (status.StartsWith("Checking USB target safety", StringComparison.OrdinalIgnoreCase))
        {
            return MediaCreationStepNames.CheckUsbTargetSafety;
        }

        if (status.StartsWith("Partitioning and formatting USB target", StringComparison.OrdinalIgnoreCase))
        {
            return MediaCreationStepNames.PartitionAndFormatUsbTarget;
        }

        if (status.StartsWith("Copying WinPE media to USB", StringComparison.OrdinalIgnoreCase))
        {
            return MediaCreationStepNames.CopyWinPeMediaToUsb;
        }

        if (status.StartsWith("Configuring USB boot files", StringComparison.OrdinalIgnoreCase))
        {
            return MediaCreationStepNames.ConfigureUsbBootFiles;
        }

        if (status.StartsWith("Verifying USB boot media", StringComparison.OrdinalIgnoreCase))
        {
            return MediaCreationStepNames.VerifyUsbBootMedia;
        }

        if (status.StartsWith("Preparing USB cache partition", StringComparison.OrdinalIgnoreCase))
        {
            return MediaCreationStepNames.PrepareUsbCachePartition;
        }

        if (status.StartsWith("Provisioning USB runtime payloads", StringComparison.OrdinalIgnoreCase))
        {
            return MediaCreationStepNames.ProvisionUsbRuntimePayloads;
        }

        return MediaCreationStepNames.CreateFinalMedia;
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
