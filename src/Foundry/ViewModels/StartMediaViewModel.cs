using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using Foundry.Core.Models.Configuration;
using Foundry.Core.Services.Application;
using Foundry.Core.Services.Media;
using Foundry.Core.Services.WinPe;
using Foundry.Services.Adk;
using Foundry.Services.Configuration;
using Foundry.Services.Localization;
using Foundry.Services.Operations;
using Foundry.Services.Settings;
using Foundry.Services.Shell;
using Foundry.Telemetry;
using Serilog;

namespace Foundry.ViewModels;

/// <summary>
/// Coordinates media creation options, readiness evaluation, and final ISO or USB creation commands.
/// </summary>
public sealed partial class StartMediaViewModel : ObservableObject, IDisposable
{
    private readonly IAppSettingsService appSettingsService;
    private readonly IAdkService adkService;
    private readonly IWinPeLanguageDiscoveryService languageDiscoveryService;
    private readonly IWinPeEmbeddedAssetService embeddedAssetService;
    private readonly IWinPeBuildService buildService;
    private readonly IWinPeWorkspacePreparationService workspacePreparationService;
    private readonly IWinPeIsoMediaService isoMediaService;
    private readonly IWinPeUsbMediaService usbMediaService;
    private readonly IFilePickerService filePickerService;
    private readonly IExpertDeployConfigurationStateService expertDeployConfigurationStateService;
    private readonly ITelemetryService telemetryService;
    private readonly IOperationProgressService operationProgressService;
    private readonly IShellNavigationGuardService shellNavigationGuardService;
    private readonly IDialogService dialogService;
    private readonly IApplicationLocalizationService localizationService;
    private readonly IAppDispatcher appDispatcher;
    private readonly ILogger logger;
    private IReadOnlyList<string> availableWinPeLanguages = [];
    private UsbCandidateDiscoveryState usbCandidateDiscoveryState = UsbCandidateDiscoveryState.NotLoaded;
    private bool isLoadingConfiguration = true;

    /// <summary>
    /// Initializes a new instance of the <see cref="StartMediaViewModel"/> class.
    /// </summary>
    public StartMediaViewModel(
        IAppSettingsService appSettingsService,
        IAdkService adkService,
        IWinPeLanguageDiscoveryService languageDiscoveryService,
        IWinPeEmbeddedAssetService embeddedAssetService,
        IWinPeBuildService buildService,
        IWinPeWorkspacePreparationService workspacePreparationService,
        IWinPeIsoMediaService isoMediaService,
        IWinPeUsbMediaService usbMediaService,
        IFilePickerService filePickerService,
        IExpertDeployConfigurationStateService expertDeployConfigurationStateService,
        ITelemetryService telemetryService,
        IOperationProgressService operationProgressService,
        IShellNavigationGuardService shellNavigationGuardService,
        IDialogService dialogService,
        IApplicationLocalizationService localizationService,
        IAppDispatcher appDispatcher,
        ILogger logger)
    {
        this.appSettingsService = appSettingsService;
        this.adkService = adkService;
        this.languageDiscoveryService = languageDiscoveryService;
        this.embeddedAssetService = embeddedAssetService;
        this.buildService = buildService;
        this.workspacePreparationService = workspacePreparationService;
        this.isoMediaService = isoMediaService;
        this.usbMediaService = usbMediaService;
        this.filePickerService = filePickerService;
        this.expertDeployConfigurationStateService = expertDeployConfigurationStateService;
        this.telemetryService = telemetryService;
        this.operationProgressService = operationProgressService;
        this.shellNavigationGuardService = shellNavigationGuardService;
        this.dialogService = dialogService;
        this.localizationService = localizationService;
        this.appDispatcher = appDispatcher;
        this.logger = logger.ForContext<StartMediaViewModel>();

        Architectures =
        [
            new(WinPeArchitecture.X64, "x64"),
            new(WinPeArchitecture.Arm64, "arm64")
        ];
        PartitionStyles = [];
        FormatModes = [];

        IsoOutputPath = appSettingsService.Current.Media.IsoOutputPath;
        SelectedArchitecture = SelectOption(Architectures, ParseEnum(appSettingsService.Current.Media.Architecture, WinPeArchitecture.X64));
        UseCa2023Signature = appSettingsService.Current.Media.UseCa2023Signature;
        RebuildPartitionStyles();
        IncludeDellDrivers = appSettingsService.Current.Media.IncludeDellDrivers;
        IncludeHpDrivers = appSettingsService.Current.Media.IncludeHpDrivers;
        CustomDriverDirectoryPath = appSettingsService.Current.Media.CustomDriverDirectoryPath ?? string.Empty;

        adkService.StatusChanged += OnAdkStatusChanged;
        expertDeployConfigurationStateService.StateChanged += OnExpertDeployConfigurationStateChanged;
        localizationService.LanguageChanged += OnLanguageChanged;

        isLoadingConfiguration = false;
        ApplyLocalizedText();
        RefreshEvaluation();
    }

    /// <summary>
    /// Gets the available WinPE architecture options.
    /// </summary>
    public ObservableCollection<SelectionOption<WinPeArchitecture>> Architectures { get; }

    /// <summary>
    /// Gets the USB partition style options valid for the selected architecture.
    /// </summary>
    public ObservableCollection<SelectionOption<UsbPartitionStyle>> PartitionStyles { get; }

    /// <summary>
    /// Gets the USB formatting mode options.
    /// </summary>
    public ObservableCollection<SelectionOption<UsbFormatMode>> FormatModes { get; }

    /// <summary>
    /// Gets removable USB disk candidates discovered for media creation.
    /// </summary>
    public ObservableCollection<SelectionOption<WinPeUsbDiskCandidate>> UsbCandidates { get; } = [];

    /// <summary>
    /// Gets readiness items that describe prerequisite blockers.
    /// </summary>
    public ObservableCollection<StartReadinessItemViewModel> PrerequisiteReadinessItems { get; } = [];

    /// <summary>
    /// Gets readiness items that describe ISO and USB output blockers.
    /// </summary>
    public ObservableCollection<StartReadinessItemViewModel> MediaOutputReadinessItems { get; } = [];

    /// <summary>
    /// Gets readiness items that describe expert configuration blockers.
    /// </summary>
    public ObservableCollection<StartReadinessItemViewModel> ExpertConfigurationReadinessItems { get; } = [];

    [ObservableProperty]
    public partial string PageTitle { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string IsoOutputPath { get; set; }

    [ObservableProperty]
    public partial SelectionOption<WinPeArchitecture>? SelectedArchitecture { get; set; }

    [ObservableProperty]
    public partial bool UseCa2023Signature { get; set; }

    [ObservableProperty]
    public partial SelectionOption<UsbPartitionStyle>? SelectedPartitionStyle { get; set; }

    [ObservableProperty]
    public partial SelectionOption<UsbFormatMode>? SelectedFormatMode { get; set; }

    [ObservableProperty]
    public partial bool IncludeDellDrivers { get; set; }

    [ObservableProperty]
    public partial bool IncludeHpDrivers { get; set; }

    [ObservableProperty]
    public partial string CustomDriverDirectoryPath { get; set; }

    [ObservableProperty]
    public partial SelectionOption<WinPeUsbDiskCandidate>? SelectedUsbDisk { get; set; }

    [ObservableProperty]
    public partial bool IsRefreshingUsbCandidates { get; set; }

    [ObservableProperty]
    public partial bool CanGenerateIsoSummary { get; set; }

    [ObservableProperty]
    public partial bool CanGenerateUsbSummary { get; set; }

    [ObservableProperty]
    public partial bool CanCreateIso { get; set; }

    [ObservableProperty]
    public partial bool CanCreateUsb { get; set; }

    [ObservableProperty]
    public partial bool IsMediaOperationRunning { get; set; }

    [ObservableProperty]
    public partial string StatusSummary { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string FinalExecutionStatus { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string GlobalSummary { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string WinPeLanguage { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string UsbCandidateStatus { get; set; } = string.Empty;

    [ObservableProperty]
    public partial InfoBarSeverity ReadinessSeverity { get; set; } = InfoBarSeverity.Informational;

    [ObservableProperty]
    public partial bool IsPrerequisiteReadinessExpanded { get; set; }

    [ObservableProperty]
    public partial bool IsMediaOutputReadinessExpanded { get; set; }

    [ObservableProperty]
    public partial bool IsExpertConfigurationReadinessExpanded { get; set; }

    /// <inheritdoc />
    public void Dispose()
    {
        adkService.StatusChanged -= OnAdkStatusChanged;
        expertDeployConfigurationStateService.StateChanged -= OnExpertDeployConfigurationStateChanged;
        localizationService.LanguageChanged -= OnLanguageChanged;
    }

    /// <summary>
    /// Refreshes readiness and disk candidates after the page is loaded.
    /// </summary>
    public async Task InitializeAsync()
    {
        RefreshEvaluation();

        if (adkService.CurrentStatus.CanCreateMedia)
        {
            await RefreshUsbCandidatesAsync();
        }

        MediaPreflightOptions options = CreatePreflightOptions();
        LogPreflightSummary(options, MediaPreflightService.Evaluate(options));
    }

    [RelayCommand]
    private async Task BrowseIsoOutputPathAsync()
    {
        string? path = await filePickerService.PickSaveFileAsync(
            new FileSavePickerRequest(
                localizationService.GetString("StartMedia.IsoPicker.Title"),
                "Foundry",
                [new(localizationService.GetString("StartMedia.IsoPicker.Filter"), [".iso"])],
                ".iso"));

        if (!string.IsNullOrWhiteSpace(path))
        {
            IsoOutputPath = path;
        }
    }

    [RelayCommand]
    private async Task RefreshUsbCandidatesAsync()
    {
        if (!adkService.CurrentStatus.CanCreateMedia)
        {
            usbCandidateDiscoveryState = UsbCandidateDiscoveryState.Blocked;
            UsbCandidateStatus = localizationService.GetString("StartMedia.Usb.AdkBlocked");
            RefreshEvaluation();
            return;
        }

        WinPeResult<WinPeToolPaths> toolsResult = new WinPeToolResolver().ResolveTools(adkService.CurrentStatus.KitsRootPath);
        if (!toolsResult.IsSuccess || toolsResult.Value is null)
        {
            usbCandidateDiscoveryState = UsbCandidateDiscoveryState.Error;
            UsbCandidateStatus = toolsResult.Error?.Message ?? localizationService.GetString("StartMedia.Usb.QueryFailed");
            logger.Warning("USB target refresh skipped because ADK tools were not resolved. ErrorCode={ErrorCode}", toolsResult.Error?.Code);
            RefreshEvaluation();
            return;
        }

        IsRefreshingUsbCandidates = true;
        usbCandidateDiscoveryState = UsbCandidateDiscoveryState.Loading;
        UsbCandidateStatus = localizationService.GetString("StartMedia.Usb.Loading");
        RefreshEvaluation();

        try
        {
            WinPeResult<IReadOnlyList<WinPeUsbDiskCandidate>> result = await usbMediaService.GetUsbCandidatesAsync(
                toolsResult.Value,
                Constants.UsbQueryTempDirectoryPath,
                CancellationToken.None);

            UsbCandidates.Clear();
            if (result.IsSuccess && result.Value is not null)
            {
                foreach (WinPeUsbDiskCandidate candidate in result.Value)
                {
                    UsbCandidates.Add(CreateUsbDiskOption(candidate));
                }

                SelectedUsbDisk = UsbCandidates.FirstOrDefault(option => option.Value.DiskNumber == SelectedUsbDisk?.Value.DiskNumber)
                    ?? UsbCandidates.FirstOrDefault();
                usbCandidateDiscoveryState = UsbCandidates.Count == 0
                    ? UsbCandidateDiscoveryState.Empty
                    : UsbCandidateDiscoveryState.Ready;
                UsbCandidateStatus = UsbCandidates.Count == 0
                    ? localizationService.GetString("StartMedia.Usb.NoCandidatesFound")
                    : string.Format(localizationService.GetString("StartMedia.Usb.CandidatesFound"), UsbCandidates.Count);
                logger.Information("USB targets refreshed. CandidateCount={CandidateCount}", UsbCandidates.Count);
            }
            else
            {
                SelectedUsbDisk = null;
                usbCandidateDiscoveryState = UsbCandidateDiscoveryState.Error;
                UsbCandidateStatus = result.Error?.Message ?? localizationService.GetString("StartMedia.Usb.QueryFailed");
                logger.Warning("USB target refresh failed. ErrorCode={ErrorCode}", result.Error?.Code);
            }
        }
        finally
        {
            IsRefreshingUsbCandidates = false;
            RefreshEvaluation();
        }
    }

    [RelayCommand]
    private async Task CreateIsoAsync()
    {
        SynchronizeRuntimeTelemetrySettings();

        MediaPreflightOptions options = CreatePreflightOptions();
        MediaPreflightEvaluation evaluation = MediaPreflightService.Evaluate(options);
        if (!evaluation.CanCreateIso)
        {
            await ShowBlockedDialogAsync("StartMedia.CreateIso.BlockedTitle", evaluation.IsoBlockingReasons);
            return;
        }

        await RunFinalMediaOperationAsync(FinalMediaTarget.Iso, options);
    }

    [RelayCommand]
    private async Task CreateUsbAsync()
    {
        SynchronizeRuntimeTelemetrySettings();

        MediaPreflightOptions options = CreatePreflightOptions();
        MediaPreflightEvaluation evaluation = MediaPreflightService.Evaluate(options);
        if (!evaluation.CanCreateUsb || options.SelectedUsbDisk is null)
        {
            await ShowBlockedDialogAsync("StartMedia.CreateUsb.BlockedTitle", evaluation.UsbBlockingReasons);
            return;
        }

        WinPeUsbDiskCandidate selectedDisk = options.SelectedUsbDisk;
        bool confirmed = await dialogService.ConfirmAsync(new ConfirmationDialogRequest(
            localizationService.GetString("StartMedia.CreateUsb.ConfirmTitle"),
            string.Format(
                localizationService.GetString("StartMedia.CreateUsb.ConfirmMessage"),
                selectedDisk.DiskNumber,
                selectedDisk.FriendlyName,
                FormatByteSize(selectedDisk.SizeBytes)),
            localizationService.GetString("StartMedia.CreateUsb.ConfirmPrimary"),
            localizationService.GetString("Common.Cancel")));

        if (!confirmed)
        {
            logger.Information(
                "Final USB media creation cancelled before formatting. DiskNumber={DiskNumber}, DiskName={DiskName}",
                selectedDisk.DiskNumber,
                selectedDisk.FriendlyName);
            return;
        }

        await RunFinalMediaOperationAsync(FinalMediaTarget.Usb, options);
    }

    private async Task RunFinalMediaOperationAsync(FinalMediaTarget target, MediaPreflightOptions options)
    {
        if (IsMediaOperationRunning)
        {
            return;
        }

        IsMediaOperationRunning = true;
        RefreshEvaluation();
        // Final media operations can format disks or overwrite ISO output, so the shell remains locked until completion.
        shellNavigationGuardService.SetState(ShellNavigationState.OperationRunning);
        string terminalStatus = string.Empty;
        string? successMessage = null;
        Stopwatch stopwatch = Stopwatch.StartNew();
        bool success = false;

        try
        {
            string startStatus = target == FinalMediaTarget.Iso
                ? localizationService.GetString("StartMedia.Operation.CreatingIso")
                : localizationService.GetString("StartMedia.Operation.CreatingUsb");

            operationProgressService.Start(OperationKind.MediaCreation, startStatus);
            logger.Information(
                "Final media creation started. Target={Target}, Architecture={Architecture}, WinPeLanguage={WinPeLanguage}, IsoOutputPath={IsoOutputPath}, UsbDiskNumber={UsbDiskNumber}, UsbDiskName={UsbDiskName}",
                target,
                options.Architecture,
                NormalizeCultureName(options.WinPeLanguage),
                target == FinalMediaTarget.Iso ? options.IsoOutputPath : null,
                target == FinalMediaTarget.Usb ? options.SelectedUsbDisk?.DiskNumber : null,
                target == FinalMediaTarget.Usb ? options.SelectedUsbDisk?.FriendlyName : null);

            if (target == FinalMediaTarget.Iso)
            {
                _ = await CreateIsoMediaAsync(options, CancellationToken.None);
                successMessage = localizationService.GetString("StartMedia.Operation.IsoSuccessMessage");
            }
            else
            {
                WinPeUsbProvisionResult usbResult = await CreateUsbMediaAsync(options, CancellationToken.None);
                successMessage = string.Format(
                    CultureInfo.CurrentCulture,
                    localizationService.GetString("StartMedia.Operation.UsbSuccessMessage"),
                    usbResult.BootDriveLetter,
                    usbResult.CacheDriveLetter);
            }

            terminalStatus = successMessage ?? localizationService.GetString("StartMedia.Operation.Completed");
            operationProgressService.Complete(terminalStatus);
            success = true;
            logger.Information("Final media creation completed. Target={Target}", target);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            string failedStatus = localizationService.GetString("StartMedia.Operation.Failed");
            terminalStatus = string.IsNullOrWhiteSpace(ex.Message)
                ? failedStatus
                : $"{failedStatus} {ex.Message}";
            operationProgressService.Report(100, terminalStatus);
            logger.Error(ex, "Final media creation failed. Target={Target}", target);
        }
        finally
        {
            await TrackMediaCreatedAsync(target, options, success, stopwatch.Elapsed, CancellationToken.None);
            shellNavigationGuardService.SetState(adkService.CurrentStatus.CanCreateMedia
                ? ShellNavigationState.Ready
                : ShellNavigationState.AdkBlocked);
            operationProgressService.Reset(terminalStatus);
            IsMediaOperationRunning = false;
            RefreshEvaluation();
        }
    }

    private async Task<string> CreateIsoMediaAsync(MediaPreflightOptions options, CancellationToken cancellationToken)
    {
        PreparedMediaWorkspace? workspace = null;

        try
        {
            workspace = await PrepareMediaWorkspaceAsync(
                options,
                includeRuntimePayloadInImage: true,
                cancellationToken);

            logger.Debug(
                "Creating ISO media. OutputIsoPath={OutputIsoPath}, MediaDirectoryPath={MediaDirectoryPath}, UseBootEx={UseBootEx}",
                options.IsoOutputPath,
                workspace.PreparedWorkspace.Artifact.MediaDirectoryPath,
                workspace.PreparedWorkspace.UseBootEx);

            WinPeResult result = await isoMediaService.CreateAsync(
                new WinPeIsoMediaOptions
                {
                    PreparedWorkspace = workspace.PreparedWorkspace,
                    OutputIsoPath = options.IsoOutputPath,
                    IsoTempDirectoryPath = Path.Combine(Constants.TempDirectoryPath, "Iso"),
                    Progress = new Progress<WinPeMediaProgress>(ReportFinalMediaProgress)
                },
                cancellationToken);

            EnsureSuccess(result);
            logger.Debug("ISO media service completed. OutputIsoPath={OutputIsoPath}", options.IsoOutputPath);
            return options.IsoOutputPath;
        }
        finally
        {
            CleanupPreparedWorkspace(workspace?.PreparedWorkspace.Artifact.WorkingDirectoryPath);
        }
    }

    private async Task<WinPeUsbProvisionResult> CreateUsbMediaAsync(MediaPreflightOptions options, CancellationToken cancellationToken)
    {
        if (options.SelectedUsbDisk is null)
        {
            throw new InvalidOperationException(localizationService.GetString("StartMedia.BlockingReason.NoUsbTarget"));
        }

        PreparedMediaWorkspace? workspace = null;

        try
        {
            workspace = await PrepareMediaWorkspaceAsync(
                options,
                includeRuntimePayloadInImage: false,
                cancellationToken);

            WinPeUsbDiskCandidate selectedDisk = options.SelectedUsbDisk;
            logger.Debug(
                "Creating USB media. DiskNumber={DiskNumber}, DiskName={DiskName}, PartitionStyle={PartitionStyle}, FormatMode={FormatMode}, MediaDirectoryPath={MediaDirectoryPath}, UseBootEx={UseBootEx}",
                selectedDisk.DiskNumber,
                selectedDisk.FriendlyName,
                options.UsbPartitionStyle,
                options.UsbFormatMode,
                workspace.PreparedWorkspace.Artifact.MediaDirectoryPath,
                workspace.PreparedWorkspace.UseBootEx);

            WinPeResult<WinPeUsbProvisionResult> result = await usbMediaService.ProvisionAndPopulateAsync(
                new UsbOutputOptions
                {
                    TargetDiskNumber = selectedDisk.DiskNumber,
                    ExpectedDiskFriendlyName = selectedDisk.FriendlyName,
                    ExpectedDiskSerialNumber = selectedDisk.SerialNumber,
                    ExpectedDiskUniqueId = selectedDisk.UniqueId,
                    PartitionStyle = options.UsbPartitionStyle,
                    FormatMode = options.UsbFormatMode,
                    RuntimePayloadProvisioning = workspace.RuntimePayloadProvisioning,
                    DownloadProgress = new Progress<WinPeDownloadProgress>(ReportDownloadProgress),
                    Progress = new Progress<WinPeMediaProgress>(ReportFinalMediaProgress)
                },
                workspace.PreparedWorkspace.Artifact,
                workspace.Tools,
                workspace.PreparedWorkspace.UseBootEx,
                cancellationToken);

            EnsureSuccess(result);
            logger.Debug(
                "USB media service completed. DiskNumber={DiskNumber}, BootVolume={BootVolume}, CacheVolume={CacheVolume}",
                selectedDisk.DiskNumber,
                result.Value?.BootDriveLetter,
                result.Value?.CacheDriveLetter);
            return result.Value!;
        }
        finally
        {
            CleanupPreparedWorkspace(workspace?.PreparedWorkspace.Artifact.WorkingDirectoryPath);
        }
    }

    private async Task<PreparedMediaWorkspace> PrepareMediaWorkspaceAsync(
        MediaPreflightOptions options,
        bool includeRuntimePayloadInImage,
        CancellationToken cancellationToken)
    {
        WinPeBuildArtifact? artifact = null;

        try
        {
            WinPeToolPaths tools = ResolveWinPeToolsOrThrow();
            logger.Debug(
                "Resolved WinPE tools. KitsRootPath={KitsRootPath}, DismPath={DismPath}, MakeWinPeMediaPath={MakeWinPeMediaPath}",
                tools.KitsRootPath,
                tools.DismPath,
                tools.MakeWinPeMediaPath);

            WinPeRuntimePayloadProvisioningOptions runtimePayloadProvisioning = CreateRuntimePayloadProvisioningOptions(
                options.Architecture,
                Constants.WinPeWorkspaceDirectoryPath,
                Constants.WinPeWorkspaceDirectoryPath,
                Constants.WinPeWorkspaceDirectoryPath);
            runtimePayloadProvisioning = AddReleaseConnectProvisioning(runtimePayloadProvisioning);
            TelemetrySettings connectTelemetrySettings = CreateRuntimeTelemetrySettings(ResolveRuntimePayloadSource(runtimePayloadProvisioning.Connect));
            TelemetrySettings deployTelemetrySettings = CreateRuntimeTelemetrySettings(ResolveRuntimePayloadSource(runtimePayloadProvisioning.Deploy));

            logger.Debug(
                "Final media workspace preparation started. Architecture={Architecture}, WinPeLanguage={WinPeLanguage}, SignatureMode={SignatureMode}, BootImageSource={BootImageSource}, IncludeRuntimePayloadInImage={IncludeRuntimePayloadInImage}, DriverVendorCount={DriverVendorCount}, HasCustomDriverDirectory={HasCustomDriverDirectory}, IsAutopilotEnabled={IsAutopilotEnabled}, IsConnectRuntimeProvisioningEnabled={IsConnectRuntimeProvisioningEnabled}, ConnectRuntimeSource={ConnectRuntimeSource}, IsDeployRuntimeProvisioningEnabled={IsDeployRuntimeProvisioningEnabled}, DeployRuntimeSource={DeployRuntimeSource}",
                options.Architecture,
                NormalizeCultureName(options.WinPeLanguage),
                options.SignatureMode,
                options.BootImageSource,
                includeRuntimePayloadInImage,
                options.DriverVendors.Count,
                !string.IsNullOrWhiteSpace(options.CustomDriverDirectoryPath),
                options.IsAutopilotEnabled,
                runtimePayloadProvisioning.Connect.IsEnabled,
                ResolveProvisioningSource(runtimePayloadProvisioning.Connect),
                runtimePayloadProvisioning.Deploy.IsEnabled,
                ResolveProvisioningSource(runtimePayloadProvisioning.Deploy));

            CleanupStaleWinPeWorkspaces();

            operationProgressService.Report(10, localizationService.GetString("StartMedia.Operation.BuildingWorkspace"));
            WinPeResult<WinPeBuildArtifact> buildResult = await buildService.BuildAsync(
                new WinPeBuildOptions
                {
                    OutputDirectoryPath = Constants.WinPeWorkspaceDirectoryPath,
                    AdkRootPath = tools.KitsRootPath,
                    Architecture = options.Architecture,
                    SignatureMode = options.SignatureMode
                },
                cancellationToken);
            EnsureSuccess(buildResult);

            artifact = buildResult.Value!;
            logger.Debug(
                "WinPE workspace created. WorkingDirectoryPath={WorkingDirectoryPath}, MediaDirectoryPath={MediaDirectoryPath}, MountDirectoryPath={MountDirectoryPath}, BootWimPath={BootWimPath}",
                artifact.WorkingDirectoryPath,
                artifact.MediaDirectoryPath,
                artifact.MountDirectoryPath,
                artifact.BootWimPath);

            FoundryConnectProvisioningBundle connectBundle = expertDeployConfigurationStateService.GenerateConnectProvisioningBundle(
                Path.Combine(artifact.WorkingDirectoryPath, "Provisioning"),
                connectTelemetrySettings);
            logger.Debug(
                "Generated local provisioning payloads. ConnectAssetFileCount={ConnectAssetFileCount}, HasMediaSecretsKey={HasMediaSecretsKey}, AutopilotProfileCount={AutopilotProfileCount}",
                connectBundle.AssetFiles.Count,
                connectBundle.MediaSecretsKey is { Length: > 0 },
                options.IsAutopilotEnabled ? expertDeployConfigurationStateService.Current.Autopilot.Profiles.Count : 0);

            WinPeRuntimePayloadProvisioningOptions artifactRuntimePayloadProvisioning = runtimePayloadProvisioning with
            {
                WorkingDirectoryPath = artifact.WorkingDirectoryPath,
                MountedImagePath = artifact.MountDirectoryPath,
                UsbCacheRootPath = string.Empty
            };

            operationProgressService.Report(25, localizationService.GetString("StartMedia.Operation.PreparingWorkspace"));
            WinPeResult<WinPeWorkspacePreparationResult> preparationResult = await workspacePreparationService.PrepareAsync(
                new WinPeWorkspacePreparationOptions
                {
                    Artifact = artifact,
                    Tools = tools,
                    SignatureMode = options.SignatureMode,
                    BootImageSource = options.BootImageSource,
                    DriverCatalogUri = new WinPeDriverCatalogOptions().CatalogUri,
                    DriverVendors = options.DriverVendors,
                    CustomDriverDirectoryPath = options.CustomDriverDirectoryPath,
                    WinPeLanguage = options.WinPeLanguage,
                    AssetProvisioning = CreateAssetProvisioningOptions(options, connectBundle, runtimePayloadProvisioning, deployTelemetrySettings),
                    RuntimePayloadProvisioning = includeRuntimePayloadInImage ? artifactRuntimePayloadProvisioning : null,
                    WinReCacheDirectoryPath = Constants.WinReTempDirectoryPath,
                    Progress = new Progress<WinPeWorkspacePreparationStage>(ReportWorkspacePreparationStage),
                    DownloadProgress = new Progress<WinPeDownloadProgress>(ReportDownloadProgress),
                    CustomizationProgress = new Progress<WinPeMountedImageCustomizationProgress>(ReportCustomizationProgress)
                },
                cancellationToken);
            EnsureSuccess(preparationResult);

            logger.Debug(
                "WinPE workspace prepared. UseBootEx={UseBootEx}, RuntimePayloadInImage={RuntimePayloadInImage}",
                preparationResult.Value!.UseBootEx,
                includeRuntimePayloadInImage);

            return new PreparedMediaWorkspace(
                preparationResult.Value!,
                tools,
                artifactRuntimePayloadProvisioning with
                {
                    MountedImagePath = string.Empty,
                    UsbCacheRootPath = string.Empty
                });
        }
        catch
        {
            CleanupPreparedWorkspace(artifact?.WorkingDirectoryPath);
            throw;
        }
    }

    private WinPeMountedImageAssetProvisioningOptions CreateAssetProvisioningOptions(
        MediaPreflightOptions options,
        FoundryConnectProvisioningBundle connectBundle,
        WinPeRuntimePayloadProvisioningOptions runtimePayloadProvisioning,
        TelemetrySettings deployTelemetrySettings)
    {
        return new WinPeMountedImageAssetProvisioningOptions
        {
            BootstrapScriptContent = embeddedAssetService.GetBootstrapScriptContent(),
            CurlExecutableSourcePath = ResolveCurlExecutablePath(),
            SevenZipSourceDirectoryPath = embeddedAssetService.GetSevenZipSourceDirectoryPath(),
            IanaWindowsTimeZoneMapJson = embeddedAssetService.GetIanaWindowsTimeZoneMapJson(),
            FoundryConnectConfigurationJson = connectBundle.ConfigurationJson,
            ExpertDeployConfigurationJson = expertDeployConfigurationStateService.GenerateDeployConfigurationJson(deployTelemetrySettings),
            MediaSecretsKey = connectBundle.MediaSecretsKey,
            FoundryConnectAssetFiles = connectBundle.AssetFiles,
            AutopilotProfiles = options.IsAutopilotEnabled
                ? expertDeployConfigurationStateService.Current.Autopilot.Profiles
                : [],
            ConnectProvisioningSource = ResolveProvisioningSource(runtimePayloadProvisioning.Connect),
            DeployProvisioningSource = ResolveProvisioningSource(runtimePayloadProvisioning.Deploy)
        };
    }

    private static WinPeProvisioningSource ResolveProvisioningSource(WinPeRuntimePayloadApplicationOptions options)
    {
        return options.IsEnabled ? options.ProvisioningSource : WinPeProvisioningSource.Release;
    }

    private static string ResolveRuntimePayloadSource(WinPeRuntimePayloadApplicationOptions options)
    {
        return ResolveProvisioningSource(options) switch
        {
            WinPeProvisioningSource.Debug => TelemetryRuntimePayloadSources.Debug,
            WinPeProvisioningSource.Release => TelemetryRuntimePayloadSources.Release,
            _ => TelemetryRuntimePayloadSources.Unknown
        };
    }

    private void ReportWorkspacePreparationStage(WinPeWorkspacePreparationStage stage)
    {
        int progress = stage switch
        {
            WinPeWorkspacePreparationStage.ResolvingDrivers => 30,
            WinPeWorkspacePreparationStage.CustomizingImage => 35,
            WinPeWorkspacePreparationStage.EvaluatingSignaturePolicy => 84,
            _ => 35
        };

        string statusResourceKey = stage switch
        {
            WinPeWorkspacePreparationStage.ResolvingDrivers => "StartMedia.Operation.ResolvingDrivers",
            WinPeWorkspacePreparationStage.CustomizingImage => "StartMedia.Operation.CustomizingImage",
            WinPeWorkspacePreparationStage.EvaluatingSignaturePolicy => "StartMedia.Operation.EvaluatingSignaturePolicy",
            _ => "StartMedia.Operation.PreparingWorkspace"
        };

        logger.Debug("WinPE workspace preparation stage changed. Stage={Stage}, Progress={Progress}", stage, progress);
        operationProgressService.Report(progress, localizationService.GetString(statusResourceKey));
    }

    private void ReportCustomizationProgress(WinPeMountedImageCustomizationProgress progress)
    {
        int normalizedProgress = Math.Clamp(35 + (progress.Percent * 47 / 100), 35, 82);
        string status = string.IsNullOrWhiteSpace(progress.Status)
            ? localizationService.GetString("StartMedia.Operation.PreparingWorkspace")
            : LocalizeCustomizationStatus(progress.Status);

        logger.Debug(
            "WinPE image customization progress changed. CoreStatus={CoreStatus}, Percent={Percent}, NormalizedProgress={NormalizedProgress}, DetailStatus={DetailStatus}, DetailPercent={DetailPercent}",
            progress.Status,
            progress.Percent,
            normalizedProgress,
            progress.DetailStatus,
            progress.DetailPercent);

        if (progress.DetailPercent.HasValue || !string.IsNullOrWhiteSpace(progress.DetailStatus))
        {
            operationProgressService.Report(
                normalizedProgress,
                status,
                progress.DetailPercent,
                LocalizeDismProgressStatus(progress.DetailStatus));
            return;
        }

        operationProgressService.Report(normalizedProgress, status);
    }

    private void ReportDownloadProgress(WinPeDownloadProgress progress)
    {
        string status = string.IsNullOrWhiteSpace(progress.Status)
            ? localizationService.GetString("StartMedia.Operation.Downloading")
            : LocalizeDownloadStatus(progress.Status);

        logger.Debug(
            "Final media download progress changed. DownloadStatus={DownloadStatus}, DownloadPercent={DownloadPercent}",
            progress.Status,
            progress.Percent);
        operationProgressService.Report(
            operationProgressService.State.Progress,
            operationProgressService.State.Status,
            progress.Percent,
            status);
    }

    private void ReportFinalMediaProgress(WinPeMediaProgress progress)
    {
        int normalizedProgress = Math.Clamp(88 + (progress.Percent * 10 / 100), 88, 98);
        string status = string.IsNullOrWhiteSpace(progress.Status)
            ? localizationService.GetString("StartMedia.Operation.CreatingIso")
            : LocalizeFinalMediaStatus(progress.Status);

        logger.Debug(
            "Final media output progress changed. CoreStatus={CoreStatus}, Percent={Percent}, NormalizedProgress={NormalizedProgress}",
            progress.Status,
            progress.Percent,
            normalizedProgress);
        operationProgressService.Report(normalizedProgress, status);
    }

    private string LocalizeCustomizationStatus(string status)
    {
        string resourceKey = status switch
        {
            "Preparing boot image customization." => "StartMedia.Operation.PreparingBootImageCustomization",
            "Mounting boot image." => "StartMedia.Operation.MountingBootImage",
            "Injecting drivers into mounted image." => "StartMedia.Operation.InjectingDrivers",
            "Applying language and optional components." => "StartMedia.Operation.ApplyingLanguageAndComponents",
            "Provisioning Foundry boot assets." => "StartMedia.Operation.ProvisioningBootAssets",
            "Provisioning Foundry runtime payloads." => "StartMedia.Operation.ProvisioningRuntimePayloads",
            "Committing image changes." => "StartMedia.Operation.CommittingImageChanges",
            "Image customization completed." => "StartMedia.Operation.ImageCustomizationCompleted",
            "Resolving WinRE source catalog." => "StartMedia.Operation.ResolvingWinReSourceCatalog",
            "Selected WinRE source package." => "StartMedia.Operation.SelectedWinReSourcePackage",
            "Preparing WinRE source package." => "StartMedia.Operation.PreparingWinReSourcePackage",
            "Validating cached WinRE source package." => "StartMedia.Operation.ValidatingCachedWinReSourcePackage",
            "Using cached WinRE source package." => "StartMedia.Operation.UsingCachedWinReSourcePackage",
            "Downloading WinRE source package." => "StartMedia.Operation.DownloadingWinReSourcePackage",
            "Validating WinRE source package." => "StartMedia.Operation.ValidatingWinReSourcePackage",
            "Resolving WinRE image index." => "StartMedia.Operation.ResolvingWinReImageIndex",
            "Exporting Windows image for WinRE extraction." => "StartMedia.Operation.ExportingWinReSourceImage",
            "Mounting WinRE source image." => "StartMedia.Operation.MountingWinReSourceImage",
            "Staging WinRE Wi-Fi dependencies." => "StartMedia.Operation.StagingWinReWifiDependencies",
            "Replacing boot image with WinRE." => "StartMedia.Operation.ReplacingBootImageWithWinRe",
            "WinRE Wi-Fi boot image is ready." => "StartMedia.Operation.WinReWifiBootImageReady",
            _ => string.Empty
        };

        return string.IsNullOrWhiteSpace(resourceKey)
            ? status
            : localizationService.GetString(resourceKey);
    }

    private string LocalizeDownloadStatus(string status)
    {
        if (status.StartsWith("Downloading WinRE source package", StringComparison.Ordinal))
        {
            int suffixIndex = status.IndexOf('(', StringComparison.Ordinal);
            string suffix = suffixIndex >= 0
                ? $" {status[suffixIndex..]}"
                : string.Empty;
            return localizationService.GetString("StartMedia.Operation.DownloadingWinReSourcePackage") + suffix;
        }

        if (status.StartsWith("Downloading driver package", StringComparison.Ordinal))
        {
            int suffixIndex = status.IndexOf(':', StringComparison.Ordinal);
            string suffix = suffixIndex >= 0
                ? status[suffixIndex..]
                : string.Empty;
            return localizationService.GetString("StartMedia.Operation.DownloadingDriverPackage") + suffix;
        }

        const string runtimeDownloadPrefix = "Downloading ";
        const string runtimeDownloadSuffix = " runtime payload.";
        if (status.StartsWith(runtimeDownloadPrefix, StringComparison.Ordinal))
        {
            int suffixIndex = status.IndexOf('(', StringComparison.Ordinal);
            int applicationNameEndIndex = status.IndexOf(runtimeDownloadSuffix, StringComparison.Ordinal);
            if (applicationNameEndIndex > runtimeDownloadPrefix.Length)
            {
                string applicationName = status[runtimeDownloadPrefix.Length..applicationNameEndIndex];
                string suffix = suffixIndex >= 0
                    ? $" {status[suffixIndex..]}"
                    : string.Empty;
                return localizationService.FormatString("StartMedia.Operation.DownloadingRuntimePayloadFormat", applicationName) + suffix;
            }
        }

        return status;
    }

    private string LocalizeDismProgressStatus(string status)
    {
        string resourceKey = status switch
        {
            "Exporting Windows image with DISM." => "StartMedia.Operation.DismExportingWindowsImage",
            "Resolving WinRE image index with DISM." => "StartMedia.Operation.DismResolvingWinReImageIndex",
            "Mounting image with DISM." => "StartMedia.Operation.DismMountingImage",
            "Injecting drivers with DISM." => "StartMedia.Operation.DismInjectingDrivers",
            "Applying language pack with DISM." => "StartMedia.Operation.DismApplyingLanguagePack",
            "Applying optional components with DISM." => "StartMedia.Operation.DismApplyingOptionalComponents",
            "Applying international settings with DISM." => "StartMedia.Operation.DismApplyingInternationalSettings",
            "Committing image changes with DISM." => "StartMedia.Operation.DismCommittingImageChanges",
            _ => string.Empty
        };

        return string.IsNullOrWhiteSpace(resourceKey)
            ? status
            : localizationService.GetString(resourceKey);
    }

    private string LocalizeFinalMediaStatus(string status)
    {
        string resourceKey = status switch
        {
            "Preparing ISO output path." => "StartMedia.Operation.PreparingIsoOutput",
            "Preparing ISO workspace." => "StartMedia.Operation.PreparingIsoWorkspace",
            "Running MakeWinPEMedia for ISO." => "StartMedia.Operation.RunningMakeWinPeMediaIso",
            "Finalizing ISO output." => "StartMedia.Operation.FinalizingIsoOutput",
            "ISO media completed." => "StartMedia.Operation.IsoMediaCompleted",
            "Validating USB target." => "StartMedia.Operation.ValidatingUsbTarget",
            "Checking USB target safety." => "StartMedia.Operation.CheckingUsbTargetSafety",
            "Partitioning and formatting USB target." => "StartMedia.Operation.PartitioningUsbTarget",
            "Copying WinPE media to USB." => "StartMedia.Operation.CopyingUsbMedia",
            "Configuring USB boot files." => "StartMedia.Operation.ConfiguringUsbBootFiles",
            "Verifying USB boot media." => "StartMedia.Operation.VerifyingUsbMedia",
            "Preparing USB cache partition." => "StartMedia.Operation.PreparingUsbCache",
            "Provisioning USB runtime payloads." => "StartMedia.Operation.ProvisioningUsbRuntimePayloads",
            "USB media completed." => "StartMedia.Operation.UsbMediaCompleted",
            _ => string.Empty
        };

        return string.IsNullOrWhiteSpace(resourceKey)
            ? status
            : localizationService.GetString(resourceKey);
    }

    private void CleanupPreparedWorkspace(string? workingDirectoryPath)
    {
        if (string.IsNullOrWhiteSpace(workingDirectoryPath))
        {
            return;
        }

        string workspaceRoot = Path.GetFullPath(Constants.WinPeWorkspaceDirectoryPath);
        string workspacePath = Path.GetFullPath(workingDirectoryPath);
        string normalizedRoot = workspaceRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;

        if (!workspacePath.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase))
        {
            logger.Warning(
                "Skipped WinPE workspace cleanup because the target is outside the workspace root. WorkspacePath={WorkspacePath}, WorkspaceRoot={WorkspaceRoot}",
                workspacePath,
                workspaceRoot);
            return;
        }

        if (!Directory.Exists(workspacePath))
        {
            logger.Debug("Skipped WinPE workspace cleanup because the directory no longer exists. WorkspacePath={WorkspacePath}", workspacePath);
            return;
        }

        DeleteWorkspaceDirectory(workspacePath, reportProgress: true);
    }

    private void CleanupStaleWinPeWorkspaces()
    {
        string workspaceRoot = Path.GetFullPath(Constants.WinPeWorkspaceDirectoryPath);
        if (!Directory.Exists(workspaceRoot))
        {
            return;
        }

        foreach (string workspacePath in Directory.EnumerateDirectories(workspaceRoot))
        {
            DeleteWorkspaceDirectory(workspacePath, reportProgress: false);
        }

        foreach (string filePath in Directory.EnumerateFiles(workspaceRoot))
        {
            try
            {
                logger.Debug("Cleaning stale WinPE workspace file. FilePath={FilePath}", filePath);
                File.Delete(filePath);
            }
            catch (Exception ex)
            {
                logger.Warning(ex, "Failed to clean stale WinPE workspace file. FilePath={FilePath}", filePath);
            }
        }
    }

    private void DeleteWorkspaceDirectory(string workspacePath, bool reportProgress)
    {
        try
        {
            if (reportProgress)
            {
                operationProgressService.Report(99, localizationService.GetString("StartMedia.Operation.CleaningWorkspace"));
            }

            logger.Debug("Cleaning WinPE workspace. WorkspacePath={WorkspacePath}", workspacePath);
            NormalizeWorkspaceAttributes(workspacePath);
            Directory.Delete(workspacePath, recursive: true);
            logger.Debug("WinPE workspace cleaned. WorkspacePath={WorkspacePath}", workspacePath);
        }
        catch (Exception ex)
        {
            logger.Warning(ex, "Failed to clean WinPE workspace. WorkspacePath={WorkspacePath}", workspacePath);
        }
    }

    private static void NormalizeWorkspaceAttributes(string workspacePath)
    {
        foreach (string filePath in Directory.EnumerateFiles(workspacePath, "*", SearchOption.AllDirectories))
        {
            File.SetAttributes(filePath, FileAttributes.Normal);
        }

        foreach (string directoryPath in Directory
            .EnumerateDirectories(workspacePath, "*", SearchOption.AllDirectories)
            .OrderByDescending(path => path.Length))
        {
            File.SetAttributes(directoryPath, FileAttributes.Directory);
        }

        File.SetAttributes(workspacePath, FileAttributes.Directory);
    }

    private async Task ShowBlockedDialogAsync(
        string titleResourceKey,
        IReadOnlyList<MediaPreflightBlockingReason> blockingReasons)
    {
        IReadOnlyList<MediaPreflightBlockingReason> reasons = blockingReasons.Distinct().ToList();
        string message = reasons.Count == 0
            ? localizationService.GetString("StartMedia.Operation.Failed")
            : string.Join(Environment.NewLine, reasons.Select(reason => $"- {GetBlockingReasonText(reason)}"));

        await dialogService.ShowMessageAsync(new DialogRequest(
            localizationService.GetString(titleResourceKey),
            message,
            localizationService.GetString("Common.Close")));
    }

    partial void OnSelectedUsbDiskChanged(SelectionOption<WinPeUsbDiskCandidate>? value)
    {
        RefreshEvaluation();
    }

    partial void OnIsoOutputPathChanged(string value)
    {
        if (isLoadingConfiguration)
        {
            return;
        }

        appSettingsService.Current.Media.IsoOutputPath = value;
        appSettingsService.Save();
        RefreshEvaluation();
    }

    partial void OnSelectedPartitionStyleChanged(SelectionOption<UsbPartitionStyle>? value)
    {
        if (value is null || isLoadingConfiguration)
        {
            return;
        }

        if (SelectedArchitecture?.Value == WinPeArchitecture.Arm64 && value.Value == UsbPartitionStyle.Mbr)
        {
            SelectedPartitionStyle = SelectOption(PartitionStyles, UsbPartitionStyle.Gpt);
            return;
        }

        appSettingsService.Current.Media.UsbPartitionStyle = value.Value.ToString();
        appSettingsService.Save();
        RefreshEvaluation();
    }

    partial void OnSelectedFormatModeChanged(SelectionOption<UsbFormatMode>? value)
    {
        if (value is null || isLoadingConfiguration)
        {
            return;
        }

        appSettingsService.Current.Media.UsbFormatMode = value.Value.ToString();
        appSettingsService.Save();
        RefreshEvaluation();
    }

    private void OnAdkStatusChanged(object? sender, AdkStatusChangedEventArgs e)
    {
        if (!appDispatcher.TryEnqueue(RefreshEvaluation))
        {
            logger.Warning("Failed to enqueue Start page ADK status refresh. IsReady={IsReady}", e.Status.CanCreateMedia);
        }
    }

    private void OnExpertDeployConfigurationStateChanged(object? sender, EventArgs e)
    {
        if (!appDispatcher.TryEnqueue(RefreshEvaluation))
        {
            logger.Warning("Failed to enqueue Start page Expert Deploy configuration refresh.");
        }
    }

    private void OnLanguageChanged(object? sender, ApplicationLanguageChangedEventArgs e)
    {
        if (!appDispatcher.TryEnqueue(() =>
        {
            ApplyLocalizedText();
            RefreshEvaluation();
        }))
        {
            logger.Warning(
                "Failed to enqueue Start page localization refresh. OldLanguage={OldLanguage}, NewLanguage={NewLanguage}",
                e.OldLanguage,
                e.NewLanguage);
        }
    }

    private void ApplyLocalizedText()
    {
        PageTitle = localizationService.GetString("StartPage_Title.Text");
        RebuildFormatModes();
        RebuildUsbCandidateDisplayNames();
    }

    private void RefreshEvaluation()
    {
        isLoadingConfiguration = true;
        try
        {
            LoadConfigurationFromSettings();
        }
        finally
        {
            isLoadingConfiguration = false;
        }

        WinPeLanguage = NormalizeCultureName(appSettingsService.Current.Media.WinPeLanguage);
        availableWinPeLanguages = GetAvailableWinPeLanguages();
        MediaPreflightOptions options = CreatePreflightOptions();
        MediaPreflightEvaluation evaluation = MediaPreflightService.Evaluate(options);
        CanGenerateIsoSummary = evaluation.CanGenerateIsoSummary;
        CanGenerateUsbSummary = evaluation.CanGenerateUsbSummary;
        CanCreateIso = evaluation.CanCreateIso && !IsMediaOperationRunning;
        CanCreateUsb = evaluation.CanCreateUsb && !IsMediaOperationRunning;
        FinalExecutionStatus = options.IsFinalExecutionEnabled
            ? localizationService.GetString("StartMedia.FinalExecution.Ready")
            : localizationService.GetString("StartMedia.FinalExecution.Deferred");
        ReadinessSeverity = GetReadinessSeverity(evaluation);
        StatusSummary = BuildStatusText(evaluation);
        GlobalSummary = BuildGlobalSummary(options, evaluation);
        RebuildReadinessItems(options, evaluation);

    }

    private void LoadConfigurationFromSettings()
    {
        IsoOutputPath = appSettingsService.Current.Media.IsoOutputPath;
        SelectedArchitecture = SelectOption(Architectures, ParseEnum(appSettingsService.Current.Media.Architecture, WinPeArchitecture.X64));
        UseCa2023Signature = appSettingsService.Current.Media.UseCa2023Signature;
        RebuildPartitionStyles();
        SelectedFormatMode = SelectOption(FormatModes, ParseEnum(appSettingsService.Current.Media.UsbFormatMode, UsbFormatMode.Quick));
        IncludeDellDrivers = appSettingsService.Current.Media.IncludeDellDrivers;
        IncludeHpDrivers = appSettingsService.Current.Media.IncludeHpDrivers;
        CustomDriverDirectoryPath = appSettingsService.Current.Media.CustomDriverDirectoryPath ?? string.Empty;
    }

    private MediaPreflightOptions CreatePreflightOptions()
    {
        List<WinPeVendorSelection> vendors = [];
        if (IncludeDellDrivers)
        {
            vendors.Add(WinPeVendorSelection.Dell);
        }

        if (IncludeHpDrivers)
        {
            vendors.Add(WinPeVendorSelection.Hp);
        }

        return new MediaPreflightOptions
        {
            IsAdkReady = adkService.CurrentStatus.CanCreateMedia,
            IsNetworkConfigurationReady = expertDeployConfigurationStateService.IsNetworkConfigurationReady,
            IsDeployConfigurationReady = expertDeployConfigurationStateService.IsDeployConfigurationReady,
            IsConnectProvisioningReady = expertDeployConfigurationStateService.IsConnectProvisioningReady,
            AreRequiredSecretsReady = expertDeployConfigurationStateService.AreRequiredSecretsReady,
            IsAutopilotEnabled = expertDeployConfigurationStateService.IsAutopilotEnabled,
            IsAutopilotConfigurationReady = expertDeployConfigurationStateService.IsAutopilotConfigurationReady,
            AutopilotProfileDisplayName = expertDeployConfigurationStateService.SelectedAutopilotProfileDisplayName,
            AutopilotProfileFolderName = expertDeployConfigurationStateService.SelectedAutopilotProfileFolderName,
            IsFinalExecutionEnabled = true,
            IsoOutputPath = IsoOutputPath,
            Architecture = SelectedArchitecture?.Value ?? WinPeArchitecture.X64,
            SignatureMode = UseCa2023Signature ? WinPeSignatureMode.Pca2023 : WinPeSignatureMode.Pca2011,
            UsbPartitionStyle = SelectedPartitionStyle?.Value ?? UsbPartitionStyle.Gpt,
            UsbFormatMode = SelectedFormatMode?.Value ?? UsbFormatMode.Quick,
            WinPeLanguage = WinPeLanguage,
            AvailableWinPeLanguages = availableWinPeLanguages,
            BootImageSource = ResolveBootImageSource(),
            DriverVendors = vendors,
            CustomDriverDirectoryPath = CustomDriverDirectoryPath,
            SelectedUsbDisk = SelectedUsbDisk?.Value
        };
    }

    private void SynchronizeRuntimeTelemetrySettings()
    {
        expertDeployConfigurationStateService.UpdateTelemetry(CreateRuntimeTelemetrySettings(TelemetryRuntimePayloadSources.None));
    }

    private TelemetrySettings CreateRuntimeTelemetrySettings(string runtimePayloadSource)
    {
        return new TelemetrySettings
        {
            IsEnabled = appSettingsService.Current.Telemetry.IsEnabled,
            InstallId = appSettingsService.Current.Telemetry.InstallId,
            HostUrl = TelemetryDefaults.PostHogEuHost,
            ProjectToken = TelemetryDefaults.ProjectToken,
            RuntimePayloadSource = runtimePayloadSource
        };
    }

    private async Task TrackMediaCreatedAsync(
        FinalMediaTarget target,
        MediaPreflightOptions options,
        bool success,
        TimeSpan duration,
        CancellationToken cancellationToken)
    {
        WinPeRuntimePayloadProvisioningOptions runtimePayloadProvisioning = AddReleaseConnectProvisioning(CreateRuntimePayloadProvisioningOptions(
            options.Architecture,
            Constants.WinPeWorkspaceDirectoryPath,
            Constants.WinPeWorkspaceDirectoryPath,
            Constants.WinPeWorkspaceDirectoryPath));

        var properties = new Dictionary<string, object?>
            {
                ["target"] = target == FinalMediaTarget.Iso ? "iso" : "usb",
                ["success"] = success,
                ["duration_seconds"] = Math.Round(duration.TotalSeconds, 2),
                ["architecture"] = options.Architecture.ToString().ToLowerInvariant(),
                ["winpe_language"] = NormalizeCultureName(options.WinPeLanguage).ToLowerInvariant(),
                ["boot_image_source"] = options.BootImageSource.ToString().ToLowerInvariant(),
                ["signature_mode"] = options.SignatureMode.ToString().ToLowerInvariant(),
                ["usb_partition_style"] = target == FinalMediaTarget.Usb
                    ? options.UsbPartitionStyle.ToString().ToLowerInvariant()
                    : "none",
                ["usb_format_mode"] = target == FinalMediaTarget.Usb
                    ? options.UsbFormatMode.ToString().ToLowerInvariant()
                    : "none",
                ["include_dell_drivers"] = options.DriverVendors.Contains(WinPeVendorSelection.Dell),
                ["include_hp_drivers"] = options.DriverVendors.Contains(WinPeVendorSelection.Hp),
                ["custom_drivers_enabled"] = !string.IsNullOrWhiteSpace(options.CustomDriverDirectoryPath),
                ["network_configured"] = options.IsNetworkConfigurationReady,
                ["connect_configured"] = options.IsConnectProvisioningReady,
                ["deploy_configured"] = options.IsDeployConfigurationReady,
                ["connect_runtime_payload_source"] = ResolveRuntimePayloadSource(runtimePayloadProvisioning.Connect),
                ["deploy_runtime_payload_source"] = ResolveRuntimePayloadSource(runtimePayloadProvisioning.Deploy),
                ["autopilot_enabled"] = options.IsAutopilotEnabled
            };

        logger.Debug(
            "Tracking media telemetry event. Target={Target}, Success={Success}, DurationSeconds={DurationSeconds}, Architecture={Architecture}, BootImageSource={BootImageSource}, SignatureMode={SignatureMode}, ConnectRuntimePayloadSource={ConnectRuntimePayloadSource}, DeployRuntimePayloadSource={DeployRuntimePayloadSource}.",
            properties["target"],
            success,
            properties["duration_seconds"],
            properties["architecture"],
            properties["boot_image_source"],
            properties["signature_mode"],
            properties["connect_runtime_payload_source"],
            properties["deploy_runtime_payload_source"]);

        await telemetryService.TrackAsync("boot_media_created", properties, cancellationToken);
        logger.Debug("Media telemetry event queued. Target={Target}, Success={Success}.", properties["target"], success);
    }

    private string BuildStatusText(MediaPreflightEvaluation evaluation)
    {
        if (evaluation.CanCreateIso && evaluation.CanCreateUsb)
        {
            return localizationService.GetString("StartMedia.Readiness.Status.ReadyIsoAndUsb");
        }

        if (evaluation.CanCreateIso)
        {
            return localizationService.GetString("StartMedia.Readiness.Status.ReadyIso");
        }

        if (evaluation.CanCreateUsb)
        {
            return localizationService.GetString("StartMedia.Readiness.Status.ReadyUsb");
        }

        IReadOnlyList<MediaPreflightBlockingReason> blockingReasons = GetGlobalBlockingReasons(evaluation)
            .Where(IsBlockingReadinessReason)
            .ToList();

        return blockingReasons.Count == 0
            ? localizationService.GetString("StartMedia.Readiness.Status.Warnings")
            : string.Format(localizationService.GetString("StartMedia.Readiness.Status.NeedsAttention"), blockingReasons.Count);
    }

    private InfoBarSeverity GetReadinessSeverity(MediaPreflightEvaluation evaluation)
    {
        if (evaluation.CanCreateIso && evaluation.CanCreateUsb)
        {
            return InfoBarSeverity.Success;
        }

        if (evaluation.CanCreateIso || evaluation.CanCreateUsb)
        {
            return InfoBarSeverity.Informational;
        }

        if (!GetGlobalBlockingReasons(evaluation).Any(IsBlockingReadinessReason))
        {
            return InfoBarSeverity.Warning;
        }

        return InfoBarSeverity.Error;
    }

    private void RebuildReadinessItems(MediaPreflightOptions options, MediaPreflightEvaluation evaluation)
    {
        IsPrerequisiteReadinessExpanded = ReplaceReadinessItems(
            PrerequisiteReadinessItems,
            [
                BuildAdkReadinessItem(options),
                BuildWinPeLanguageReadinessItem(options, evaluation),
                BuildDriverReadinessItem(evaluation)
            ]);

        IsMediaOutputReadinessExpanded = ReplaceReadinessItems(
            MediaOutputReadinessItems,
            [
                BuildIsoOutputReadinessItem(options, evaluation),
                BuildUsbTargetReadinessItem(options),
                BuildUsbLayoutReadinessItem(options, evaluation)
            ]);

        IsExpertConfigurationReadinessExpanded = ReplaceReadinessItems(
            ExpertConfigurationReadinessItems,
            [
                BuildNetworkReadinessItem(options),
                BuildDeployConfigurationReadinessItem(options),
                BuildConnectProvisioningReadinessItem(options),
                BuildRequiredSecretsReadinessItem(options),
                BuildAutopilotReadinessItem(options)
            ]);
    }

    private StartReadinessItemViewModel BuildAdkReadinessItem(MediaPreflightOptions options)
    {
        return CreateReadinessItem(
            localizationService.GetString("StartMedia.Field.Adk"),
            options.IsAdkReady ? StartReadinessState.Ready : StartReadinessState.Blocked,
            options.IsAdkReady
                ? localizationService.GetString("StartMedia.Readiness.Adk.Ready")
                : GetBlockingReasonText(MediaPreflightBlockingReason.AdkNotReady),
            options.IsAdkReady ? StartReadinessNavigationTarget.None : StartReadinessNavigationTarget.Adk);
    }

    private StartReadinessItemViewModel BuildWinPeLanguageReadinessItem(MediaPreflightOptions options, MediaPreflightEvaluation evaluation)
    {
        MediaPreflightBlockingReason? reason = GetFirstReason(
            evaluation,
            MediaPreflightBlockingReason.MissingWinPeLanguage,
            MediaPreflightBlockingReason.WinPeLanguageUnavailable);

        return CreateReadinessItem(
            localizationService.GetString("StartMedia.Field.WinPeLanguage"),
            reason.HasValue ? StartReadinessState.Blocked : StartReadinessState.Ready,
            reason.HasValue
                ? GetBlockingReasonText(reason.Value)
                : FormatValue(NormalizeCultureName(options.WinPeLanguage)),
            reason.HasValue ? StartReadinessNavigationTarget.General : StartReadinessNavigationTarget.None);
    }

    private StartReadinessItemViewModel BuildDriverReadinessItem(MediaPreflightEvaluation evaluation)
    {
        MediaPreflightBlockingReason? reason = GetFirstReason(
            evaluation,
            MediaPreflightBlockingReason.CustomDriverDirectoryNotFound,
            MediaPreflightBlockingReason.CustomDriverDirectoryHasNoInfFiles);

        return CreateReadinessItem(
            localizationService.GetString("StartMedia.Field.Drivers"),
            reason.HasValue ? StartReadinessState.Blocked : StartReadinessState.Ready,
            reason.HasValue
                ? GetBlockingReasonText(reason.Value)
                : localizationService.GetString("StartMedia.Readiness.Drivers.Ready"),
            reason.HasValue ? StartReadinessNavigationTarget.General : StartReadinessNavigationTarget.None);
    }

    private StartReadinessItemViewModel BuildIsoOutputReadinessItem(MediaPreflightOptions options, MediaPreflightEvaluation evaluation)
    {
        bool hasInvalidIsoPath = HasReason(evaluation, MediaPreflightBlockingReason.InvalidIsoPath);
        return CreateReadinessItem(
            localizationService.GetString("StartMedia.Field.IsoPath"),
            hasInvalidIsoPath ? StartReadinessState.Warning : StartReadinessState.Ready,
            hasInvalidIsoPath
                ? GetBlockingReasonText(MediaPreflightBlockingReason.InvalidIsoPath)
                : FormatValue(options.IsoOutputPath));
    }

    private StartReadinessItemViewModel BuildUsbTargetReadinessItem(MediaPreflightOptions options)
    {
        StartReadinessState state = usbCandidateDiscoveryState switch
        {
            UsbCandidateDiscoveryState.Loading => StartReadinessState.Loading,
            UsbCandidateDiscoveryState.Empty => StartReadinessState.Warning,
            UsbCandidateDiscoveryState.Error => StartReadinessState.Warning,
            _ => options.SelectedUsbDisk is null ? StartReadinessState.NotConfigured : StartReadinessState.Ready
        };

        string description = state == StartReadinessState.Ready
            ? FormatUsbCandidate(options.SelectedUsbDisk)
            : string.IsNullOrWhiteSpace(UsbCandidateStatus)
                ? localizationService.GetString("StartMedia.Usb.NotLoaded")
                : UsbCandidateStatus;

        return CreateReadinessItem(
            localizationService.GetString("StartMedia.Field.UsbTarget"),
            state,
            description);
    }

    private StartReadinessItemViewModel BuildUsbLayoutReadinessItem(MediaPreflightOptions options, MediaPreflightEvaluation evaluation)
    {
        bool requiresGpt = HasReason(evaluation, MediaPreflightBlockingReason.Arm64RequiresGpt);
        string description = requiresGpt
            ? GetBlockingReasonText(MediaPreflightBlockingReason.Arm64RequiresGpt)
            : string.Format(
                localizationService.GetString("StartMedia.Readiness.UsbLayout.Format"),
                evaluation.EffectiveUsbPartitionStyle,
                FormatUsbFormatMode(options.UsbFormatMode));

        return CreateReadinessItem(
            localizationService.GetString("StartMedia.UsbLayout.Header"),
            requiresGpt ? StartReadinessState.Warning : StartReadinessState.Ready,
            description);
    }

    private StartReadinessItemViewModel BuildNetworkReadinessItem(MediaPreflightOptions options)
    {
        return CreateReadinessItem(
            localizationService.GetString("StartMedia.Field.Network"),
            options.IsNetworkConfigurationReady ? StartReadinessState.Ready : StartReadinessState.Blocked,
            options.IsNetworkConfigurationReady
                ? localizationService.GetString("StartMedia.Readiness.Network.Ready")
                : GetBlockingReasonText(MediaPreflightBlockingReason.NetworkConfigurationNotReady),
            options.IsNetworkConfigurationReady ? StartReadinessNavigationTarget.None : StartReadinessNavigationTarget.Network);
    }

    private StartReadinessItemViewModel BuildDeployConfigurationReadinessItem(MediaPreflightOptions options)
    {
        return CreateReadinessItem(
            localizationService.GetString("StartMedia.Field.Deploy"),
            options.IsDeployConfigurationReady ? StartReadinessState.Ready : StartReadinessState.Blocked,
            options.IsDeployConfigurationReady
                ? localizationService.GetString("StartMedia.Readiness.Deploy.Ready")
                : GetBlockingReasonText(MediaPreflightBlockingReason.DeployConfigurationNotReady),
            options.IsDeployConfigurationReady ? StartReadinessNavigationTarget.None : StartReadinessNavigationTarget.Customization);
    }

    private StartReadinessItemViewModel BuildConnectProvisioningReadinessItem(MediaPreflightOptions options)
    {
        return CreateReadinessItem(
            localizationService.GetString("StartMedia.Field.Connect"),
            options.IsConnectProvisioningReady ? StartReadinessState.Ready : StartReadinessState.Blocked,
            options.IsConnectProvisioningReady
                ? localizationService.GetString("StartMedia.Readiness.Connect.Ready")
                : GetBlockingReasonText(MediaPreflightBlockingReason.ConnectProvisioningNotReady),
            options.IsConnectProvisioningReady ? StartReadinessNavigationTarget.None : StartReadinessNavigationTarget.Network);
    }

    private StartReadinessItemViewModel BuildRequiredSecretsReadinessItem(MediaPreflightOptions options)
    {
        return CreateReadinessItem(
            localizationService.GetString("StartMedia.Field.Secrets"),
            options.AreRequiredSecretsReady ? StartReadinessState.Ready : StartReadinessState.Blocked,
            options.AreRequiredSecretsReady
                ? localizationService.GetString("StartMedia.Readiness.Secrets.Ready")
                : GetBlockingReasonText(MediaPreflightBlockingReason.RequiredSecretsNotReady),
            options.AreRequiredSecretsReady ? StartReadinessNavigationTarget.None : StartReadinessNavigationTarget.Network);
    }

    private StartReadinessItemViewModel BuildAutopilotReadinessItem(MediaPreflightOptions options)
    {
        if (!options.IsAutopilotEnabled)
        {
            return CreateReadinessItem(
                localizationService.GetString("StartMedia.Field.Autopilot"),
                StartReadinessState.NotConfigured,
                localizationService.GetString("StartMedia.Readiness.Autopilot.Disabled"));
        }

        return CreateReadinessItem(
            localizationService.GetString("StartMedia.Field.Autopilot"),
            options.IsAutopilotConfigurationReady ? StartReadinessState.Ready : StartReadinessState.Blocked,
            options.IsAutopilotConfigurationReady
                ? FormatAutopilot(options)
                : GetBlockingReasonText(MediaPreflightBlockingReason.AutopilotConfigurationNotReady),
            options.IsAutopilotConfigurationReady ? StartReadinessNavigationTarget.None : StartReadinessNavigationTarget.Autopilot);
    }

    private StartReadinessItemViewModel CreateReadinessItem(
        string title,
        StartReadinessState state,
        string description,
        StartReadinessNavigationTarget navigationTarget = StartReadinessNavigationTarget.None)
    {
        return new StartReadinessItemViewModel(
            title,
            description,
            localizationService.GetString($"StartMedia.Readiness.State.{state}"),
            GetReadinessGlyph(state),
            state is StartReadinessState.Blocked or StartReadinessState.Warning,
            navigationTarget,
            navigationTarget == StartReadinessNavigationTarget.None
                ? string.Empty
                : localizationService.GetString("StartMedia.Readiness.Action.Review"));
    }

    private static bool ReplaceReadinessItems(
        ObservableCollection<StartReadinessItemViewModel> target,
        IEnumerable<StartReadinessItemViewModel> items)
    {
        target.Clear();
        bool hasAttentionItem = false;
        foreach (StartReadinessItemViewModel item in items)
        {
            target.Add(item);
            hasAttentionItem |= item.ExpandsGroup;
        }

        return hasAttentionItem;
    }

    private static MediaPreflightBlockingReason? GetFirstReason(
        MediaPreflightEvaluation evaluation,
        params MediaPreflightBlockingReason[] reasons)
    {
        foreach (MediaPreflightBlockingReason reason in reasons)
        {
            if (HasReason(evaluation, reason))
            {
                return reason;
            }
        }

        return null;
    }

    private static bool HasReason(MediaPreflightEvaluation evaluation, MediaPreflightBlockingReason reason)
    {
        return evaluation.IsoBlockingReasons.Contains(reason) || evaluation.UsbBlockingReasons.Contains(reason);
    }

    private static string GetReadinessGlyph(StartReadinessState state)
    {
        return state switch
        {
            StartReadinessState.Ready => "\uE8FB",
            StartReadinessState.Warning => "\uE7BA",
            StartReadinessState.Blocked => "\uE711",
            StartReadinessState.Loading => "\uE895",
            _ => "\uE946"
        };
    }

    private string BuildGlobalSummary(MediaPreflightOptions options, MediaPreflightEvaluation evaluation)
    {
        IReadOnlyList<MediaPreflightBlockingReason> reasons = GetGlobalBlockingReasons(evaluation);
        var builder = new StringBuilder();
        builder.AppendLine($"{localizationService.GetString("StartMedia.Field.Adk")}: {FormatReady(adkService.CurrentStatus.CanCreateMedia)}");
        builder.AppendLine($"{localizationService.GetString("StartMedia.Field.WinPeLanguage")}: {FormatValue(NormalizeCultureName(options.WinPeLanguage))}");
        builder.AppendLine($"{localizationService.GetString("StartMedia.Field.BootImageSource")}: {FormatBootImageSource(options.BootImageSource)}");
        builder.AppendLine($"{localizationService.GetString("StartMedia.Field.Architecture")}: {FormatArchitecture(options.Architecture)}");
        builder.AppendLine($"{localizationService.GetString("StartMedia.Field.IsoPath")}: {FormatValue(options.IsoOutputPath)}");
        builder.AppendLine($"{localizationService.GetString("StartMedia.Field.UsbTarget")}: {FormatUsbCandidate(options.SelectedUsbDisk)}");
        builder.AppendLine($"{localizationService.GetString("StartMedia.Field.Signature")}: {FormatSignatureMode(options.SignatureMode)}");
        builder.AppendLine($"{localizationService.GetString("StartMedia.Field.PartitionStyle")}: {evaluation.EffectiveUsbPartitionStyle}");
        builder.AppendLine($"{localizationService.GetString("StartMedia.Field.FormatMode")}: {FormatUsbFormatMode(options.UsbFormatMode)}");
        builder.AppendLine($"{localizationService.GetString("StartMedia.Field.Drivers")}: {FormatDriverOptions(options.DriverVendors, options.CustomDriverDirectoryPath)}");
        builder.AppendLine($"{localizationService.GetString("StartMedia.Field.Network")}: {FormatReady(options.IsNetworkConfigurationReady)}");
        builder.AppendLine($"{localizationService.GetString("StartMedia.Field.Deploy")}: {FormatReady(options.IsDeployConfigurationReady)}");
        builder.AppendLine($"{localizationService.GetString("StartMedia.Field.Connect")}: {FormatReady(options.IsConnectProvisioningReady)}");
        builder.AppendLine($"{localizationService.GetString("StartMedia.Field.Secrets")}: {FormatReady(options.AreRequiredSecretsReady)}");
        builder.AppendLine($"{localizationService.GetString("StartMedia.Field.Autopilot")}: {FormatAutopilot(options)}");
        builder.AppendLine();
        builder.AppendLine(options.IsFinalExecutionEnabled
            ? localizationService.GetString("StartMedia.FinalExecution.Ready")
            : localizationService.GetString("StartMedia.FinalExecution.Deferred"));

        AppendBlockingReasons(builder, reasons);

        return builder.ToString();
    }

    private void AppendBlockingReasons(StringBuilder builder, IReadOnlyList<MediaPreflightBlockingReason> reasons)
    {
        if (reasons.Count == 0)
        {
            return;
        }

        builder.AppendLine();
        builder.AppendLine(localizationService.GetString("StartMedia.Summary.BlockingReasons"));
        foreach (MediaPreflightBlockingReason reason in reasons)
        {
            builder.AppendLine($"- {GetBlockingReasonText(reason)}");
        }
    }

    private string GetBlockingReasonText(MediaPreflightBlockingReason reason)
    {
        return localizationService.GetString($"StartMedia.BlockingReason.{reason}");
    }

    private void LogPreflightSummary(MediaPreflightOptions options, MediaPreflightEvaluation evaluation)
    {
        IReadOnlyList<MediaPreflightBlockingReason> reasons = GetGlobalBlockingReasons(evaluation);

        logger.Information(
            "Media preflight summary refreshed. Architecture={Architecture}, WinPeLanguage={WinPeLanguage}, BootImageSource={BootImageSource}, IsoOutputPath={IsoOutputPath}, UsbTargetSelected={UsbTargetSelected}, DiskNumber={DiskNumber}, DiskName={DiskName}, NetworkReady={NetworkReady}, DeployReady={DeployReady}, ConnectReady={ConnectReady}, SecretsReady={SecretsReady}, AutopilotEnabled={AutopilotEnabled}, AutopilotReady={AutopilotReady}, AutopilotProfile={AutopilotProfile}, AutopilotFolder={AutopilotFolder}, SummaryReady={SummaryReady}, BlockingReasons={BlockingReasons}",
            options.Architecture,
            NormalizeCultureName(options.WinPeLanguage),
            options.BootImageSource,
            options.IsoOutputPath,
            options.SelectedUsbDisk is not null,
            options.SelectedUsbDisk?.DiskNumber,
            options.SelectedUsbDisk?.FriendlyName,
            options.IsNetworkConfigurationReady,
            options.IsDeployConfigurationReady,
            options.IsConnectProvisioningReady,
            options.AreRequiredSecretsReady,
            options.IsAutopilotEnabled,
            options.IsAutopilotConfigurationReady,
            options.AutopilotProfileDisplayName,
            options.AutopilotProfileFolderName,
            evaluation.CanGenerateIsoSummary || evaluation.CanGenerateUsbSummary,
            string.Join(",", reasons));
    }

    private static IReadOnlyList<MediaPreflightBlockingReason> GetGlobalBlockingReasons(MediaPreflightEvaluation evaluation)
    {
        return evaluation.IsoBlockingReasons
            .Concat(evaluation.UsbBlockingReasons)
            .Where(reason => reason != MediaPreflightBlockingReason.NoUsbTarget)
            .Distinct()
            .ToList();
    }

    private static bool IsBlockingReadinessReason(MediaPreflightBlockingReason reason)
    {
        return reason is not (MediaPreflightBlockingReason.InvalidIsoPath
            or MediaPreflightBlockingReason.NoUsbTarget
            or MediaPreflightBlockingReason.Arm64RequiresGpt);
    }

    private IReadOnlyList<string> GetAvailableWinPeLanguages()
    {
        if (!adkService.CurrentStatus.CanCreateMedia)
        {
            return [];
        }

        WinPeResult<WinPeToolPaths> toolsResult = new WinPeToolResolver().ResolveTools(adkService.CurrentStatus.KitsRootPath);
        if (!toolsResult.IsSuccess || toolsResult.Value is null)
        {
            logger.Warning("WinPE language validation skipped because ADK tools were not resolved. ErrorCode={ErrorCode}", toolsResult.Error?.Code);
            return [];
        }

        WinPeResult<IReadOnlyList<string>> languagesResult = languageDiscoveryService.GetAvailableLanguages(
            new WinPeLanguageDiscoveryOptions
            {
                Tools = toolsResult.Value,
                Architecture = SelectedArchitecture?.Value ?? WinPeArchitecture.X64
            });

        if (!languagesResult.IsSuccess || languagesResult.Value is null)
        {
            logger.Warning("WinPE language validation skipped because language discovery failed. ErrorCode={ErrorCode}", languagesResult.Error?.Code);
            return [];
        }

        return languagesResult.Value.Select(NormalizeCultureName).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private void RebuildFormatModes()
    {
        UsbFormatMode selectedValue = SelectedFormatMode?.Value
            ?? ParseEnum(appSettingsService.Current.Media.UsbFormatMode, UsbFormatMode.Quick);

        FormatModes.Clear();
        FormatModes.Add(new(UsbFormatMode.Quick, localizationService.GetString("StartMedia.FormatMode.Quick")));
        FormatModes.Add(new(UsbFormatMode.Complete, localizationService.GetString("StartMedia.FormatMode.Complete")));
        SelectedFormatMode = SelectOption(FormatModes, selectedValue);
    }

    private void RebuildPartitionStyles()
    {
        UsbPartitionStyle selectedValue = SelectedArchitecture?.Value == WinPeArchitecture.Arm64
            ? UsbPartitionStyle.Gpt
            : SelectedPartitionStyle?.Value ?? ParseEnum(appSettingsService.Current.Media.UsbPartitionStyle, UsbPartitionStyle.Gpt);

        PartitionStyles.Clear();
        PartitionStyles.Add(new(UsbPartitionStyle.Gpt, "GPT"));

        if (SelectedArchitecture?.Value != WinPeArchitecture.Arm64)
        {
            PartitionStyles.Add(new(UsbPartitionStyle.Mbr, "MBR"));
        }

        SelectedPartitionStyle = SelectOption(PartitionStyles, selectedValue) ?? PartitionStyles[0];
    }

    private void RebuildUsbCandidateDisplayNames()
    {
        List<WinPeUsbDiskCandidate> candidates = UsbCandidates.Select(option => option.Value).ToList();
        WinPeUsbDiskCandidate? selectedCandidate = SelectedUsbDisk?.Value;

        UsbCandidates.Clear();
        foreach (WinPeUsbDiskCandidate candidate in candidates)
        {
            UsbCandidates.Add(CreateUsbDiskOption(candidate));
        }

        SelectedUsbDisk = UsbCandidates.FirstOrDefault(option => option.Value.DiskNumber == selectedCandidate?.DiskNumber)
            ?? UsbCandidates.FirstOrDefault();
    }

    private SelectionOption<WinPeUsbDiskCandidate> CreateUsbDiskOption(WinPeUsbDiskCandidate candidate)
    {
        return new SelectionOption<WinPeUsbDiskCandidate>(
            candidate,
            string.Format(
                localizationService.GetString("StartMedia.Usb.DiskDisplay"),
                candidate.DiskNumber,
                candidate.FriendlyName,
                FormatByteSize(candidate.SizeBytes)));
    }

    private string FormatReady(bool isReady)
    {
        return isReady ? localizationService.GetString("Common.Enabled") : localizationService.GetString("Common.Disabled");
    }

    private string FormatAutopilot(MediaPreflightOptions options)
    {
        if (!options.IsAutopilotEnabled)
        {
            return FormatReady(false);
        }

        if (!options.IsAutopilotConfigurationReady)
        {
            return localizationService.GetString("StartMedia.Autopilot.NotReady");
        }

        return string.Format(
            localizationService.GetString("StartMedia.Autopilot.ProfileFormat"),
            FormatValue(options.AutopilotProfileDisplayName),
            FormatValue(options.AutopilotProfileFolderName));
    }

    private static string FormatValue(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? "-" : value;
    }

    private static string NormalizeCultureName(string? language)
    {
        if (string.IsNullOrWhiteSpace(language))
        {
            return string.Empty;
        }

        try
        {
            return CultureInfo.GetCultureInfo(language).Name;
        }
        catch (CultureNotFoundException)
        {
            return language;
        }
    }

    private string FormatDriverOptions(IReadOnlyList<WinPeVendorSelection> vendors, string? customDriverDirectoryPath)
    {
        List<string> parts = vendors.Select(FormatDriverVendor).ToList();
        if (!string.IsNullOrWhiteSpace(customDriverDirectoryPath))
        {
            parts.Add(customDriverDirectoryPath);
        }

        return parts.Count == 0 ? "-" : string.Join(", ", parts);
    }

    private string FormatUsbCandidate(WinPeUsbDiskCandidate? candidate)
    {
        if (candidate is null)
        {
            return "-";
        }

        return string.Format(
            localizationService.GetString("StartMedia.Usb.DiskDisplay"),
            candidate.DiskNumber,
            candidate.FriendlyName,
            FormatByteSize(candidate.SizeBytes));
    }

    private string FormatArchitecture(WinPeArchitecture architecture)
    {
        return architecture switch
        {
            WinPeArchitecture.X64 => "x64",
            WinPeArchitecture.Arm64 => "arm64",
            _ => architecture.ToString()
        };
    }

    private string FormatSignatureMode(WinPeSignatureMode signatureMode)
    {
        return localizationService.GetString($"StartMedia.SignatureMode.{signatureMode}");
    }

    private string FormatBootImageSource(WinPeBootImageSource bootImageSource)
    {
        return localizationService.GetString($"StartMedia.BootImageSource.{bootImageSource}");
    }

    private string FormatUsbFormatMode(UsbFormatMode formatMode)
    {
        return localizationService.GetString($"StartMedia.FormatMode.{formatMode}");
    }

    private string FormatDriverVendor(WinPeVendorSelection vendor)
    {
        return localizationService.GetString($"StartMedia.DriverVendor.{vendor}");
    }

    private string FormatByteSize(ulong bytes)
    {
        double size = bytes;
        string[] units =
        [
            localizationService.GetString("StartMedia.ByteUnit.B"),
            localizationService.GetString("StartMedia.ByteUnit.KB"),
            localizationService.GetString("StartMedia.ByteUnit.MB"),
            localizationService.GetString("StartMedia.ByteUnit.GB"),
            localizationService.GetString("StartMedia.ByteUnit.TB")
        ];
        int unitIndex = 0;
        while (size >= 1024 && unitIndex < units.Length - 1)
        {
            size /= 1024;
            unitIndex++;
        }

        return $"{size:0.#} {units[unitIndex]}";
    }

    private WinPeToolPaths ResolveWinPeToolsOrThrow()
    {
        WinPeResult<WinPeToolPaths> result = new WinPeToolResolver().ResolveTools(adkService.CurrentStatus.KitsRootPath);
        EnsureSuccess(result);
        return result.Value!;
    }

    private WinPeBootImageSource ResolveBootImageSource()
    {
        NetworkSettings network = expertDeployConfigurationStateService.Current.Network;
        return network.WifiProvisioned || network.Wifi.IsEnabled
            ? WinPeBootImageSource.WinReWifi
            : WinPeBootImageSource.WinPe;
    }

    private WinPeRuntimePayloadProvisioningOptions CreateRuntimePayloadProvisioningOptions(
        WinPeArchitecture architecture,
        string workingDirectoryPath,
        string mountedImagePath,
        string usbCacheRootPath)
    {
        return WinPeRuntimePayloadProvisioningOptions.CreateDeveloperOptions(
            architecture,
            workingDirectoryPath,
            mountedImagePath,
            usbCacheRootPath,
            Debugger.IsAttached,
            projectDiscoveryStartPath: FindRepositoryRoot());
    }

    private static WinPeRuntimePayloadProvisioningOptions AddReleaseConnectProvisioning(
        WinPeRuntimePayloadProvisioningOptions options)
    {
        if (options.Connect.IsEnabled)
        {
            return options;
        }

        return options with
        {
            Connect = new WinPeRuntimePayloadApplicationOptions
            {
                IsEnabled = true,
                ProvisioningSource = WinPeProvisioningSource.Release
            }
        };
    }

    private static string ResolveCurlExecutablePath()
    {
        string systemCurlPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.System),
            "curl.exe");

        return File.Exists(systemCurlPath) ? systemCurlPath : "curl.exe";
    }

    private static string? FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "src", "Foundry.Connect", "Foundry.Connect.csproj")) &&
                File.Exists(Path.Combine(current.FullName, "src", "Foundry.Deploy", "Foundry.Deploy.csproj")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        return null;
    }

    private static void EnsureSuccess(WinPeResult result)
    {
        if (result.IsSuccess)
        {
            return;
        }

        throw new InvalidOperationException(FormatWinPeError(result.Error));
    }

    private static string FormatWinPeError(WinPeDiagnostic? diagnostic)
    {
        if (diagnostic is null)
        {
            return "WinPE operation failed.";
        }

        return string.IsNullOrWhiteSpace(diagnostic.Details)
            ? diagnostic.Message
            : $"{diagnostic.Message}{Environment.NewLine}{diagnostic.Details}";
    }

    private static T ParseEnum<T>(string? value, T fallback)
        where T : struct, Enum
    {
        return Enum.TryParse(value, ignoreCase: true, out T result) ? result : fallback;
    }

    private static SelectionOption<T>? SelectOption<T>(IEnumerable<SelectionOption<T>> options, T value)
    {
        return options.FirstOrDefault(option => EqualityComparer<T>.Default.Equals(option.Value, value));
    }

    private enum FinalMediaTarget
    {
        Iso,
        Usb
    }

    private enum StartReadinessState
    {
        Ready,
        Warning,
        Blocked,
        NotConfigured,
        Loading
    }

    private enum UsbCandidateDiscoveryState
    {
        NotLoaded,
        Loading,
        Ready,
        Empty,
        Error,
        Blocked
    }

    private sealed record PreparedMediaWorkspace(
        WinPeWorkspacePreparationResult PreparedWorkspace,
        WinPeToolPaths Tools,
        WinPeRuntimePayloadProvisioningOptions RuntimePayloadProvisioning);
}
