// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Foundry.Core.Models.Configuration;
using Foundry.Core.Services.Application;
using Foundry.Core.Services.Configuration;
using Foundry.Core.Services.Drivers;
using Foundry.Core.Services.Images;
using Foundry.Core.Services.Packages;
using Foundry.Core.Services.Media;
using Foundry.Core.Services.Profiles;
using Foundry.Core.Services.Telemetry;
using Foundry.Core.Services.WinPe;
using Foundry.Services.Adk;
using Foundry.Services.Configuration;
using Foundry.Services.Localization;
using Foundry.Services.Media;
using Foundry.Services.Operations;
using Foundry.Services.Settings;
using Foundry.Services.Shell;
using Foundry.Services.Updates;
using Foundry.Telemetry;
using Foundry.Utilities.Globalization;
using Foundry.Utilities.Storage;
using Serilog;
using Serilog.Context;

namespace Foundry.ViewModels;

/// <summary>
/// Coordinates media creation options, readiness evaluation, and final ISO, USB or PXE creation commands.
/// </summary>
public sealed partial class StartMediaViewModel : ObservableObject, IDisposable
{
    private readonly IAppSettingsService appSettingsService;
    private readonly IAdkService adkService;
    private readonly IWinPeLanguageDiscoveryService languageDiscoveryService;
    private readonly IWinPeEmbeddedAssetService embeddedAssetService;
    private readonly IWinPeBuildService buildService;
    private readonly IWinPeWorkspacePreparationService workspacePreparationService;
    private readonly WinPeWorkspaceCleanupService workspaceCleanupService;
    private readonly IWinPeRuntimePayloadProvisioningService runtimePayloadProvisioningService;
    private readonly IWinPeIsoMediaService isoMediaService;
    private readonly IWinPePxeMediaService pxeMediaService;
    private readonly IWinPeUsbMediaService usbMediaService;
    private readonly CustomImageLibraryService customImageLibrary;
    private readonly ContentPackageLibraryService postInstallPackages;
    private readonly CustomDriverSetLibrary customDriverSetLibrary;
    private readonly IFilePickerService filePickerService;
    private readonly IFoundryConfigurationStateService foundryConfigurationStateService;
    private readonly IConfigurationOverviewService configurationOverviewService;
    private readonly INetworkSecretStateService networkSecretStateService;
    private readonly ITelemetryService telemetryService;
    private readonly IOperationProgressService operationProgressService;
    private readonly IShellNavigationGuardService shellNavigationGuardService;
    private readonly IApplicationUpdateStateService applicationUpdateStateService;
    private readonly IMediaUpdateAdvisoryDialogService mediaUpdateAdvisoryDialogService;
    private readonly IPxeOutputDialogService pxeOutputDialogService;
    private readonly IApplicationUpdateRestartService applicationUpdateRestartService;
    private readonly IAppNavigationService appNavigationService;
    private readonly IDialogService dialogService;
    private readonly IApplicationLocalizationService localizationService;
    private readonly IExternalProcessLauncher externalProcessLauncher;
    private readonly IAppDispatcher appDispatcher;
    private readonly ILogger logger;
    private IReadOnlyList<string> availableWinPeLanguages = [];
    private bool isLoadingConfiguration = true;
    private MediaCreationProgressPlan? progressPlan;
    private int lastFinalMediaPercent = -1;
    private WinPeBootImageSource preparedBootImageSource = WinPeBootImageSource.WinRe;
    private string? lastCustomizationLogStatus;
    private int lastCustomizationLogBucket = -1;
    private string? lastDownloadLogStatus;
    private int lastDownloadLogBucket = -1;
    private string? lastFinalMediaLogStatus;
    private int lastFinalMediaLogBucket = -1;
    private bool isRefreshingUnattendSources;
    private bool isUpdateAdvisoryPending;
    private bool isDisposed;

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
        WinPeWorkspaceCleanupService workspaceCleanupService,
        IWinPeRuntimePayloadProvisioningService runtimePayloadProvisioningService,
        IWinPeIsoMediaService isoMediaService,
        IWinPePxeMediaService pxeMediaService,
        IWinPeUsbMediaService usbMediaService,
        CustomImageLibraryService customImageLibrary,
        ContentPackageLibraryService postInstallPackages,
        CustomDriverSetLibrary customDriverSetLibrary,
        IFilePickerService filePickerService,
        IFoundryConfigurationStateService foundryConfigurationStateService,
        IConfigurationOverviewService configurationOverviewService,
        INetworkSecretStateService networkSecretStateService,
        ITelemetryService telemetryService,
        IOperationProgressService operationProgressService,
        IShellNavigationGuardService shellNavigationGuardService,
        IApplicationUpdateStateService applicationUpdateStateService,
        IMediaUpdateAdvisoryDialogService mediaUpdateAdvisoryDialogService,
        IPxeOutputDialogService pxeOutputDialogService,
        IApplicationUpdateRestartService applicationUpdateRestartService,
        IAppNavigationService appNavigationService,
        IDialogService dialogService,
        IApplicationLocalizationService localizationService,
        IExternalProcessLauncher externalProcessLauncher,
        IAppDispatcher appDispatcher,
        ILogger logger)
    {
        this.appSettingsService = appSettingsService;
        this.adkService = adkService;
        this.languageDiscoveryService = languageDiscoveryService;
        this.embeddedAssetService = embeddedAssetService;
        this.buildService = buildService;
        this.workspacePreparationService = workspacePreparationService;
        this.workspaceCleanupService = workspaceCleanupService;
        this.runtimePayloadProvisioningService = runtimePayloadProvisioningService;
        this.isoMediaService = isoMediaService;
        this.pxeMediaService = pxeMediaService;
        this.usbMediaService = usbMediaService;
        this.customImageLibrary = customImageLibrary;
        this.postInstallPackages = postInstallPackages;
        this.customDriverSetLibrary = customDriverSetLibrary;
        this.filePickerService = filePickerService;
        this.foundryConfigurationStateService = foundryConfigurationStateService;
        this.configurationOverviewService = configurationOverviewService;
        this.networkSecretStateService = networkSecretStateService;
        this.telemetryService = telemetryService;
        this.operationProgressService = operationProgressService;
        this.shellNavigationGuardService = shellNavigationGuardService;
        this.applicationUpdateStateService = applicationUpdateStateService;
        this.mediaUpdateAdvisoryDialogService = mediaUpdateAdvisoryDialogService;
        this.pxeOutputDialogService = pxeOutputDialogService;
        this.applicationUpdateRestartService = applicationUpdateRestartService;
        this.appNavigationService = appNavigationService;
        this.dialogService = dialogService;
        this.localizationService = localizationService;
        this.externalProcessLauncher = externalProcessLauncher;
        this.appDispatcher = appDispatcher;
        this.logger = logger.ForContext<StartMediaViewModel>();

        Architectures =
        [
            new(WinPeArchitecture.X64, "x64"),
            new(WinPeArchitecture.Arm64, "arm64")
        ];
        PartitionStyles = [];
        FormatModes = [];

        GeneralSettings general = foundryConfigurationStateService.Current.General;
        IsoOutputPath = general.IsoOutputPath ?? string.Empty;
        SelectedArchitecture = SelectOption(Architectures, general.Architecture);
        UseCa2023Signature = general.UseCa2023;
        RebuildPartitionStyles();
        IncludeDellDrivers = general.IncludeDellDrivers;
        IncludeHpDrivers = general.IncludeHpDrivers;
        CustomDriverDirectoryPath = general.CustomDriverDirectoryPath ?? string.Empty;

        configurationOverviewService.Changed += OnConfigurationOverviewChanged;
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
    /// Gets the disks connected by USB (flash drives and external disks) that are not the Windows system or boot disk.
    /// </summary>
    public ObservableCollection<SelectionOption<WinPeUsbDiskCandidate>> UsbCandidates { get; } = [];

    /// <summary>
    /// Gets the General configuration overview rows.
    /// </summary>
    public ObservableCollection<StartConfigurationOverviewItemViewModel> GeneralConfigurationOverviewItems { get; } = [];

    /// <summary>
    /// Gets the network configuration overview rows.
    /// </summary>
    public ObservableCollection<StartConfigurationOverviewItemViewModel> NetworkOverviewItems { get; } = [];

    /// <summary>
    /// Gets the Windows Autopilot overview rows.
    /// </summary>
    public ObservableCollection<StartConfigurationOverviewItemViewModel> AutopilotOverviewItems { get; } = [];

    /// <summary>Gets both exclusive domain joining mode overview rows.</summary>
    public ObservableCollection<StartConfigurationOverviewItemViewModel> DomainJoinOverviewItems { get; } = [];

    /// <summary>
    /// Gets the customization overview rows.
    /// </summary>
    public ObservableCollection<StartConfigurationOverviewItemViewModel> CustomizationOverviewItems { get; } = [];

    [ObservableProperty]
    public partial string PageTitle { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string PageDescription { get; set; } = string.Empty;

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
    public partial bool IsSelectedUsbFoundryMedia { get; set; }

    [ObservableProperty]
    public partial bool IsRefreshingUsbCandidates { get; set; }

    [ObservableProperty]
    public partial bool CanGenerateIsoSummary { get; set; }

    [ObservableProperty]
    public partial bool CanGenerateUsbSummary { get; set; }

    [ObservableProperty]
    public partial bool CanSelectUsbDisk { get; set; }

    [ObservableProperty]
    public partial bool CanCreateIso { get; set; }

    [ObservableProperty]
    public partial bool CanCreateUsb { get; set; }

    [ObservableProperty]
    public partial bool CanCreatePxe { get; set; }

    [ObservableProperty]
    public partial bool IsMediaOperationRunning { get; set; }

    [ObservableProperty]
    public partial string StatusSummary { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string FinalExecutionStatus { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string WinPeLanguage { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string UsbCandidateStatus { get; set; } = string.Empty;

    [ObservableProperty]
    public partial InfoBarSeverity ReadinessSeverity { get; set; } = InfoBarSeverity.Informational;

    [ObservableProperty]
    public partial bool IsGeneralConfigurationOverviewExpanded { get; set; }

    [ObservableProperty]
    public partial bool IsNetworkOverviewExpanded { get; set; }

    [ObservableProperty]
    public partial bool IsAutopilotOverviewExpanded { get; set; }

    [ObservableProperty]
    public partial bool IsDomainJoinOverviewExpanded { get; set; }

    [ObservableProperty]
    public partial bool IsCustomizationOverviewExpanded { get; set; }

    [ObservableProperty]
    public partial string GeneralConfigurationOverviewSummary { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string NetworkOverviewSummary { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string AutopilotOverviewSummary { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string DomainJoinOverviewSummary { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string CustomizationOverviewSummary { get; set; } = string.Empty;

    public string DocumentationUrl => FoundryApplicationInfo.StartDocumentationUrl;

    /// <inheritdoc />
    public void Dispose()
    {
        isDisposed = true;
        configurationOverviewService.Changed -= OnConfigurationOverviewChanged;
        localizationService.LanguageChanged -= OnLanguageChanged;
    }

    /// <summary>
    /// Refreshes readiness and disk candidates after the page is loaded.
    /// </summary>
    public async Task InitializeAsync()
    {
        RefreshEvaluation();
        // Driver set availability is cached; check it off the UI thread so the overview and the media gate use current library state.
        await foundryConfigurationStateService.RefreshCustomDriversReadinessAsync();

        if (adkService.CurrentStatus.CanCreateMediaFor(SelectedArchitecture?.Value ?? WinPeArchitecture.X64))
        {
            await RefreshUsbCandidatesAsync();
        }
        else
        {
            await RefreshUnattendSourcesAsync();
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
                (FilePickerTypeChoice[])[new(
                    localizationService.GetString("StartMedia.IsoPicker.Filter"),
                    (string[])[".iso"])],
                ".iso"));

        if (!string.IsNullOrWhiteSpace(path))
        {
            IsoOutputPath = path;
        }
    }

    [RelayCommand]
    private async Task RefreshUsbCandidatesAsync()
    {
        await RefreshUnattendSourcesAsync();
        if (isDisposed)
        {
            return;
        }

        if (!adkService.CurrentStatus.CanCreateMediaFor(SelectedArchitecture?.Value ?? WinPeArchitecture.X64))
        {
            UsbCandidateStatus = localizationService.GetString("StartMedia.Usb.AdkBlocked");
            RefreshEvaluation();
            return;
        }

        WinPeResult<WinPeToolPaths> toolsResult = new WinPeToolResolver().ResolveTools(adkService.CurrentStatus.KitsRootPath);
        if (!toolsResult.IsSuccess || toolsResult.Value is null)
        {
            UsbCandidateStatus = toolsResult.Error?.Message ?? localizationService.GetString("StartMedia.Usb.QueryFailed");
            logger.Warning("USB target refresh skipped because ADK tools were not resolved. ErrorCode={ErrorCode}", toolsResult.Error?.Code);
            RefreshEvaluation();
            return;
        }

        IsRefreshingUsbCandidates = true;
        UsbCandidateStatus = localizationService.GetString("StartMedia.Usb.Loading");
        RefreshEvaluation();

        try
        {
            WinPeResult<IReadOnlyList<WinPeUsbDiskCandidate>> result = await usbMediaService.GetUsbCandidatesAsync(
                toolsResult.Value,
                Constants.UsbQueryTempDirectoryPath,
                CancellationToken.None);

            WinPeUsbDiskCandidate? previousSelection = SelectedUsbDisk?.Value;
            UsbCandidates.Clear();
            if (result.IsSuccess && result.Value is not null)
            {
                foreach (WinPeUsbDiskCandidate candidate in result.Value)
                {
                    UsbCandidates.Add(CreateUsbDiskOption(candidate));
                }

                WinPeUsbDiskCandidate? selectedCandidate = WinPeUsbDiskCandidateSelector.Reselect(result.Value, previousSelection);
                SelectedUsbDisk = UsbCandidates.FirstOrDefault(option => ReferenceEquals(option.Value, selectedCandidate));
                UsbCandidateStatus = UsbCandidates.Count == 0
                    ? localizationService.GetString("StartMedia.Usb.NoCandidatesFound")
                    : string.Format(localizationService.GetString("StartMedia.Usb.CandidatesFound"), UsbCandidates.Count);
                logger.Information("USB targets refreshed. CandidateCount={CandidateCount}", UsbCandidates.Count);
            }
            else
            {
                SelectedUsbDisk = null;
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
        if (isDisposed || IsMediaOperationRunning || isUpdateAdvisoryPending || isRefreshingUnattendSources || shellNavigationGuardService.State != ShellNavigationState.Ready)
        {
            return;
        }

        await RefreshUnattendSourcesAsync();
        if (isDisposed || shellNavigationGuardService.State != ShellNavigationState.Ready)
        {
            return;
        }

        MediaPreflightOptions options = CreatePreflightOptions();
        MediaPreflightEvaluation evaluation = MediaPreflightService.Evaluate(options);
        if (!evaluation.CanCreateIso)
        {
            logger.Information(
                "ISO media creation was blocked by preflight validation. BlockingReasons={BlockingReasons}",
                string.Join(",", evaluation.IsoBlockingReasons));
            await ShowBlockedDialogAsync(
                "StartMedia.CreateIso.BlockedTitle",
                evaluation.IsoBlockingReasons,
                options.AutopilotConfigurationValidationCode);
            return;
        }

        await RunFinalMediaOperationAsync(FinalMediaTarget.Iso, options);
    }

    [RelayCommand]
    private async Task CreateUsbAsync()
    {
        if (isDisposed || IsMediaOperationRunning || isUpdateAdvisoryPending || isRefreshingUnattendSources || shellNavigationGuardService.State != ShellNavigationState.Ready)
        {
            return;
        }

        await RefreshUnattendSourcesAsync();
        if (isDisposed || shellNavigationGuardService.State != ShellNavigationState.Ready)
        {
            return;
        }

        MediaPreflightOptions options = CreatePreflightOptions();
        MediaPreflightEvaluation evaluation = MediaPreflightService.Evaluate(options);
        if (!evaluation.CanCreateUsb || options.SelectedUsbDisk is null)
        {
            logger.Information(
                "USB media creation was blocked by preflight validation. HasSelectedUsbTarget={HasSelectedUsbTarget}, BlockingReasons={BlockingReasons}",
                options.SelectedUsbDisk is not null,
                string.Join(",", evaluation.UsbBlockingReasons));
            await ShowBlockedDialogAsync(
                "StartMedia.CreateUsb.BlockedTitle",
                evaluation.UsbBlockingReasons,
                options.AutopilotConfigurationValidationCode);
            return;
        }

        if (options.SelectedUsbDisk.IsFoundryMedia)
        {
            await RunFinalMediaOperationAsync(FinalMediaTarget.UsbUpdate, options);
            return;
        }

        await RunFinalMediaOperationAsync(FinalMediaTarget.Usb, options);
    }

    [RelayCommand]
    private async Task CreatePxeAsync()
    {
        if (isDisposed || IsMediaOperationRunning || isUpdateAdvisoryPending || isRefreshingUnattendSources || shellNavigationGuardService.State != ShellNavigationState.Ready)
        {
            return;
        }

        await RefreshUnattendSourcesAsync();
        if (isDisposed || shellNavigationGuardService.State != ShellNavigationState.Ready)
        {
            return;
        }

        MediaPreflightOptions options = CreatePreflightOptions();
        MediaPreflightEvaluation evaluation = MediaPreflightService.Evaluate(options);
        if (!evaluation.CanCreatePxe)
        {
            logger.Information(
                "PXE media creation was blocked by preflight validation. BlockingReasons={BlockingReasons}",
                string.Join(",", evaluation.PxeBlockingReasons));
            await ShowBlockedDialogAsync(
                "StartMedia.CreatePxe.BlockedTitle",
                evaluation.PxeBlockingReasons,
                options.AutopilotConfigurationValidationCode);
            return;
        }

        string? outputDirectoryPath = await pxeOutputDialogService.ShowAsync(
            appSettingsService.Current.MediaOutput.PxeOutputDirectoryPath,
            PxeMissingContent.Evaluate(foundryConfigurationStateService.Current));
        if (outputDirectoryPath is null || isDisposed || shellNavigationGuardService.State != ShellNavigationState.Ready)
        {
            return;
        }

        appSettingsService.Current.MediaOutput.PxeOutputDirectoryPath = outputDirectoryPath;
        appSettingsService.Save();
        await RunFinalMediaOperationAsync(FinalMediaTarget.Pxe, options, outputDirectoryPath);
    }

    private async Task<bool> ConfirmUpdateBeforeMediaAsync()
    {
        ApplicationUpdateCheckResult? update = applicationUpdateStateService.CurrentResult;
        if (update?.HasKnownUpdate != true)
        {
            return true;
        }

        ShellNavigationState previousState = shellNavigationGuardService.State;
        bool ownsNavigationGuard = false;
        MediaUpdateAdvisoryChoice choice;
        isUpdateAdvisoryPending = true;
        try
        {
            RefreshEvaluation();
            shellNavigationGuardService.SetState(ShellNavigationState.InteractionPending);
            ownsNavigationGuard = true;
            choice = await mediaUpdateAdvisoryDialogService.ShowAsync(
                FoundryApplicationInfo.Version, update.Version!, update.IsReadyToApply);
        }
        finally
        {
            if (ownsNavigationGuard && shellNavigationGuardService.State == ShellNavigationState.InteractionPending)
            {
                shellNavigationGuardService.SetState(previousState);
            }

            isUpdateAdvisoryPending = false;
            if (!isDisposed)
            {
                RefreshEvaluation();
            }
        }

        if (isDisposed || shellNavigationGuardService.State != ShellNavigationState.Ready)
        {
            return false;
        }

        switch (choice)
        {
            case MediaUpdateAdvisoryChoice.CreateAnyway:
                return true;
            case MediaUpdateAdvisoryChoice.ViewUpdate:
                appNavigationService.NavigateToUpdateSettings();
                return false;
            case MediaUpdateAdvisoryChoice.ApplyUpdate:
                await applicationUpdateRestartService.ApplyUpdateAsync();
                return false;
            default:
                return false;
        }
    }

    private static DiskIdentity GetUsbDiskIdentity(WinPeUsbDiskCandidate disk) =>
        new(disk.DiskNumber, disk.UniqueId, disk.SerialNumber, disk.FriendlyName, disk.BusType, disk.SizeBytes);

    private async Task RunFinalMediaOperationAsync(FinalMediaTarget target, MediaPreflightOptions options, string? pxeOutputDirectoryPath = null)
    {
        if (isDisposed || IsMediaOperationRunning || isUpdateAdvisoryPending || shellNavigationGuardService.State != ShellNavigationState.Ready)
        {
            return;
        }

        if (!await ConfirmUpdateBeforeMediaAsync() || isDisposed || shellNavigationGuardService.State != ShellNavigationState.Ready)
        {
            return;
        }

        MediaPreflightOptions currentOptions = CreatePreflightOptions();
        if (target is FinalMediaTarget.Usb or FinalMediaTarget.UsbUpdate &&
            (options.SelectedUsbDisk is null || currentOptions.SelectedUsbDisk is null ||
             options.SelectedUsbDisk.IsFoundryMedia != currentOptions.SelectedUsbDisk.IsFoundryMedia ||
             !GetUsbDiskIdentity(options.SelectedUsbDisk).Matches(GetUsbDiskIdentity(currentOptions.SelectedUsbDisk))))
        {
            await ShowBlockedDialogAsync("StartMedia.CreateUsb.BlockedTitle",
                localizationService.GetString("StartMedia.Operation.DiskIdentityCannotBeConfirmed"));
            return;
        }

        MediaPreflightEvaluation evaluation = MediaPreflightService.Evaluate(currentOptions);
        (bool canCreate, IReadOnlyList<MediaPreflightBlockingReason> blockingReasons) = target switch
        {
            FinalMediaTarget.Iso => (evaluation.CanCreateIso, evaluation.IsoBlockingReasons),
            FinalMediaTarget.Pxe => (evaluation.CanCreatePxe, evaluation.PxeBlockingReasons),
            _ => (evaluation.CanCreateUsb, evaluation.UsbBlockingReasons)
        };
        if (!canCreate)
        {
            await ShowBlockedDialogAsync(GetBlockedTitleKey(target), blockingReasons,
                currentOptions.AutopilotConfigurationValidationCode);
            return;
        }

        if (isDisposed || shellNavigationGuardService.State != ShellNavigationState.Ready)
        {
            return;
        }

        options = currentOptions;
        SynchronizeRuntimeTelemetrySettings();
        IsMediaOperationRunning = true;
        using var operationCancellation = new CancellationTokenSource();
        CancellationToken cancellationToken = operationCancellation.Token;
        ResetProgressLogSampling();
        progressPlan = MediaCreationProgressPlan.For(ToMediaCreationKind(target));
        lastFinalMediaPercent = -1;
        preparedBootImageSource = WinPeBootImageSource.WinRe;
        RefreshEvaluation();
        // Final media operations can format disks or overwrite ISO output, so the shell remains locked until completion.
        shellNavigationGuardService.SetState(ShellNavigationState.InteractionPending);
        Stopwatch stopwatch = Stopwatch.StartNew();
        bool success = false;
        var telemetryProgressTracker = new MediaCreationTelemetryProgressTracker();
        string operationId = Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture);
        string bootMediaTarget = GetTelemetryBootMediaTarget(target);
        string usbOperation = target switch
        {
            FinalMediaTarget.Usb => TelemetryBootMediaUsbOperations.Create,
            FinalMediaTarget.UsbUpdate => TelemetryBootMediaUsbOperations.Update,
            _ => TelemetryBootMediaUsbOperations.None
        };
        WinPeDiagnostic? failureDiagnostic = null;
        // Telemetry keeps the tracked step: a diagnostic stage can be a dynamic label, which stays in logs only.
        string? telemetryFailedStepName = null;
        DeploymentMediaProtectionMaterial? deploymentProtectionMaterial = null;
        DeploymentBuildSnapshot? snapshot = null;
        FoundryConfigurationDocument capturedConfiguration = foundryConfigurationStateService.Current;
        bool shouldTrackMedia = true;

        using IDisposable operationIdScope = LogContext.PushProperty("OperationId", operationId);
        using IDisposable workflowScope = LogContext.PushProperty("Workflow", "boot_media_creation");
        using IDisposable targetScope = LogContext.PushProperty("BootMediaTarget", bootMediaTarget);
        using IDisposable usbOperationScope = LogContext.PushProperty("UsbOperation", usbOperation);
        using IDisposable stageScope = LogContext.Push(telemetryProgressTracker);

        try
        {
            var currentAdkStatus = await adkService.RefreshStatusAsync(cancellationToken);
            if (!currentAdkStatus.CanCreateMediaFor(options.Architecture))
            {
                shouldTrackMedia = false;
                await ShowBlockedDialogAsync(GetBlockedTitleKey(target),
                    new[] { MediaPreflightBlockingReason.AdkNotReady },
                    options.AutopilotConfigurationValidationCode);
                return;
            }
            options = options with
            {
                DriverVendors = options.DriverVendors.ToArray(),
                AvailableWinPeLanguages = options.AvailableWinPeLanguages.ToArray(),
                AvailableKeyboardLayoutIds = options.AvailableKeyboardLayoutIds.ToArray()
            };
            var storagePaths = new List<string>
            {
                Constants.WorkspacesDirectoryPath,
                Path.Combine(Constants.UserRootDirectoryPath, "BuildSnapshots"),
                Path.GetTempPath(),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Temp")
            };
            storagePaths.Add(Constants.WindowsSourceCacheDirectoryPath);
            storagePaths.Add(Constants.WinReCacheDirectoryPath);
            if (options.DriverVendors.Count > 0 || options.IncludeWirelessSupport)
                storagePaths.Add(Constants.WinPeDriverCacheDirectoryPath);
            if (target == FinalMediaTarget.Iso)
                storagePaths.Add(options.IsoOutputPath);

            WinPeResult localSpaceResult = await Task.Run(
                () => WinPeLocalStorageCapacityPolicy.Validate(storagePaths, cancellationToken), cancellationToken);
            if (!localSpaceResult.IsSuccess)
            {
                shouldTrackMedia = false;
                WinPeDiagnostic diagnostic = localSpaceResult.Error!;
                logger.Warning(
                    "Boot media creation was blocked by local storage validation. FailureCode={FailureCode}, StoragePath={StoragePath}, RequiredBytes={RequiredBytes}, AvailableBytes={AvailableBytes}",
                    diagnostic.Code, diagnostic.StoragePath, diagnostic.RequiredBytes, diagnostic.AvailableBytes);
                string storageVolume = await Task.Run(
                    () => WindowsVolumeStorage.GetDisplayName(diagnostic.StoragePath!), cancellationToken);
                string message = diagnostic.Code == WinPeErrorCodes.LocalSpaceInsufficient
                    ? FormatMediaCapacityFailure("StartMedia.Operation.LocalSpaceInsufficient", diagnostic, storageVolume)
                    : string.Format(CultureInfo.CurrentCulture,
                        localizationService.GetString("StartMedia.Operation.LocalSpaceUnknown"), storageVolume);
                await ShowBlockedDialogAsync(GetBlockedTitleKey(target), message);
                return;
            }
            if (target == FinalMediaTarget.Usb && !await ConfirmUsbFormattingAsync(options.SelectedUsbDisk!))
            {
                shouldTrackMedia = false;
                return;
            }
            operationProgressService.Start(
                OperationKind.MediaCreation,
                GetOperationTitle(target, OperationOutcome.None),
                FormatStep(progressPlan.Current),
                operationCancellation.Cancel);
            shellNavigationGuardService.SetState(ShellNavigationState.OperationRunning);
            logger.Information(
                "Final media creation started. Target={Target}, Architecture={Architecture}, WinPeLanguage={WinPeLanguage}, WinPeKeyboardLayout={WinPeKeyboardLayout}, IsoOutputPath={IsoOutputPath}, PxeOutputDirectoryPath={PxeOutputDirectoryPath}, UsbDiskNumber={UsbDiskNumber}, UsbDiskName={UsbDiskName}",
                target,
                options.Architecture,
                NormalizeCultureName(options.WinPeLanguage),
                options.WinPeKeyboardLayoutId ?? "default",
                target == FinalMediaTarget.Iso ? options.IsoOutputPath : null,
                target == FinalMediaTarget.Pxe ? pxeOutputDirectoryPath : null,
                target is FinalMediaTarget.Usb or FinalMediaTarget.UsbUpdate ? options.SelectedUsbDisk?.DiskNumber : null,
                target is FinalMediaTarget.Usb or FinalMediaTarget.UsbUpdate ? options.SelectedUsbDisk?.FriendlyName : null);

            snapshot = await foundryConfigurationStateService.CaptureBuildSnapshotAsync(
                Path.Combine(Constants.UserRootDirectoryPath, "BuildSnapshots"), cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            capturedConfiguration = snapshot.Configuration;
            options = options with { CustomDriverDirectoryPath = capturedConfiguration.General.CustomDriverDirectoryPath };
            deploymentProtectionMaterial = snapshot.CreateDeploymentProtectionMaterial();

            string successMessage;
            OperationAction? successAction = null;
            if (target == FinalMediaTarget.Iso)
            {
                string isoPath = await CreateIsoMediaAsync(options, snapshot, deploymentProtectionMaterial, telemetryProgressTracker, cancellationToken);
                successMessage = BuildIsoSuccessMessage(isoPath);
                successAction = new OperationAction(
                    localizationService.GetString("StartMedia.Operation.OpenFolder"),
                    () => OpenFolder(Path.GetDirectoryName(isoPath)));
            }
            else if (target == FinalMediaTarget.Pxe)
            {
                string outputDirectoryPath = pxeOutputDirectoryPath
                    ?? throw new InvalidOperationException("PXE media creation requires an output folder.");
                await CreatePxeMediaAsync(options, outputDirectoryPath, snapshot, deploymentProtectionMaterial, telemetryProgressTracker, cancellationToken);
                successMessage = BuildPxeSuccessMessage(outputDirectoryPath);
                successAction = new OperationAction(
                    localizationService.GetString("StartMedia.Operation.OpenFolder"),
                    () => OpenFolder(outputDirectoryPath));
            }
            else
            {
                WinPeUsbProvisionResult usbResult = target == FinalMediaTarget.UsbUpdate
                    ? await UpdateUsbMediaAsync(options, snapshot, deploymentProtectionMaterial, telemetryProgressTracker, cancellationToken)
                    : await CreateUsbMediaAsync(options, snapshot, deploymentProtectionMaterial, telemetryProgressTracker, cancellationToken);
                successMessage = localizationService.FormatString(
                    "StartMedia.Operation.UsbVolumesFormat",
                    usbResult.BootDriveLetter,
                    usbResult.CacheDriveLetter);
            }

            if (preparedBootImageSource == WinPeBootImageSource.WinPe)
            {
                successMessage += Environment.NewLine + Environment.NewLine + localizationService.GetString("StartMedia.Operation.WinReFallbackWarning");
            }

            cancellationToken.ThrowIfCancellationRequested();
            ReportPhase(MediaCreationPhase.Finishing, 100);
            operationProgressService.Finish(
                OperationOutcome.Success,
                GetOperationTitle(target, OperationOutcome.Success),
                successMessage,
                successAction);
            success = true;
            logger.Information(
                "Final media creation completed. Target={Target}, DurationMs={DurationMs}",
                target,
                stopwatch.ElapsedMilliseconds);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            bool isTimeout = ex is TimeoutException or OperationCanceledException;
            failureDiagnostic = ex is CustomDriverSizeLimitException driverSizeLimit
                ? new WinPeDiagnostic(
                    WinPeErrorCodes.CustomDriversTooLarge,
                    "The custom driver directory exceeds the supported size limit.",
                    stage: telemetryProgressTracker.CurrentStepName)
                {
                    RequiredBytes = (ulong)driverSizeLimit.ActualBytes,
                    AvailableBytes = (ulong)driverSizeLimit.LimitBytes
                }
                : (ex as WinPeOperationException)?.Diagnostic ?? new WinPeDiagnostic(
                    WinPeErrorCodes.InternalError,
                    "Unexpected boot media creation failure.",
                    ex.ToString(),
                    telemetryProgressTracker.CurrentStepName,
                    failureKind: isTimeout ? WinPeFailureKinds.Network : WinPeFailureKinds.Internal,
                    failureReason: isTimeout ? WinPeFailureReasons.Timeout : WinPeFailureReasons.Unexpected,
                    exception: ex);
            telemetryFailedStepName = telemetryProgressTracker.CurrentStepName;
            string failedStepName = string.IsNullOrWhiteSpace(failureDiagnostic.Stage)
                ? telemetryProgressTracker.CurrentStepName
                : failureDiagnostic.Stage;
            telemetryProgressTracker.SetCurrentStep(failedStepName);
            string? failureMessage = failureDiagnostic.FailureReason == WinPeFailureReasons.Timeout
                ? localizationService.GetString("StartMedia.Operation.TransferTimedOut")
                : IsWinPeImageAccessBlocked(failureDiagnostic)
                ? FormatWinPeImageAccessBlockedFailure()
                : failureDiagnostic.Code switch
                {
                    WinPeErrorCodes.UsbIdentityMismatch => localizationService.GetString("StartMedia.Operation.DiskIdentityCannotBeConfirmed"),
                    WinPeErrorCodes.UsbUnsafeTarget => localizationService.GetString("StartMedia.Operation.DiskNoLongerSafe"),
                    WinPeErrorCodes.CustomDriversTooLarge => FormatMediaCapacityFailure("StartMedia.Operation.CustomDriversTooLarge", failureDiagnostic),
                    WinPeErrorCodes.UsbBootCapacityInsufficient => FormatMediaCapacityFailure("StartMedia.Operation.UsbBootCapacityInsufficient", failureDiagnostic),
                    WinPeErrorCodes.UsbBootCapacityUnknown => localizationService.GetString("StartMedia.Operation.UsbBootCapacityUnknown"),
                    WinPeErrorCodes.UsbBootFileTooLarge => FormatMediaCapacityFailure("StartMedia.Operation.UsbBootFileTooLarge", failureDiagnostic),
                    WinPeErrorCodes.UsbCacheCapacityInsufficient => localizationService.GetString("StartMedia.Operation.UsbCacheCapacityInsufficient"),
                    WinPeErrorCodes.ExternalMediaContentFailed => localizationService.GetString("StartMedia.Operation.ExternalMediaContentFailed"),
                    WinPeErrorCodes.PxeOutputCapacityInsufficient => FormatMediaCapacityFailure("StartMedia.Operation.PxeOutputCapacityInsufficient", failureDiagnostic),
                    WinPeErrorCodes.PxeOutputFailed => localizationService.GetString("StartMedia.Operation.PxeOutputFailed"),
                    WinPeErrorCodes.IsoCreateFailed when IsIsoOutputLocked(failureDiagnostic) => string.Format(
                        CultureInfo.CurrentCulture,
                        localizationService.GetString("StartMedia.Operation.IsoOutputLocked"),
                        options.IsoOutputPath),
                    _ => null
                };
            operationProgressService.Finish(
                OperationOutcome.Error,
                GetOperationTitle(target, OperationOutcome.Error),
                BuildFailureMessage(failureMessage, ex.Message),
                new OperationAction(localizationService.GetString("StartMedia.Operation.OpenLogFolder"), OpenLogFolder));
            if (failureDiagnostic.Code is WinPeErrorCodes.CustomDriversTooLarge
                or WinPeErrorCodes.UsbBootCapacityInsufficient
                or WinPeErrorCodes.UsbBootCapacityUnknown
                or WinPeErrorCodes.UsbBootFileTooLarge
                or WinPeErrorCodes.UsbCacheCapacityInsufficient
                or WinPeErrorCodes.PxeOutputCapacityInsufficient)
            {
                logger.Warning(
                    "Final boot media operation failed validation. FailedStepName={FailedStepName}, DurationMs={DurationMs}, FailureKind={FailureKind}, FailureReason={FailureReason}, FailureCode={FailureCode}, RequiredBytes={RequiredBytes}, AvailableBytes={AvailableBytes}",
                    failedStepName,
                    stopwatch.ElapsedMilliseconds,
                    failureDiagnostic.FailureKind,
                    failureDiagnostic.FailureReason,
                    failureDiagnostic.Code,
                    failureDiagnostic.RequiredBytes,
                    failureDiagnostic.AvailableBytes);
            }
            else
            {
                logger.Error(
                    ex,
                    "Final boot media operation failed. FailedStepName={FailedStepName}, DurationMs={DurationMs}, FailureKind={FailureKind}, FailureReason={FailureReason}, FailureCode={FailureCode}, FailureDetail={FailureDetail}, ToolName={ToolName}, ExitCode={ExitCode}, RetryCount={RetryCount}, FailureSummary={FailureSummary}",
                    failedStepName,
                    stopwatch.ElapsedMilliseconds,
                    failureDiagnostic.FailureKind,
                    failureDiagnostic.FailureReason,
                    failureDiagnostic.Code,
                    failureDiagnostic.FailureDetail ?? string.Empty,
                    failureDiagnostic.ToolName,
                    failureDiagnostic.ExitCode,
                    failureDiagnostic.RetryCount,
                    failureDiagnostic.Message);
            }
        }
        catch (OperationCanceledException ex) when (cancellationToken.IsCancellationRequested)
        {
            operationProgressService.Finish(
                OperationOutcome.Cancelled,
                GetOperationTitle(target, OperationOutcome.Cancelled),
                BuildCancelledMessage(target));
            telemetryFailedStepName = telemetryProgressTracker.CurrentStepName;
            failureDiagnostic = new WinPeDiagnostic(
                WinPeErrorCodes.OperationCancelled,
                "Boot media creation was cancelled.",
                ex.ToString(),
                telemetryProgressTracker.CurrentStepName,
                failureKind: WinPeFailureKinds.Cancellation,
                failureReason: WinPeFailureReasons.Cancelled,
                exception: ex);
            logger.Information(
                "Final boot media operation cancelled. FailedStepName={FailedStepName}, DurationMs={DurationMs}, FailureKind={FailureKind}, FailureReason={FailureReason}, FailureCode={FailureCode}, RetryCount={RetryCount}",
                telemetryProgressTracker.CurrentStepName,
                stopwatch.ElapsedMilliseconds,
                failureDiagnostic.FailureKind,
                failureDiagnostic.FailureReason,
                failureDiagnostic.Code,
                failureDiagnostic.RetryCount);
        }
        finally
        {
            deploymentProtectionMaterial?.Dispose();
            try
            {
                snapshot?.Dispose();
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                logger.Warning(exception, "Private media build sources could not be fully removed.");
            }
            try
            {
                if (shouldTrackMedia)
                {
                    await TrackMediaCreatedAsync(
                        target,
                        options with { BootImageSource = preparedBootImageSource },
                        capturedConfiguration,
                        success,
                        success ? null : telemetryFailedStepName ?? telemetryProgressTracker.CurrentStepName,
                        stopwatch.Elapsed,
                        operationId,
                        failureDiagnostic,
                        CancellationToken.None);
                }
            }
            finally
            {
                if (target is FinalMediaTarget.Usb or FinalMediaTarget.UsbUpdate && !isDisposed)
                {
                    await RefreshUsbCandidatesAfterMediaOperationAsync();
                }

                shellNavigationGuardService.SetState(adkService.CurrentStatus.CanCreateMedia
                    ? ShellNavigationState.Ready
                    : ShellNavigationState.AdkBlocked);
                operationProgressService.Reset();
                progressPlan = null;
                IsMediaOperationRunning = false;
                RefreshEvaluation();
            }
        }
    }

    private static MediaCreationKind ToMediaCreationKind(FinalMediaTarget target) => target switch
    {
        FinalMediaTarget.Iso => MediaCreationKind.Iso,
        FinalMediaTarget.Pxe => MediaCreationKind.Pxe,
        FinalMediaTarget.UsbUpdate => MediaCreationKind.UsbUpdate,
        _ => MediaCreationKind.UsbCreate
    };

    private static string GetTelemetryBootMediaTarget(FinalMediaTarget target) => target switch
    {
        FinalMediaTarget.Iso => TelemetryBootMediaTargets.Iso,
        FinalMediaTarget.Pxe => TelemetryBootMediaTargets.Pxe,
        _ => TelemetryBootMediaTargets.Usb
    };

    private static string GetBlockedTitleKey(FinalMediaTarget target) => target switch
    {
        FinalMediaTarget.Iso => "StartMedia.CreateIso.BlockedTitle",
        FinalMediaTarget.Pxe => "StartMedia.CreatePxe.BlockedTitle",
        _ => "StartMedia.CreateUsb.BlockedTitle"
    };

    private string GetOperationTitle(FinalMediaTarget target, OperationOutcome outcome)
    {
        string targetName = target switch
        {
            FinalMediaTarget.Iso => "Iso",
            FinalMediaTarget.Pxe => "Pxe",
            FinalMediaTarget.UsbUpdate => "UsbUpdate",
            _ => "Usb"
        };
        string state = outcome switch
        {
            OperationOutcome.Success => "Succeeded",
            OperationOutcome.Error => "Failed",
            OperationOutcome.Cancelled => "Cancelled",
            _ => "Running"
        };
        return localizationService.GetString($"StartMedia.Operation.Title.{targetName}{state}");
    }

    private string BuildIsoSuccessMessage(string isoPath)
    {
        string location = localizationService.GetString("StartMedia.Operation.IsoSavedLead") + Environment.NewLine + isoPath;
        if (!File.Exists(isoPath))
        {
            return location;
        }

        string size = localizationService.FormatString(
            "StartMedia.Operation.IsoSizeFormat",
            ByteSizeFormatter.Format(localizationService, (ulong)new FileInfo(isoPath).Length));
        return location + Environment.NewLine + Environment.NewLine + size;
    }

    private string BuildPxeSuccessMessage(string outputDirectoryPath)
    {
        string location = localizationService.GetString("StartMedia.Operation.PxeSavedLead") + Environment.NewLine + outputDirectoryPath;
        long totalBytes = WinPePxeMediaService.OutputFileNames
            .Select(name => new FileInfo(Path.Combine(outputDirectoryPath, name)))
            .Where(file => file.Exists)
            .Sum(file => file.Length);
        if (totalBytes == 0)
        {
            return location;
        }

        string size = localizationService.FormatString(
            "StartMedia.Operation.IsoSizeFormat",
            ByteSizeFormatter.Format(localizationService, (ulong)totalBytes));
        return location + Environment.NewLine + Environment.NewLine + size;
    }

    /// <summary>
    /// Builds the failure message: the localized reason, the step that failed, technical details only when no
    /// localized reason exists, and where to find the log.
    /// </summary>
    private string BuildFailureMessage(string? localizedReason, string technicalDetails)
    {
        List<string> paragraphs = [localizedReason ?? localizationService.GetString("StartMedia.Operation.UnexpectedFailure")];
        if (progressPlan is not null)
        {
            MediaCreationProgress failedAt = progressPlan.Current;
            paragraphs.Add(localizationService.FormatString(
                "StartMedia.Operation.FailedStepFormat",
                failedAt.StepNumber,
                failedAt.StepCount,
                GetPhaseLabel(failedAt.Phase)));
        }

        if (localizedReason is null && !string.IsNullOrWhiteSpace(technicalDetails))
        {
            paragraphs.Add(localizationService.FormatString("StartMedia.Operation.TechnicalDetailsFormat", technicalDetails.Trim()));
        }

        paragraphs.Add(localizationService.GetString("StartMedia.Operation.SeeLog"));
        return string.Join(Environment.NewLine + Environment.NewLine, paragraphs);
    }

    private string BuildCancelledMessage(FinalMediaTarget target)
    {
        string message = localizationService.GetString("StartMedia.Operation.CancelledMessage");
        // USB creation partitions the disk from 20%; an update writes the cache partition from 22%.
        bool usbWasModified = target switch
        {
            FinalMediaTarget.Usb => lastFinalMediaPercent >= 20,
            FinalMediaTarget.UsbUpdate => lastFinalMediaPercent >= 22,
            _ => false
        };
        return usbWasModified
            ? message + Environment.NewLine + Environment.NewLine + localizationService.GetString("StartMedia.Operation.UsbMayBeIncomplete")
            : message;
    }

    private void OpenFolder(string? folderPath)
    {
        if (string.IsNullOrWhiteSpace(folderPath))
        {
            return;
        }

        _ = OpenFolderSafelyAsync(folderPath);
    }

    private void OpenLogFolder()
    {
        OpenFolder(Path.GetDirectoryName(Constants.LogFilePath));
    }

    private async Task OpenFolderSafelyAsync(string folderPath)
    {
        try
        {
            await externalProcessLauncher.OpenFolderAsync(folderPath);
        }
        catch (Exception ex)
        {
            logger.Warning(ex, "Failed to open a folder from the media creation dialog.");
        }
    }

    private async Task<bool> ConfirmUsbFormattingAsync(WinPeUsbDiskCandidate selectedDisk)
    {
        WinPeUsbDiskCandidate? currentDisk = await ReadCurrentUsbDiskAsync(selectedDisk);
        if (currentDisk is null)
        {
            return false;
        }

        bool confirmed = await dialogService.ConfirmAsync(new ConfirmationDialogRequest(
            localizationService.GetString("StartMedia.CreateUsb.ConfirmTitle"),
            BuildUsbFormattingMessage(currentDisk),
            localizationService.GetString("StartMedia.CreateUsb.ConfirmPrimary"),
            localizationService.GetString("Common.Cancel"),
            PreferCancel: true));
        if (!confirmed)
        {
            logger.Information(
                "Final USB media creation cancelled before formatting. DiskNumber={DiskNumber}, DiskName={DiskName}",
                selectedDisk.DiskNumber,
                selectedDisk.FriendlyName);
        }
        return confirmed;
    }

    /// <summary>
    /// Reads the USB inventory again just before the erase confirmation, because the list shown on the page can
    /// be old, and returns the selected disk as it is now. When the disk is gone or no longer matches its
    /// selected identity, or the read fails, it tells the user and returns null so that nothing is confirmed.
    /// </summary>
    private async Task<WinPeUsbDiskCandidate?> ReadCurrentUsbDiskAsync(WinPeUsbDiskCandidate selectedDisk)
    {
        WinPeResult<IReadOnlyList<WinPeUsbDiskCandidate>> result = await usbMediaService.GetUsbCandidatesAsync(
            ResolveWinPeToolsOrThrow(),
            Constants.UsbQueryTempDirectoryPath,
            CancellationToken.None);
        if (!result.IsSuccess || result.Value is null)
        {
            logger.Warning("USB target could not be read before the erase confirmation. ErrorCode={ErrorCode}", result.Error?.Code);
            await ShowBlockedDialogAsync(
                "StartMedia.CreateUsb.BlockedTitle",
                localizationService.GetString("StartMedia.Usb.QueryFailed"));
            return null;
        }

        WinPeUsbDiskCandidate? currentDisk = WinPeUsbDiskCandidateSelector.FindConfirmed(result.Value, selectedDisk);
        if (currentDisk is null)
        {
            logger.Warning(
                "USB target identity could not be confirmed before the erase confirmation. DiskNumber={DiskNumber}",
                selectedDisk.DiskNumber);
            await ShowBlockedDialogAsync(
                "StartMedia.CreateUsb.BlockedTitle",
                localizationService.GetString("StartMedia.Operation.DiskIdentityCannotBeConfirmed"));
        }

        return currentDisk;
    }

    /// <summary>
    /// Describes the disk about to be erased and what was just read on it, so the user can notice a wrong disk
    /// before confirming. A disk is only described as having no partition when the read succeeded and found none.
    /// </summary>
    private string BuildUsbFormattingMessage(WinPeUsbDiskCandidate selectedDisk)
    {
        List<string> lines =
        [
            string.Format(
                localizationService.GetString("StartMedia.CreateUsb.ConfirmMessage"),
                selectedDisk.DiskNumber,
                selectedDisk.FriendlyName,
                ByteSizeFormatter.Format(localizationService, selectedDisk.SizeBytes)),
            string.Empty
        ];

        switch (selectedDisk.VolumeSummary)
        {
            case WinPeUsbVolumeSummary.ReadableVolumes:
                lines.Add(localizationService.GetString("StartMedia.CreateUsb.ConfirmVolumesHeader"));
                lines.AddRange(selectedDisk.Volumes.Select(FormatUsbVolume));
                break;
            case WinPeUsbVolumeSummary.ReadableAndUnreadableVolumes:
                lines.Add(localizationService.GetString("StartMedia.CreateUsb.ConfirmVolumesHeader"));
                lines.AddRange(selectedDisk.Volumes.Select(FormatUsbVolume));
                lines.Add(localizationService.GetString("StartMedia.CreateUsb.ConfirmUnreadablePartitions"));
                break;
            case WinPeUsbVolumeSummary.NoPartition:
                lines.Add(localizationService.GetString("StartMedia.CreateUsb.ConfirmNoPartition"));
                break;
            default:
                lines.Add(localizationService.GetString("StartMedia.CreateUsb.ConfirmUnreadableVolumes"));
                break;
        }

        lines.Add(string.Empty);
        lines.Add(localizationService.GetString("StartMedia.CreateUsb.ConfirmWarning"));
        return string.Join(Environment.NewLine, lines);
    }

    private string FormatUsbVolume(WinPeUsbVolume volume)
    {
        string label = string.IsNullOrWhiteSpace(volume.Label)
            ? localizationService.GetString("StartMedia.CreateUsb.ConfirmVolumeNoName")
            : volume.Label;

        // A volume without a drive letter leaves the first placeholder empty.
        return string.Format(
            localizationService.GetString("StartMedia.CreateUsb.ConfirmVolumeLine"),
            volume.DriveLetter,
            label,
            volume.FileSystem,
            ByteSizeFormatter.Format(localizationService, volume.UsedBytes),
            ByteSizeFormatter.Format(localizationService, volume.SizeBytes)).Trim();
    }

    private async Task<string> CreateIsoMediaAsync(
        MediaPreflightOptions options,
        DeploymentBuildSnapshot snapshot,
        DeploymentMediaProtectionMaterial deploymentProtectionMaterial,
        MediaCreationTelemetryProgressTracker telemetryProgressTracker,
        CancellationToken cancellationToken)
    {
        PreparedMediaWorkspace? workspace = null;

        try
        {
            workspace = await PrepareMediaWorkspaceAsync(
                options,
                snapshot,
                includeRuntimePayloadInImage: true,
                deploymentProtectionMaterial,
                telemetryProgressTracker: telemetryProgressTracker,
                cancellationToken);

            telemetryProgressTracker.SetCurrentStep(MediaCreationStepNames.CreateIsoMedia);
            cancellationToken.ThrowIfCancellationRequested();
            logger.Debug(
                "Creating ISO media. OutputIsoPath={OutputIsoPath}, MediaDirectoryPath={MediaDirectoryPath}, UseBootEx={UseBootEx}",
                options.IsoOutputPath,
                workspace.PreparedWorkspace.Artifact.MediaDirectoryPath,
                workspace.PreparedWorkspace.UseBootEx);

            // ADK scripts can still be writing after a process cancellation request.
            // Finish the ISO stage before releasing its workspace during cleanup.
            WinPeResult result = await isoMediaService.CreateAsync(
                new WinPeIsoMediaOptions
                {
                    PreparedWorkspace = workspace.PreparedWorkspace,
                    CustomImages = workspace.CustomImages,
                    PostInstallation = workspace.PostInstallation,
                    CustomDrivers = workspace.CustomDrivers,
                    DeployConfigurationJson = workspace.DeployConfigurationJson,
                    OutputIsoPath = options.IsoOutputPath,
                    IsoTempDirectoryPath = Path.Combine(workspace.Lease.OperationDirectoryPath, "Scratch", "Iso"),
                    Progress = telemetryProgressTracker.CreateFinalMediaProgress(
                        new Progress<WinPeMediaProgress>(ReportFinalMediaProgress))
                },
                cancellationToken);

            EnsureSuccess(result);
            ReportPhase(MediaCreationPhase.Finishing, 0, localizationService.GetString("StartMedia.Operation.RemovingTemporaryFiles"));
            cancellationToken.ThrowIfCancellationRequested();
            logger.Debug("ISO media service completed. OutputIsoPath={OutputIsoPath}", options.IsoOutputPath);
            return options.IsoOutputPath;
        }
        finally
        {
            ReleasePreparedWorkspace(workspace?.CustomImages, workspace?.PostInstallation, workspace?.CustomDrivers, workspace?.Lease);
        }
    }

    /// <summary>
    /// Builds the boot image exactly like the ISO, runtimes included, then writes the network boot files. Imported content
    /// is still prepared so the Deploy configuration in boot.wim carries the same media bindings as the ISO.
    /// </summary>
    private async Task CreatePxeMediaAsync(
        MediaPreflightOptions options,
        string outputDirectoryPath,
        DeploymentBuildSnapshot snapshot,
        DeploymentMediaProtectionMaterial deploymentProtectionMaterial,
        MediaCreationTelemetryProgressTracker telemetryProgressTracker,
        CancellationToken cancellationToken)
    {
        PreparedMediaWorkspace? workspace = null;

        try
        {
            workspace = await PrepareMediaWorkspaceAsync(
                options,
                snapshot,
                includeRuntimePayloadInImage: true,
                deploymentProtectionMaterial,
                telemetryProgressTracker: telemetryProgressTracker,
                cancellationToken);

            telemetryProgressTracker.SetCurrentStep(MediaCreationStepNames.CreatePxeMedia);
            cancellationToken.ThrowIfCancellationRequested();
            logger.Debug(
                "Creating PXE boot files. OutputDirectoryPath={OutputDirectoryPath}, MediaDirectoryPath={MediaDirectoryPath}, UseBootEx={UseBootEx}",
                outputDirectoryPath,
                workspace.PreparedWorkspace.Artifact.MediaDirectoryPath,
                workspace.PreparedWorkspace.UseBootEx);

            WinPeResult result = await pxeMediaService.CreateAsync(
                new WinPePxeMediaOptions
                {
                    PreparedWorkspace = workspace.PreparedWorkspace,
                    OutputDirectoryPath = outputDirectoryPath,
                    Progress = telemetryProgressTracker.CreateFinalMediaProgress(
                        new Progress<WinPeMediaProgress>(ReportFinalMediaProgress))
                },
                cancellationToken);

            EnsureSuccess(result);
            ReportPhase(MediaCreationPhase.Finishing, 0, localizationService.GetString("StartMedia.Operation.RemovingTemporaryFiles"));
            logger.Debug("PXE boot files written. OutputDirectoryPath={OutputDirectoryPath}", outputDirectoryPath);
        }
        finally
        {
            ReleasePreparedWorkspace(workspace?.CustomImages, workspace?.PostInstallation, workspace?.CustomDrivers, workspace?.Lease);
        }
    }

    /// <summary>
    /// Re-reads disk identity and Foundry media state after a USB operation, because formatting or
    /// re-enumeration can change them and the next operation must not reuse the stale selection.
    /// </summary>
    private async Task RefreshUsbCandidatesAfterMediaOperationAsync()
    {
        try
        {
            await RefreshUsbCandidatesAsync();
        }
        catch (Exception ex)
        {
            // The media outcome is already final; a failed refresh must not replace it or keep the shell locked.
            logger.Warning(ex, "USB target refresh after the media operation failed.");
        }
    }

    /// <summary>
    /// Confirms the selected disk still has its captured identity before WinPE preparation starts,
    /// so a stale selection fails immediately instead of after the long build.
    /// </summary>
    private async Task ValidateUsbTargetBeforePreparationAsync(
        WinPeUsbDiskCandidate selectedDisk,
        CancellationToken cancellationToken)
    {
        ReportPhase(MediaCreationPhase.Preparing, 0, localizationService.GetString("StartMedia.Operation.ValidatingUsbTarget"));
        WinPeResult result = await usbMediaService.ValidateUsbTargetAsync(
            CreateUsbTargetOptions(selectedDisk),
            ResolveWinPeToolsOrThrow(),
            Constants.UsbQueryTempDirectoryPath,
            cancellationToken);
        EnsureSuccess(result);
    }

    private static UsbOutputOptions CreateUsbTargetOptions(WinPeUsbDiskCandidate selectedDisk) => new()
    {
        TargetDiskNumber = selectedDisk.DiskNumber,
        ExpectedDiskFriendlyName = selectedDisk.FriendlyName,
        ExpectedDiskSerialNumber = selectedDisk.SerialNumber,
        ExpectedDiskUniqueId = selectedDisk.UniqueId,
        ExpectedDiskBusType = selectedDisk.BusType,
        ExpectedDiskSizeBytes = selectedDisk.SizeBytes
    };

    private async Task<WinPeUsbProvisionResult> CreateUsbMediaAsync(
        MediaPreflightOptions options,
        DeploymentBuildSnapshot snapshot,
        DeploymentMediaProtectionMaterial deploymentProtectionMaterial,
        MediaCreationTelemetryProgressTracker telemetryProgressTracker,
        CancellationToken cancellationToken)
    {
        telemetryProgressTracker.SetCurrentStep(MediaCreationStepNames.ValidateUsbTarget);
        if (options.SelectedUsbDisk is null)
        {
            throw new InvalidOperationException(localizationService.GetString("StartMedia.BlockingReason.NoUsbTarget"));
        }

        WinPeUsbDiskCandidate selectedDisk = options.SelectedUsbDisk;
        PreparedMediaWorkspace? workspace = null;

        try
        {
            await ValidateUsbTargetBeforePreparationAsync(selectedDisk, cancellationToken);
            workspace = await PrepareMediaWorkspaceAsync(
                options,
                snapshot,
                includeRuntimePayloadInImage: false,
                deploymentProtectionMaterial,
                telemetryProgressTracker: telemetryProgressTracker,
                cancellationToken);

            telemetryProgressTracker.SetCurrentStep(MediaCreationStepNames.CreateUsbMedia);
            logger.Debug(
                "Creating USB media. DiskNumber={DiskNumber}, DiskName={DiskName}, PartitionStyle={PartitionStyle}, FormatMode={FormatMode}, MediaDirectoryPath={MediaDirectoryPath}, UseBootEx={UseBootEx}",
                selectedDisk.DiskNumber,
                selectedDisk.FriendlyName,
                options.UsbPartitionStyle,
                options.UsbFormatMode,
                workspace.PreparedWorkspace.Artifact.MediaDirectoryPath,
                workspace.PreparedWorkspace.UseBootEx);

            WinPeResult<WinPeUsbProvisionResult> result = await usbMediaService.ProvisionAndPopulateAsync(
                CreateUsbTargetOptions(selectedDisk) with
                {
                    PartitionStyle = options.UsbPartitionStyle,
                    FormatMode = options.UsbFormatMode,
                    RuntimePayloadProvisioning = workspace.RuntimePayloadProvisioning,
                    CustomImages = workspace.CustomImages,
                    PostInstallation = workspace.PostInstallation,
                    CustomDrivers = workspace.CustomDrivers,
                    DeployConfigurationJson = workspace.DeployConfigurationJson,
                    DownloadProgress = telemetryProgressTracker.CreateDownloadProgress(
                        new Progress<WinPeDownloadProgress>(ReportDownloadProgress)),
                    Progress = telemetryProgressTracker.CreateFinalMediaProgress(
                        new Progress<WinPeMediaProgress>(ReportFinalMediaProgress))
                },
                workspace.PreparedWorkspace.Artifact,
                workspace.Tools,
                workspace.PreparedWorkspace.UseBootEx,
                cancellationToken);

            EnsureSuccess(result);
            ReportPhase(MediaCreationPhase.Finishing, 0, localizationService.GetString("StartMedia.Operation.RemovingTemporaryFiles"));
            logger.Debug(
                "USB media service completed. DiskNumber={DiskNumber}, BootVolume={BootVolume}, CacheVolume={CacheVolume}",
                selectedDisk.DiskNumber,
                result.Value?.BootDriveLetter,
                result.Value?.CacheDriveLetter);
            return result.Value!;
        }
        finally
        {
            ReleasePreparedWorkspace(workspace?.CustomImages, workspace?.PostInstallation, workspace?.CustomDrivers, workspace?.Lease);
        }
    }

    private async Task<WinPeUsbProvisionResult> UpdateUsbMediaAsync(
        MediaPreflightOptions options,
        DeploymentBuildSnapshot snapshot,
        DeploymentMediaProtectionMaterial deploymentProtectionMaterial,
        MediaCreationTelemetryProgressTracker telemetryProgressTracker,
        CancellationToken cancellationToken)
    {
        telemetryProgressTracker.SetCurrentStep(MediaCreationStepNames.ValidateUsbTarget);
        if (options.SelectedUsbDisk is null)
        {
            throw new InvalidOperationException(localizationService.GetString("StartMedia.BlockingReason.NoUsbTarget"));
        }

        WinPeUsbDiskCandidate selectedDisk = options.SelectedUsbDisk;
        PreparedMediaWorkspace? workspace = null;

        try
        {
            await ValidateUsbTargetBeforePreparationAsync(selectedDisk, cancellationToken);
            workspace = await PrepareMediaWorkspaceAsync(
                options,
                snapshot,
                includeRuntimePayloadInImage: false,
                deploymentProtectionMaterial,
                telemetryProgressTracker: telemetryProgressTracker,
                cancellationToken);

            telemetryProgressTracker.SetCurrentStep(MediaCreationStepNames.UpdateUsbMedia);
            logger.Debug(
                "Updating USB boot partition. DiskNumber={DiskNumber}, DiskName={DiskName}, FormatMode={FormatMode}, MediaDirectoryPath={MediaDirectoryPath}, UseBootEx={UseBootEx}",
                selectedDisk.DiskNumber,
                selectedDisk.FriendlyName,
                options.UsbFormatMode,
                workspace.PreparedWorkspace.Artifact.MediaDirectoryPath,
                workspace.PreparedWorkspace.UseBootEx);

            WinPeResult<WinPeUsbProvisionResult> result = await usbMediaService.UpdateBootPartitionAsync(
                CreateUsbTargetOptions(selectedDisk) with
                {
                    FormatMode = options.UsbFormatMode,
                    RuntimePayloadProvisioning = workspace.RuntimePayloadProvisioning,
                    CustomImages = workspace.CustomImages,
                    PostInstallation = workspace.PostInstallation,
                    CustomDrivers = workspace.CustomDrivers,
                    DeployConfigurationJson = workspace.DeployConfigurationJson,
                    DownloadProgress = telemetryProgressTracker.CreateDownloadProgress(
                        new Progress<WinPeDownloadProgress>(ReportDownloadProgress)),
                    Progress = telemetryProgressTracker.CreateFinalMediaProgress(
                        new Progress<WinPeMediaProgress>(ReportFinalMediaProgress))
                },
                workspace.PreparedWorkspace.Artifact,
                workspace.Tools,
                workspace.PreparedWorkspace.UseBootEx,
                cancellationToken);

            EnsureSuccess(result);
            ReportPhase(MediaCreationPhase.Finishing, 0, localizationService.GetString("StartMedia.Operation.RemovingTemporaryFiles"));
            logger.Debug(
                "USB boot partition update service completed. DiskNumber={DiskNumber}, BootVolume={BootVolume}, CacheVolume={CacheVolume}",
                selectedDisk.DiskNumber,
                result.Value?.BootDriveLetter,
                result.Value?.CacheDriveLetter);
            return result.Value!;
        }
        finally
        {
            ReleasePreparedWorkspace(workspace?.CustomImages, workspace?.PostInstallation, workspace?.CustomDrivers, workspace?.Lease);
        }
    }

    private async Task<PreparedMediaWorkspace> PrepareMediaWorkspaceAsync(
        MediaPreflightOptions options,
        DeploymentBuildSnapshot snapshot,
        bool includeRuntimePayloadInImage,
        DeploymentMediaProtectionMaterial deploymentProtectionMaterial,
        MediaCreationTelemetryProgressTracker telemetryProgressTracker,
        CancellationToken cancellationToken)
    {
        WinPeWorkspaceLease? operationLease = null;
        WinPeCustomImageMediaLease? customImages = null;
        WinPeContentPackageMediaLease? postInstallation = null;
        WinPeContentPackageMediaLease? customDrivers = null;

        try
        {
            telemetryProgressTracker.SetCurrentStep(MediaCreationStepNames.ResolveWinPeTools);
            cancellationToken.ThrowIfCancellationRequested();
            WinPeToolPaths tools = ResolveWinPeToolsOrThrow();
            if (snapshot.Configuration.CustomImages.IsEnabled)
            {
                ReportPhase(MediaCreationPhase.Preparing, 10, localizationService.GetString("CustomImages.StageVerifying"));
                customImages = await new WinPeCustomImageMediaService().PrepareAsync(
                    customImageLibrary, snapshot.Configuration.CustomImages, cancellationToken);
            }
            logger.Debug(
                "Resolved WinPE tools. KitsRootPath={KitsRootPath}, DismPath={DismPath}, MakeWinPeMediaPath={MakeWinPeMediaPath}",
                tools.KitsRootPath,
                tools.DismPath,
                tools.MakeWinPeMediaPath);

            telemetryProgressTracker.SetCurrentStep(MediaCreationStepNames.PrepareRuntimePayloads);
            WinPeRuntimePayloadProvisioningOptions runtimePayloadProvisioning = CreateRuntimePayloadProvisioningOptions(
                options.Architecture,
                Constants.WorkspacesDirectoryPath,
                Constants.WorkspacesDirectoryPath,
                Constants.WorkspacesDirectoryPath);
            runtimePayloadProvisioning = AddReleaseRuntimeProvisioning(runtimePayloadProvisioning);
            runtimePayloadProvisioning = runtimePayloadProvisioning.WithDomainJoinRuntime(snapshot.Configuration.DomainJoin.IsEnabled);
            TelemetrySettings connectTelemetrySettings = snapshot.Configuration.Telemetry with { RuntimePayloadSource = ResolveRuntimePayloadSource(runtimePayloadProvisioning.Connect) };
            TelemetrySettings deployTelemetrySettings = snapshot.Configuration.Telemetry with { RuntimePayloadSource = ResolveRuntimePayloadSource(runtimePayloadProvisioning.Deploy) };

            logger.Debug(
                "Final media workspace preparation started. Architecture={Architecture}, WinPeLanguage={WinPeLanguage}, SignatureMode={SignatureMode}, IncludeWirelessSupport={IncludeWirelessSupport}, IncludeRuntimePayloadInImage={IncludeRuntimePayloadInImage}, DriverVendorCount={DriverVendorCount}, HasCustomDriverDirectory={HasCustomDriverDirectory}, IsAutopilotEnabled={IsAutopilotEnabled}, IsConnectRuntimeProvisioningEnabled={IsConnectRuntimeProvisioningEnabled}, ConnectRuntimeSource={ConnectRuntimeSource}, IsDeployRuntimeProvisioningEnabled={IsDeployRuntimeProvisioningEnabled}, DeployRuntimeSource={DeployRuntimeSource}",
                options.Architecture,
                NormalizeCultureName(options.WinPeLanguage),
                options.SignatureMode,
                options.IncludeWirelessSupport,
                includeRuntimePayloadInImage,
                options.DriverVendors.Count,
                !string.IsNullOrWhiteSpace(options.CustomDriverDirectoryPath),
                options.IsAutopilotEnabled,
                runtimePayloadProvisioning.Connect.IsEnabled,
                ResolveProvisioningSource(runtimePayloadProvisioning.Connect),
                runtimePayloadProvisioning.Deploy.IsEnabled,
                ResolveProvisioningSource(runtimePayloadProvisioning.Deploy));

            telemetryProgressTracker.SetCurrentStep(MediaCreationStepNames.CleanStaleWorkspaces);
            // Recovery can wait on a DISM discard, so the user sees a status first and the DISM percentage while a
            // discard runs. It runs before stale cleanup so a workspace whose earlier cleanup is now verified is
            // deleted in this same start.
            ReportPhase(MediaCreationPhase.Preparing, 15, localizationService.GetString("StartMedia.Operation.CleaningWorkspace"));
            await workspaceCleanupService.RecoverUnresolvedMountCleanupsAsync(
                Constants.WorkspacesDirectoryPath,
                tools.DismPath,
                cancellationToken,
                new Progress<WinPeDismProgress>(ReportMountCleanupRecoveryProgress));
            ReportPhase(MediaCreationPhase.Preparing, 20);
            CleanupStaleWinPeWorkspaces();
            EnsureSuccess(workspaceCleanupService.EnsureServicingCanStart(Constants.WorkspacesDirectoryPath));
            operationLease = WinPeWorkspaceLease.Create(Constants.WorkspacesDirectoryPath);

            telemetryProgressTracker.SetCurrentStep(MediaCreationStepNames.BuildWinPeWorkspace);
            cancellationToken.ThrowIfCancellationRequested();
            ReportPhase(MediaCreationPhase.Preparing, 30, localizationService.GetString("StartMedia.Operation.BuildingWorkspace"));
            // Await Copype completion before observing cancellation so cleanup cannot race the process.
            WinPeResult<WinPeBuildArtifact> buildResult = await buildService.BuildAsync(
                new WinPeBuildOptions
                {
                    OutputDirectoryPath = operationLease.OperationDirectoryPath,
                    WorkingDirectoryPath = operationLease.WinPeDirectoryPath,
                    CleanExistingWorkingDirectory = false,
                    AdkRootPath = tools.KitsRootPath,
                    Architecture = options.Architecture,
                    SignatureMode = options.SignatureMode
                },
                CancellationToken.None);
            EnsureSuccess(buildResult);

            WinPeBuildArtifact artifact = buildResult.Value!;
            cancellationToken.ThrowIfCancellationRequested();
            logger.Debug(
                "WinPE workspace created. WorkingDirectoryPath={WorkingDirectoryPath}, MediaDirectoryPath={MediaDirectoryPath}, MountDirectoryPath={MountDirectoryPath}, BootWimPath={BootWimPath}",
                artifact.WorkingDirectoryPath,
                artifact.MediaDirectoryPath,
                artifact.MountDirectoryPath,
                artifact.BootWimPath);

            WinPeRuntimePayloadProvisioningOptions artifactRuntimePayloadProvisioning = runtimePayloadProvisioning with
            {
                WorkingDirectoryPath = Path.Combine(operationLease.OperationDirectoryPath, "Runtime"),
                MountedImagePath = artifact.MountDirectoryPath,
                UsbCacheRootPath = string.Empty
            };
            telemetryProgressTracker.SetCurrentStep(MediaCreationStepNames.PrepareRuntimePayloads);
            ReportPhase(MediaCreationPhase.Preparing, 60, localizationService.GetString("StartMedia.Operation.PreparingComponents"));
            WinPeResult<WinPeRuntimePayloadProvisioningOptions> runtimePreparation =
                await runtimePayloadProvisioningService.PrepareAsync(artifactRuntimePayloadProvisioning, cancellationToken);
            EnsureSuccess(runtimePreparation);
            artifactRuntimePayloadProvisioning = runtimePreparation.Value!;

            ReportPhase(MediaCreationPhase.Preparing, 80, localizationService.GetString("StartMedia.Operation.PreparingPostInstallation"));
            postInstallation = await new WinPePreOobeMediaService().PrepareAsync(postInstallPackages,
                snapshot.Configuration.PreOobe, cancellationToken);
            if (snapshot.Configuration.CustomDrivers.IsEnabled)
            {
                ReportPhase(MediaCreationPhase.Preparing, 90, localizationService.GetString("CustomDrivers.StageVerifying"));
                // Preparation rejects an enabled section without included sets, matching the readiness check that blocks media creation.
                customDrivers = await WinPeCustomDriverSetMediaService.PrepareAsync(customDriverSetLibrary,
                    snapshot.Configuration.CustomDrivers, cancellationToken);
            }

            telemetryProgressTracker.SetCurrentStep(MediaCreationStepNames.GenerateProvisioningPayloads);
            FoundryConnectProvisioningBundle connectBundle = snapshot.CreateConnectProvisioningBundle(
                Path.Combine(operationLease.OperationDirectoryPath, "Provisioning"),
                connectTelemetrySettings);
            logger.Debug(
                "Generated local provisioning payloads. ConnectAssetFileCount={ConnectAssetFileCount}, HasMediaSecretsKey={HasMediaSecretsKey}, AutopilotProfileCount={AutopilotProfileCount}",
                connectBundle.AssetFiles.Count,
                connectBundle.MediaSecretsKey is { Length: > 0 },
                options.IsAutopilotEnabled ? snapshot.Configuration.Autopilot.Profiles.Count : 0);

            telemetryProgressTracker.SetCurrentStep(MediaCreationStepNames.PrepareWinPeWorkspace);
            IProgress<WinPeWorkspacePreparationStage> workspacePreparationProgress =
                telemetryProgressTracker.CreateWorkspacePreparationProgress(
                    new Progress<WinPeWorkspacePreparationStage>(ReportWorkspacePreparationStage));

            ReportPhase(MediaCreationPhase.Preparing, 100);
            WinPeResult<WinPeWorkspacePreparationResult> preparationResult;
            WinPeMountedImageAssetProvisioningOptions assetProvisioning;
            try
            {
                assetProvisioning = CreateAssetProvisioningOptions(options, snapshot, tools, connectBundle,
                    deploymentProtectionMaterial, runtimePayloadProvisioning, deployTelemetrySettings, customImages, postInstallation, customDrivers);
                preparationResult = await workspacePreparationService.PrepareAsync(
                    new WinPeWorkspacePreparationOptions
                    {
                        DriverArchiveCacheDirectoryPath = Constants.WinPeDriverCacheDirectoryPath,
                        Artifact = artifact,
                        Tools = tools,
                        SignatureMode = options.SignatureMode,
                        IncludeWirelessSupport = options.IncludeWirelessSupport,
                        DriverCatalogUri = new WinPeDriverCatalogOptions().CatalogUri,
                        DriverVendors = options.DriverVendors,
                        CustomDriverDirectoryPath = options.CustomDriverDirectoryPath,
                        WinPeLanguage = options.WinPeLanguage,
                        WinPeKeyboardLayout = KeyboardLayoutSelection.Find(KeyboardLayoutCatalog.GetInstalled(), options.WinPeKeyboardLayoutId),
                        AssetProvisioning = assetProvisioning,
                        RuntimePayloadProvisioning = artifactRuntimePayloadProvisioning with
                        {
                            IncludePayloadsInImage = includeRuntimePayloadInImage
                        },
                        WindowsSourceCacheDirectoryPath = Constants.WindowsSourceCacheDirectoryPath,
                        WinReCacheDirectoryPath = Constants.WinReCacheDirectoryPath,
                        LegacyWindowsSourceDirectoryPath = Constants.WinReTempDirectoryPath,
                        Progress = workspacePreparationProgress,
                        DownloadProgress = telemetryProgressTracker.CreateDownloadProgress(
                            new Progress<WinPeDownloadProgress>(ReportDownloadProgress)),
                        CustomizationProgress = telemetryProgressTracker.CreateCustomizationProgress(
                            new Progress<WinPeMountedImageCustomizationProgress>(ReportCustomizationProgress))
                    },
                    cancellationToken);
            }
            finally
            {
                if (connectBundle.MediaSecretsKey is not null)
                {
                    CryptographicOperations.ZeroMemory(connectBundle.MediaSecretsKey);
                }
            }
            EnsureSuccess(preparationResult);
            preparedBootImageSource = preparationResult.Value!.BootImageSource;
            if (preparedBootImageSource == WinPeBootImageSource.WinPe)
            {
                WinPeDiagnostic? cause = preparationResult.Value.BootImageFallbackCause;
                logger.Debug(
                    "Boot media continues with the ADK Windows PE fallback. FailureCode={FailureCode}, FailureMessage={FailureMessage}",
                    cause?.Code,
                    cause?.Message);
            }

            logger.Debug(
                "WinPE workspace prepared. UseBootEx={UseBootEx}, RuntimePayloadInImage={RuntimePayloadInImage}",
                preparationResult.Value!.UseBootEx,
                includeRuntimePayloadInImage);

            return new PreparedMediaWorkspace(
                operationLease,
                preparationResult.Value!,
                tools,
                artifactRuntimePayloadProvisioning with
                {
                    MountedImagePath = string.Empty,
                    UsbCacheRootPath = string.Empty
                },
                customImages,
                postInstallation,
                customDrivers,
                assetProvisioning.DeployConfigurationJson!);
        }
        catch
        {
            ReleasePreparedWorkspace(customImages, postInstallation, customDrivers, operationLease);
            throw;
        }
    }

    private WinPeMountedImageAssetProvisioningOptions CreateAssetProvisioningOptions(
        MediaPreflightOptions options,
        DeploymentBuildSnapshot snapshot,
        WinPeToolPaths tools,
        FoundryConnectProvisioningBundle connectBundle,
        DeploymentMediaProtectionMaterial deploymentProtectionMaterial,
        WinPeRuntimePayloadProvisioningOptions runtimePayloadProvisioning,
        TelemetrySettings deployTelemetrySettings,
        WinPeCustomImageMediaLease? customImages,
        WinPeContentPackageMediaLease postInstallation,
        WinPeContentPackageMediaLease? customDrivers)
    {
        bool isHardwareHashMode = options.IsAutopilotEnabled &&
                                  options.AutopilotProvisioningMode == AutopilotProvisioningMode.HardwareHashUpload;
        string? oa3ToolSourcePath = null;
        if (isHardwareHashMode)
        {
            WinPeResult<string> oa3ToolResult = WinPeOa3ToolResolver.Resolve(tools.KitsRootPath, options.Architecture);
            EnsureSuccess(oa3ToolResult);
            oa3ToolSourcePath = oa3ToolResult.Value;
        }

        string deployConfigurationJson = snapshot.GenerateDeployConfigurationJson(
            deployTelemetrySettings, deploymentProtectionMaterial.DeploymentKey, deploymentProtectionMaterial.Settings);
        if (customImages is not null)
        {
            deployConfigurationJson = WinPeCustomImageMediaService.BindConfiguration(customImages, deployConfigurationJson);
        }
        deployConfigurationJson = WinPePreOobeMediaService.BindConfiguration(postInstallation, deployConfigurationJson);
        // Driver sets bind last so the configuration written into boot.wim carries all three media generations.
        if (customDrivers is not null)
        {
            deployConfigurationJson = WinPeCustomDriverSetMediaService.BindConfiguration(customDrivers, deployConfigurationJson);
        }

        return new WinPeMountedImageAssetProvisioningOptions
        {
            CurlExecutableSourcePath = ResolveCurlExecutablePath(),
            SevenZipSourceDirectoryPath = embeddedAssetService.GetSevenZipSourceDirectoryPath(),
            IanaWindowsTimeZoneMapJson = embeddedAssetService.GetIanaWindowsTimeZoneMapJson(),
            FoundryBootstrapConfiguration = new FoundryBootstrapConfigurationDocument
            {
                Telemetry = snapshot.Configuration.Telemetry with { RuntimePayloadSource = ResolveRuntimePayloadSource(runtimePayloadProvisioning.Bootstrap) }
            },
            FoundryConnectConfigurationJson = connectBundle.ConfigurationJson,
            DeployConfigurationJson = deployConfigurationJson,
            NetworkSecretsKey = connectBundle.MediaSecretsKey,
            DeploymentSecretsKey = deploymentProtectionMaterial.DeploymentKey,
            IsDeploymentProtectionEnabled = deploymentProtectionMaterial.Settings.IsEnabled,
            Unattend = snapshot.Configuration.Unattend,
            FoundryConnectAssetFiles = connectBundle.AssetFiles,
            AutopilotProvisioningMode = options.IsAutopilotEnabled
                ? options.AutopilotProvisioningMode
                : AutopilotProvisioningMode.JsonProfile,
            Oa3ToolSourcePath = oa3ToolSourcePath,
            AutopilotProfiles = options.IsAutopilotEnabled && options.AutopilotProvisioningMode == AutopilotProvisioningMode.JsonProfile
                ? snapshot.Configuration.Autopilot.Profiles
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
        (MediaCreationPhase phase, int phasePercent, string detailKey) = stage switch
        {
            WinPeWorkspacePreparationStage.ResolvingDrivers => (MediaCreationPhase.DownloadingDrivers, 0, "StartMedia.Operation.ResolvingDrivers"),
            WinPeWorkspacePreparationStage.CustomizingImage => (MediaCreationPhase.CustomizingBootImage, 0, string.Empty),
            WinPeWorkspacePreparationStage.EvaluatingSignaturePolicy => (MediaCreationPhase.CustomizingBootImage, 100, "StartMedia.Operation.EvaluatingSignaturePolicy"),
            _ => (MediaCreationPhase.Preparing, 100, string.Empty)
        };

        logger.Debug("WinPE workspace preparation stage changed. Stage={Stage}, Phase={Phase}", stage, phase);
        ReportPhase(phase, phasePercent, detailKey.Length == 0 ? string.Empty : localizationService.GetString(detailKey));
    }

    private void ReportMountCleanupRecoveryProgress(WinPeDismProgress progress)
    {
        string detail = LocalizeDismDetailStatus(progress.Status);
        ReportPhase(
            MediaCreationPhase.Preparing,
            15,
            detail.Length == 0 ? localizationService.GetString("StartMedia.Operation.CleaningWorkspace") : detail,
            progress.Percent);
    }

    private void ReportCustomizationProgress(WinPeMountedImageCustomizationProgress progress)
    {
        // A DISM sub-step names what is applied more precisely than the customization stage that runs it.
        string dismDetail = LocalizeDismDetailStatus(progress.DetailStatus);
        string detail = dismDetail.Length > 0 ? dismDetail : LocalizeCustomizationStatus(progress.Status);
        MediaCreationProgress overall = ReportPhase(MediaCreationPhase.CustomizingBootImage, progress.Percent, detail, progress.DetailPercent);

        int sampledPercent = progress.DetailPercent ?? progress.Percent;
        string sampledStatus = $"{progress.Status}|{progress.DetailStatus}";
        if (ShouldLogProgress(ref lastCustomizationLogStatus, ref lastCustomizationLogBucket, sampledStatus, sampledPercent))
        {
            logger.Debug(
                "WinPE image customization progress changed. CoreStatus={CoreStatus}, Percent={Percent}, OverallPercent={OverallPercent}, DetailStatus={DetailStatus}, DetailPercent={DetailPercent}",
                progress.Status,
                progress.Percent,
                overall.Percent,
                progress.DetailStatus,
                progress.DetailPercent);
        }
    }

    private void ReportDownloadProgress(WinPeDownloadProgress progress)
    {
        if (ShouldLogProgress(ref lastDownloadLogStatus, ref lastDownloadLogBucket, $"{progress.Status}|{progress.ItemName}", progress.Percent))
        {
            logger.Debug(
                "Final media download progress changed. DownloadStatus={DownloadStatus}, ItemName={ItemName}, DownloadPercent={DownloadPercent}",
                progress.Status,
                progress.ItemName,
                progress.Percent);
        }

        string detail = FormatDownloadDetail(progress);
        if (progress.Status == WinPeProgressStatuses.DownloadingDriverPackage && progress.ItemCount is > 0)
        {
            // Driver packages download in sequence, so their count drives the driver phase.
            int completedItems = Math.Clamp((progress.ItemNumber ?? 1) - 1, 0, progress.ItemCount.Value);
            int phasePercent = (int)((completedItems + ((progress.Percent ?? 0) / 100.0)) * 100 / progress.ItemCount.Value);
            ReportPhase(MediaCreationPhase.DownloadingDrivers, phasePercent, detail, progress.Percent);
            return;
        }

        ReportDetail(detail, detail.Length == 0 ? null : progress.Percent);
    }

    private void ReportFinalMediaProgress(WinPeMediaProgress progress)
    {
        if (progressPlan is null)
        {
            return;
        }

        lastFinalMediaPercent = Math.Max(lastFinalMediaPercent, progress.Percent);
        MediaCreationProgress overall = progressPlan.ReportFinalMedia(progress.Percent);
        ReportProgress(overall, LocalizeFinalMediaStatus(progress.Status), null);

        if (ShouldLogProgress(ref lastFinalMediaLogStatus, ref lastFinalMediaLogBucket, progress.Status, progress.Percent))
        {
            logger.Debug(
                "Final media output progress changed. CoreStatus={CoreStatus}, Percent={Percent}, OverallPercent={OverallPercent}, HasLogDetail={HasLogDetail}",
                progress.Status,
                progress.Percent,
                overall.Percent,
                !string.IsNullOrWhiteSpace(progress.LogDetail));
        }
    }

    /// <summary>
    /// Reports progress inside a media creation phase with an optional detail line.
    /// </summary>
    private MediaCreationProgress ReportPhase(MediaCreationPhase phase, int phasePercent, string detail = "", int? detailProgress = null)
    {
        if (progressPlan is null)
        {
            return default;
        }

        MediaCreationProgress overall = progressPlan.Report(phase, phasePercent);
        ReportProgress(overall, detail, detailProgress);
        return overall;
    }

    /// <summary>
    /// Updates only the detail line, keeping the current phase and percentage.
    /// </summary>
    private void ReportDetail(string detail, int? detailProgress)
    {
        if (progressPlan is not null)
        {
            ReportProgress(progressPlan.Current, detail, detailProgress);
        }
    }

    private void ReportProgress(MediaCreationProgress overall, string detail, int? detailProgress)
    {
        operationProgressService.Report(overall.Percent, FormatStep(overall), detailProgress, detail);
    }

    private string FormatStep(MediaCreationProgress progress)
    {
        return localizationService.FormatString(
            "StartMedia.Operation.StepFormat",
            progress.StepNumber,
            progress.StepCount,
            GetPhaseLabel(progress.Phase));
    }

    private string GetPhaseLabel(MediaCreationPhase phase)
    {
        return localizationService.GetString(phase switch
        {
            MediaCreationPhase.Preparing => "StartMedia.Operation.Phase.Preparing",
            MediaCreationPhase.DownloadingDrivers => "StartMedia.Operation.Phase.DownloadingDrivers",
            MediaCreationPhase.CustomizingBootImage => "StartMedia.Operation.Phase.CustomizingBootImage",
            MediaCreationPhase.CreatingIso => "StartMedia.Operation.Phase.CreatingIso",
            MediaCreationPhase.FormattingUsbDrive => "StartMedia.Operation.Phase.FormattingUsbDrive",
            MediaCreationPhase.CopyingToUsbDrive => "StartMedia.Operation.Phase.CopyingToUsbDrive",
            MediaCreationPhase.UpdatingUsbDrive => "StartMedia.Operation.Phase.UpdatingUsbDrive",
            MediaCreationPhase.CreatingPxeFiles => "StartMedia.Operation.Phase.CreatingPxeFiles",
            _ => "StartMedia.Operation.Phase.Finishing"
        });
    }

    /// <summary>
    /// Returns the brand name of a driver package vendor; file names are too long for the detail line.
    /// </summary>
    private static string GetVendorName(WinPeVendorSelection? vendor) => vendor switch
    {
        WinPeVendorSelection.Hp => "HP",
        null => string.Empty,
        _ => vendor.Value.ToString()
    };

    private string FormatDownloadDetail(WinPeDownloadProgress progress)
    {
        string text = progress.Status switch
        {
            WinPeProgressStatuses.DownloadingDriverPackage => localizationService.FormatString(
                "StartMedia.Operation.DownloadingDriverPackageFormat",
                progress.ItemNumber ?? 1,
                progress.ItemCount ?? 1,
                GetVendorName(progress.Vendor)),
            WinPeProgressStatuses.DownloadingWindowsSourcePackage => localizationService.GetString("StartMedia.Operation.DownloadingWindowsSourcePackage"),
            WinPeProgressStatuses.DownloadingRuntimePayload => localizationService.FormatString("StartMedia.Operation.DownloadingRuntimePayloadFormat", progress.ItemName),
            WinPeProgressStatuses.VerifyingCachedSource => progress.Vendor is null
                ? localizationService.GetString("StartMedia.Operation.VerifyingWindowsSourcePackage")
                : localizationService.FormatString("StartMedia.Operation.VerifyingDriverPackageFormat", GetVendorName(progress.Vendor)),
            WinPeProgressStatuses.ReusingWindowsSourcePackage
                or WinPeProgressStatuses.WindowsSourcePackageReady
                or WinPeProgressStatuses.ReusingDriverPackage
                or WinPeProgressStatuses.DriverPackageReady => string.Empty,
            _ => localizationService.GetString("StartMedia.Operation.Downloading")
        };

        if (text.Length == 0 || progress.BytesTransferred is not long transferred)
        {
            return text;
        }

        string bytes = progress.TotalBytes is long total and > 0
            ? localizationService.FormatString(
                "StartMedia.Operation.ByteProgressFormat",
                ByteSizeFormatter.Format(localizationService, (ulong)transferred),
                ByteSizeFormatter.Format(localizationService, (ulong)total))
            : ByteSizeFormatter.Format(localizationService, (ulong)transferred);
        return localizationService.FormatString("StartMedia.Operation.DetailWithBytesFormat", text, bytes);
    }

    private void ResetProgressLogSampling()
    {
        lastCustomizationLogStatus = null;
        lastCustomizationLogBucket = -1;
        lastDownloadLogStatus = null;
        lastDownloadLogBucket = -1;
        lastFinalMediaLogStatus = null;
        lastFinalMediaLogBucket = -1;
    }

    private static bool ShouldLogProgress(ref string? previousStatus, ref int previousBucket, string? status, int? percent)
    {
        int bucket = percent.HasValue ? Math.Clamp(percent.Value, 0, 100) / 10 : -2;
        bool shouldLog = !string.Equals(previousStatus, status, StringComparison.Ordinal) || previousBucket != bucket;
        if (shouldLog)
        {
            previousStatus = status;
            previousBucket = bucket;
        }

        return shouldLog;
    }

    private string LocalizeCustomizationStatus(string status)
    {
        return LocalizeStatus(status switch
        {
            WinPeProgressStatuses.PreparingBootImageCustomization => "StartMedia.Operation.PreparingBootImageCustomization",
            WinPeProgressStatuses.MountingBootImage => "StartMedia.Operation.MountingBootImage",
            WinPeProgressStatuses.InjectingDrivers => "StartMedia.Operation.InjectingDrivers",
            WinPeProgressStatuses.ApplyingLanguageAndComponents => "StartMedia.Operation.ApplyingLanguageAndComponents",
            WinPeProgressStatuses.ProvisioningBootAssets => "StartMedia.Operation.ProvisioningBootAssets",
            WinPeProgressStatuses.ProvisioningRuntimePayloads => "StartMedia.Operation.ProvisioningRuntimePayloads",
            WinPeProgressStatuses.CommittingImageChanges => "StartMedia.Operation.CommittingImageChanges",
            WinPeProgressStatuses.ResolvingWindowsSourceCatalog
                or WinPeProgressStatuses.SelectedWindowsSourcePackage => "StartMedia.Operation.ResolvingWindowsSourceCatalog",
            WinPeProgressStatuses.PreparingWindowsSourcePackage => "StartMedia.Operation.PreparingWindowsSourcePackage",
            WinPeProgressStatuses.ResolvingWindowsImageIndex => "StartMedia.Operation.ResolvingWindowsImageIndex",
            WinPeProgressStatuses.ExportingWindowsSourceImage => "StartMedia.Operation.ExportingWindowsSourceImage",
            WinPeProgressStatuses.MountingWindowsSourceImage => "StartMedia.Operation.MountingWindowsSourceImage",
            WinPeProgressStatuses.StagingBootImageDependencies => "StartMedia.Operation.StagingBootImageDependencies",
            WinPeProgressStatuses.ReplacingBootImageWithWinRe => "StartMedia.Operation.ReplacingBootImageWithWinRe",
            _ => null
        });
    }

    /// <summary>
    /// Localizes the DISM sub-steps that say more than their customization stage; other DISM steps return empty.
    /// </summary>
    private string LocalizeDismDetailStatus(string status)
    {
        return LocalizeStatus(status switch
        {
            WinPeProgressStatuses.DismApplyingLanguagePack => "StartMedia.Operation.DismApplyingLanguagePack",
            WinPeProgressStatuses.DismApplyingOptionalComponents => "StartMedia.Operation.DismApplyingOptionalComponents",
            WinPeProgressStatuses.DismApplyingInternationalSettings => "StartMedia.Operation.DismApplyingInternationalSettings",
            WinPeProgressStatuses.DismDiscardingMountedImage => "StartMedia.Operation.DismDiscardingMountedImage",
            _ => null
        });
    }

    private string LocalizeFinalMediaStatus(string status)
    {
        return LocalizeStatus(status switch
        {
            WinPeProgressStatuses.PreparingIsoOutput => "StartMedia.Operation.PreparingIsoOutput",
            WinPeProgressStatuses.PreparingIsoWorkspace => "StartMedia.Operation.PreparingIsoWorkspace",
            WinPeProgressStatuses.CreatingIsoMedia or WinPeProgressStatuses.RunningMakeWinPeMediaIso => "StartMedia.Operation.WritingIso",
            WinPeProgressStatuses.FinalizingIsoOutput => "StartMedia.Operation.FinalizingIsoOutput",
            WinPeProgressStatuses.PreparingPxeOutput => "StartMedia.Operation.PreparingPxeOutput",
            WinPeProgressStatuses.CopyingPxeFiles => "StartMedia.Operation.CopyingPxeFiles",
            WinPeProgressStatuses.StagingCustomImages => "StartMedia.Operation.CopyingCustomImages",
            WinPeProgressStatuses.StagingPostInstallation => "StartMedia.Operation.CopyingPostInstallation",
            WinPeProgressStatuses.StagingCustomDrivers => "StartMedia.Operation.CopyingCustomDrivers",
            WinPeProgressStatuses.CustomDriversVerified => "StartMedia.Operation.CustomDriversVerified",
            WinPeProgressStatuses.CustomDriversCleanupSkipped => "StartMedia.Operation.CustomDriversCleanupSkipped",
            WinPeProgressStatuses.ValidatingUsbTarget => "StartMedia.Operation.ValidatingUsbTarget",
            WinPeProgressStatuses.CheckingUsbTargetSafety => "StartMedia.Operation.CheckingUsbTargetSafety",
            WinPeProgressStatuses.InspectingUsbMediaLayout => "StartMedia.Operation.InspectingUsbMediaLayout",
            WinPeProgressStatuses.CheckingMediaContent => "StartMedia.Operation.CheckingMediaContent",
            WinPeProgressStatuses.PartitioningUsbTarget => "StartMedia.Operation.PartitioningUsbTarget",
            WinPeProgressStatuses.OpeningUsbDisk => "StartMedia.Operation.OpeningUsbDisk",
            WinPeProgressStatuses.PreparingUsbDiskAttributes => "StartMedia.Operation.PreparingUsbDiskAttributes",
            WinPeProgressStatuses.ClearingUsbPartitionTable => "StartMedia.Operation.ClearingUsbPartitionTable",
            WinPeProgressStatuses.InitializingUsbPartitionTable => "StartMedia.Operation.InitializingUsbPartitionTable",
            WinPeProgressStatuses.CreatingUsbBootPartition => "StartMedia.Operation.CreatingUsbBootPartition",
            WinPeProgressStatuses.FormattingUsbBootPartition => "StartMedia.Operation.FormattingUsbBootPartition",
            WinPeProgressStatuses.CreatingUsbCachePartition => "StartMedia.Operation.CreatingUsbCachePartition",
            WinPeProgressStatuses.FormattingUsbCachePartition => "StartMedia.Operation.FormattingUsbCachePartition",
            WinPeProgressStatuses.CopyingWinPeMediaToUsb => "StartMedia.Operation.CopyingUsbMedia",
            WinPeProgressStatuses.VerifyingUsbBootMedia => "StartMedia.Operation.VerifyingUsbMedia",
            WinPeProgressStatuses.PreparingUsbCachePartition => "StartMedia.Operation.PreparingUsbCache",
            WinPeProgressStatuses.ProvisioningUsbRuntimePayloads => "StartMedia.Operation.ProvisioningUsbRuntimePayloads",
            _ => null
        });
    }

    /// <summary>
    /// Returns the localized text of a status resource, or an empty string for a status with no visible detail,
    /// such as a completion status. Unknown Core statuses are never shown as raw English text.
    /// </summary>
    private string LocalizeStatus(string? resourceKey)
    {
        return resourceKey is null ? string.Empty : localizationService.GetString(resourceKey);
    }

    /// <summary>
    /// Releases prepared external content in reverse acquisition order (driver sets, post-installation content, custom images),
    /// then removes the operation workspace. Each step runs even when an earlier one throws, so one failed release cannot keep
    /// other library files leased or leave the workspace behind.
    /// </summary>
    private void ReleasePreparedWorkspace(WinPeCustomImageMediaLease? customImages, WinPeContentPackageMediaLease? postInstallation,
        WinPeContentPackageMediaLease? customDrivers, WinPeWorkspaceLease? operationLease)
    {
        try
        {
            customDrivers?.Dispose();
        }
        finally
        {
            try
            {
                postInstallation?.Dispose();
            }
            finally
            {
                try
                {
                    customImages?.Dispose();
                }
                finally
                {
                    CleanupPreparedWorkspace(operationLease);
                }
            }
        }
    }

    private void CleanupPreparedWorkspace(WinPeWorkspaceLease? operationLease)
    {
        if (operationLease is null) return;
        string path = operationLease.OperationDirectoryPath;
        operationLease.Dispose();
        DeleteWorkspaceDirectory(path, reportProgress: true);
    }
    private void CleanupStaleWinPeWorkspaces()
    {
        string workspaceRoot = Path.GetFullPath(Constants.WorkspacesDirectoryPath);
        if (!Directory.Exists(workspaceRoot))
        {
            return;
        }

        foreach (string workspacePath in Directory.EnumerateDirectories(workspaceRoot))
        {
            // Legacy folders such as Configuration or Iso are not media operations and must never be recovered.
            if (!WinPeWorkspaceCleanupService.IsOperationWorkspace(workspacePath))
            {
                logger.Debug("Skipped non-operation directory during WinPE workspace cleanup. WorkspacePath={WorkspacePath}", workspacePath);
                continue;
            }

            DeleteWorkspaceDirectory(workspacePath, reportProgress: false);
        }
    }

    private void DeleteWorkspaceDirectory(string workspacePath, bool reportProgress)
    {
        try
        {
            if (reportProgress)
            {
                ReportDetail(localizationService.GetString("StartMedia.Operation.RemovingTemporaryFiles"), null);
            }

            logger.Debug("Cleaning WinPE workspace. WorkspacePath={WorkspacePath}", workspacePath);
            WinPeResult cleanup = workspaceCleanupService.DeleteOwnedOperation(Constants.WorkspacesDirectoryPath, workspacePath);
            if (!cleanup.IsSuccess)
            {
                logger.Warning("WinPE workspace cleanup was not completed. WorkspacePath={WorkspacePath}, Details={Details}",
                    workspacePath, cleanup.Error?.Details);
                return;
            }
            logger.Debug("WinPE workspace cleaned. WorkspacePath={WorkspacePath}", workspacePath);
        }
        catch (Exception ex)
        {
            logger.Warning(ex, "Failed to clean WinPE workspace. WorkspacePath={WorkspacePath}", workspacePath);
        }
    }

    private async Task ShowBlockedDialogAsync(
        string titleResourceKey,
        IReadOnlyList<MediaPreflightBlockingReason> blockingReasons,
        AutopilotConfigurationValidationCode autopilotValidationCode)
    {
        string[] reasons = blockingReasons
            .Distinct()
            .Select(reason => GetBlockingReasonText(reason, autopilotValidationCode))
            .ToArray();
        string message = reasons.Length switch
        {
            0 => localizationService.GetString("StartMedia.Blocked.Unknown"),
            1 => reasons[0],
            _ => localizationService.GetString("StartMedia.Blocked.LeadIn")
                + Environment.NewLine + Environment.NewLine
                + string.Join(Environment.NewLine, reasons.Select(reason => $"\u2022 {reason}"))
        };

        await ShowBlockedDialogAsync(titleResourceKey, message);
    }

    private async Task ShowBlockedDialogAsync(string titleResourceKey, string message)
    {
        await dialogService.ShowMessageAsync(new DialogRequest(
            localizationService.GetString(titleResourceKey),
            message,
            localizationService.GetString("Common.Close")));
    }

    private async Task RefreshUnattendSourcesAsync()
    {
        if (isDisposed || isRefreshingUnattendSources || !foundryConfigurationStateService.Current.Unattend.IsEnabled)
        {
            return;
        }

        isRefreshingUnattendSources = true;
        RefreshEvaluation();
        try
        {
            await foundryConfigurationStateService.RefreshUnattendSourcesAsync();
        }
        finally
        {
            isRefreshingUnattendSources = false;
            if (!isDisposed)
            {
                RefreshEvaluation();
            }
        }
    }

    partial void OnSelectedUsbDiskChanged(SelectionOption<WinPeUsbDiskCandidate>? value)
    {
        IsSelectedUsbFoundryMedia = value?.Value.IsFoundryMedia == true;
        RefreshEvaluation();
    }

    partial void OnIsoOutputPathChanged(string value)
    {
        if (isLoadingConfiguration)
        {
            return;
        }

        UpdateGeneral(foundryConfigurationStateService.Current.General with { IsoOutputPath = value });
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

        UpdateGeneral(foundryConfigurationStateService.Current.General with { UsbPartitionStyle = value.Value });
        RefreshEvaluation(loadConfigurationFromSettings: false);
    }

    partial void OnSelectedFormatModeChanged(SelectionOption<UsbFormatMode>? value)
    {
        if (value is null || isLoadingConfiguration)
        {
            return;
        }

        UpdateGeneral(foundryConfigurationStateService.Current.General with { UsbFormatMode = value.Value });
        RefreshEvaluation();
    }

    private void OnConfigurationOverviewChanged(object? sender, EventArgs e)
    {
        if (!appDispatcher.TryEnqueue(() => RefreshEvaluation()))
        {
            logger.Warning("Failed to enqueue Start page configuration overview refresh.");
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
        PageDescription = localizationService.GetString("StartMedia.PageDescription");
        RebuildFormatModes();
        RebuildUsbCandidateDisplayNames();
    }

    private void RefreshEvaluation(bool loadConfigurationFromSettings = true)
    {
        if (loadConfigurationFromSettings)
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
        }

        WinPeLanguage = NormalizeCultureName(foundryConfigurationStateService.Current.General.WinPeLanguage);
        availableWinPeLanguages = GetAvailableWinPeLanguages();
        MediaPreflightOptions options = CreatePreflightOptions();
        MediaPreflightEvaluation evaluation = MediaPreflightService.Evaluate(options);
        IsSelectedUsbFoundryMedia = options.SelectedUsbDisk?.IsFoundryMedia == true;
        CanGenerateIsoSummary = evaluation.CanGenerateIsoSummary;
        CanGenerateUsbSummary = evaluation.CanGenerateUsbSummary;
        CanSelectUsbDisk = UsbCandidates.Count > 0 && !IsRefreshingUsbCandidates;
        CanCreateIso = evaluation.CanCreateIso && !IsMediaOperationRunning && !isUpdateAdvisoryPending && !isRefreshingUnattendSources;
        CanCreateUsb = evaluation.CanCreateUsb && !IsMediaOperationRunning && !isUpdateAdvisoryPending && !isRefreshingUnattendSources;
        CanCreatePxe = evaluation.CanCreatePxe && !IsMediaOperationRunning && !isUpdateAdvisoryPending && !isRefreshingUnattendSources;
        FinalExecutionStatus = localizationService.GetString(!options.IsFinalExecutionEnabled
            ? "StartMedia.FinalExecution.Deferred"
            : evaluation.CanCreateIso || evaluation.CanCreateUsb
                ? "StartMedia.FinalExecution.ReadyNow"
                : "StartMedia.FinalExecution.Ready");
        ConfigurationOverviewEvaluation overview = configurationOverviewService.Evaluate();
        ReadinessSeverity = GetReadinessSeverity(evaluation);
        StatusSummary = BuildStatusText(evaluation, overview);
        RebuildConfigurationOverview(options, evaluation, overview);

    }

    private void LoadConfigurationFromSettings()
    {
        GeneralSettings general = foundryConfigurationStateService.Current.General;
        IsoOutputPath = general.IsoOutputPath ?? string.Empty;
        SelectedArchitecture = SelectOption(Architectures, general.Architecture);
        UseCa2023Signature = general.UseCa2023;
        RebuildPartitionStyles();
        SelectedFormatMode = SelectOption(FormatModes, general.UsbFormatMode);
        IncludeDellDrivers = general.IncludeDellDrivers;
        IncludeHpDrivers = general.IncludeHpDrivers;
        CustomDriverDirectoryPath = general.CustomDriverDirectoryPath ?? string.Empty;
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

        AutopilotConfigurationValidationResult autopilotValidation = foundryConfigurationStateService.AutopilotConfigurationValidation;
        NetworkMediaReadinessEvaluation networkReadiness = foundryConfigurationStateService.NetworkMediaReadiness;

        return new MediaPreflightOptions
        {
            IsAdkReady = adkService.CurrentStatus.CanCreateMediaFor(SelectedArchitecture?.Value ?? WinPeArchitecture.X64),
            IsNetworkConfigurationReady = networkReadiness.IsNetworkConfigurationReady,
            NetworkConfigurationValidationCode = networkReadiness.NetworkConfigurationValidationCode,
            IsDeployConfigurationReady = foundryConfigurationStateService.IsDeployConfigurationReady,
            IsConnectProvisioningReady = networkReadiness.IsConnectProvisioningReady,
            AreRequiredSecretsReady = networkReadiness.AreRequiredSecretsReady,
            IsAutopilotEnabled = foundryConfigurationStateService.IsAutopilotEnabled,
            IsAutopilotConfigurationReady = autopilotValidation.IsReady,
            AutopilotConfigurationValidationCode = autopilotValidation.Code,
            AutopilotProvisioningMode = foundryConfigurationStateService.AutopilotProvisioningMode,
            AutopilotProfileDisplayName = foundryConfigurationStateService.SelectedAutopilotProfileDisplayName,
            AutopilotProfileFolderName = foundryConfigurationStateService.SelectedAutopilotProfileFolderName,
            IsFinalExecutionEnabled = true,
            IsoOutputPath = IsoOutputPath,
            Architecture = SelectedArchitecture?.Value ?? WinPeArchitecture.X64,
            SignatureMode = UseCa2023Signature ? WinPeSignatureMode.Pca2023 : WinPeSignatureMode.Pca2011,
            UsbPartitionStyle = SelectedPartitionStyle?.Value ?? UsbPartitionStyle.Gpt,
            UsbFormatMode = SelectedFormatMode?.Value ?? UsbFormatMode.Quick,
            WinPeLanguage = WinPeLanguage,
            AvailableWinPeLanguages = availableWinPeLanguages,
            WinPeKeyboardLayoutId = foundryConfigurationStateService.Current.General.WinPeKeyboardLayoutId,
            AvailableKeyboardLayoutIds = KeyboardLayoutCatalog.GetInstalled().Select(layout => layout.Id).ToArray(),
            IncludeWirelessSupport = IsWirelessSupportRequested(),
            DriverVendors = vendors,
            CustomDriverDirectoryPath = CustomDriverDirectoryPath,
            CustomImages = foundryConfigurationStateService.Current.CustomImages,
            SelectedUsbDisk = SelectedUsbDisk?.Value
        };
    }

    private void UpdateGeneral(GeneralSettings settings)
    {
        foundryConfigurationStateService.UpdateGeneral(settings);
    }

    private void SynchronizeRuntimeTelemetrySettings()
    {
        foundryConfigurationStateService.UpdateTelemetry(CreateRuntimeTelemetrySettings(TelemetryRuntimePayloadSources.None));
    }

    private TelemetrySettings CreateRuntimeTelemetrySettings(string runtimePayloadSource)
    {
        return new TelemetrySettings
        {
            IsEnabled = appSettingsService.Current.Telemetry.IsEnabled,
            IsRemoteDiagnosticsEnabled = appSettingsService.Current.Telemetry.IsRemoteDiagnosticsEnabled,
            InstallId = appSettingsService.Current.Telemetry.InstallId,
            HostUrl = TelemetryDefaults.PostHogEuHost,
            ProjectToken = TelemetryDefaults.ProjectToken,
            RuntimePayloadSource = runtimePayloadSource
        };
    }

    private async Task TrackMediaCreatedAsync(
        FinalMediaTarget target,
        MediaPreflightOptions options,
        FoundryConfigurationDocument capturedConfiguration,
        bool success,
        string? failedStepName,
        TimeSpan duration,
        string operationId,
        WinPeDiagnostic? diagnostic,
        CancellationToken cancellationToken)
    {
        WinPeRuntimePayloadProvisioningOptions runtimePayloadProvisioning = AddReleaseRuntimeProvisioning(CreateRuntimePayloadProvisioningOptions(
            options.Architecture,
            Constants.WorkspacesDirectoryPath,
            Constants.WorkspacesDirectoryPath,
            Constants.WorkspacesDirectoryPath));

        string bootMediaTarget = GetTelemetryBootMediaTarget(target);
        string bootMediaUsbOperation = target switch
        {
            FinalMediaTarget.Usb => TelemetryBootMediaUsbOperations.Create,
            FinalMediaTarget.UsbUpdate => TelemetryBootMediaUsbOperations.Update,
            _ => TelemetryBootMediaUsbOperations.None
        };
        IReadOnlyDictionary<string, object?> properties = BootMediaTelemetryPropertyBuilder.Build(
            bootMediaTarget,
            bootMediaUsbOperation,
            options,
            capturedConfiguration,
            success,
            failedStepName,
            duration,
            ResolveRuntimePayloadSource(runtimePayloadProvisioning.Connect),
            ResolveRuntimePayloadSource(runtimePayloadProvisioning.Deploy),
            diagnostic,
            operationId,
            adkService.CurrentStatus);

        logger.Debug(
            "Tracking media telemetry event. Target={Target}, UsbOperation={UsbOperation}, Success={Success}, FailedStepName={FailedStepName}, DurationSeconds={DurationSeconds}, Architecture={Architecture}, BootImageSource={BootImageSource}, SignatureMode={SignatureMode}, ConnectRuntimePayloadSource={ConnectRuntimePayloadSource}, DeployRuntimePayloadSource={DeployRuntimePayloadSource}.",
            properties["boot_media_target"],
            properties["boot_media_usb_operation"],
            success,
            failedStepName,
            properties["boot_media_creation_duration_seconds"],
            properties["boot_media_architecture"],
            properties["boot_media_boot_image_source"],
            properties["boot_media_signature_mode"],
            properties["boot_media_connect_runtime_payload_source"],
            properties["boot_media_deploy_runtime_payload_source"]);

        await telemetryService.TrackAsync(TelemetryEvents.OsdBootMediaFinished, properties, cancellationToken);
        logger.Debug("Media telemetry event queued. Target={Target}, Success={Success}.", properties["boot_media_target"], success);
    }

    private string BuildStatusText(
        MediaPreflightEvaluation evaluation,
        ConfigurationOverviewEvaluation overview)
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

        int actionableItemCount = overview.NeedsAttentionCount;

        return actionableItemCount == 0
            ? localizationService.GetString("StartMedia.Readiness.Status.Warnings")
            : string.Format(localizationService.GetString("StartMedia.Readiness.Status.NeedsAttention"), actionableItemCount);
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

    private void RebuildConfigurationOverview(
        MediaPreflightOptions options,
        MediaPreflightEvaluation mediaEvaluation,
        ConfigurationOverviewEvaluation overview)
    {
        FoundryConfigurationDocument configuration = foundryConfigurationStateService.Current;
        NetworkSettings effectiveNetwork = networkSecretStateService.ApplyRequiredSecrets(configuration.Network);

        IsGeneralConfigurationOverviewExpanded = ReplaceOverviewItems(
            GeneralConfigurationOverviewItems,
            BuildGeneralConfigurationOverview(configuration, options, mediaEvaluation, overview));
        IsNetworkOverviewExpanded = ReplaceOverviewItems(
            NetworkOverviewItems,
            BuildNetworkOverview(configuration, effectiveNetwork, overview));
        IsAutopilotOverviewExpanded = ReplaceOverviewItems(
            AutopilotOverviewItems,
            BuildAutopilotOverview(options, overview));
        IsDomainJoinOverviewExpanded = ReplaceOverviewItems(DomainJoinOverviewItems,
        (StartConfigurationOverviewItemViewModel[])[
            CreateOverviewItem(ConfigurationOverviewItem.DomainJoinAutomatic, overview, "Nav_ZeroTouchDomainJoinKey.Title",
                localizationService.GetString("Nav_ZeroTouchDomainJoinKey.Description"), ConfigurationNavigationTarget.DomainJoinAutomatic),
            CreateOverviewItem(ConfigurationOverviewItem.DomainJoinInteractive, overview, "Nav_InteractiveDomainJoinKey.Title",
                localizationService.GetString("Nav_InteractiveDomainJoinKey.Description"), ConfigurationNavigationTarget.DomainJoinInteractive)
        ]);
        IsCustomizationOverviewExpanded = ReplaceOverviewItems(
            CustomizationOverviewItems,
            BuildCustomizationOverview(configuration, mediaEvaluation, overview));

        GeneralConfigurationOverviewSummary = GetOverviewSummary(GeneralConfigurationOverviewItems);
        NetworkOverviewSummary = GetOverviewSummary(NetworkOverviewItems);
        AutopilotOverviewSummary = GetOverviewSummary(AutopilotOverviewItems);
        DomainJoinOverviewSummary = GetOverviewSummary(DomainJoinOverviewItems);
        CustomizationOverviewSummary = GetOverviewSummary(CustomizationOverviewItems);
    }

    private IReadOnlyList<StartConfigurationOverviewItemViewModel> BuildGeneralConfigurationOverview(
        FoundryConfigurationDocument configuration,
        MediaPreflightOptions options,
        MediaPreflightEvaluation mediaEvaluation,
        ConfigurationOverviewEvaluation overview)
    {
        GeneralSettings general = configuration.General;
        bool oobePasswordRequiresDeploymentProtection =
            OobeAccountConfigurationValidator.RequiresProtectedMedia(configuration.Customization.Oobe) &&
            !general.DeploymentProtection.IsEnabled;
        MediaPreflightBlockingReason? languageReason = GetFirstReason(
            mediaEvaluation,
            MediaPreflightBlockingReason.MissingWinPeLanguage,
            MediaPreflightBlockingReason.WinPeLanguageUnavailable,
            MediaPreflightBlockingReason.WinPeKeyboardLayoutUnavailable);
        MediaPreflightBlockingReason? driverReason = GetFirstReason(
            mediaEvaluation,
            MediaPreflightBlockingReason.CustomDriverDirectoryNotFound,
            MediaPreflightBlockingReason.CustomDriverDirectoryHasNoInfFiles);

        return
        (StartConfigurationOverviewItemViewModel[])
        [
            CreateOverviewItem(ConfigurationOverviewItem.Architecture, overview, "StartMedia.Field.Architecture",
                FormatArchitecture(options.Architecture), ConfigurationNavigationTarget.General),
            CreateOverviewItem(ConfigurationOverviewItem.SecureBoot, overview, "GeneralConfiguration.SecureBoot.Header",
                FormatSignatureMode(options.SignatureMode), ConfigurationNavigationTarget.General),
            CreateOverviewItem(ConfigurationOverviewItem.WinPeLanguage, overview, "StartMedia.Field.WinPeLanguage",
                languageReason.HasValue ? GetBlockingReasonText(languageReason.Value) : FormatValue(WinPeLanguage),
                ConfigurationNavigationTarget.General),
            CreateOverviewItem(ConfigurationOverviewItem.TimeZone, overview, "GeneralConfiguration.TimeZone.Header",
                string.IsNullOrWhiteSpace(configuration.Localization.DefaultTimeZoneId)
                    ? localizationService.GetString("Common.AutomaticOption")
                    : configuration.Localization.DefaultTimeZoneId,
                ConfigurationNavigationTarget.General),
            CreateOverviewItem(ConfigurationOverviewItem.DeploymentCompletion, overview, "GeneralConfiguration.Completion.Header",
                general.AutomaticRebootEnabled
                    ? $"{DeploymentRebootDelay.NormalizeRuntime(general.AutomaticRebootDelaySeconds)} s"
                    : localizationService.GetString("Common.Disabled"),
                ConfigurationNavigationTarget.General),
            CreateOverviewItem(ConfigurationOverviewItem.DeploymentProtection, overview, "GeneralConfiguration.DeploymentProtection.Header",
                localizationService.GetString(oobePasswordRequiresDeploymentProtection
                    ? "Customization.OobeAccountPasswordDescription"
                    : "GeneralConfiguration.DeploymentProtection.Description"),
                ConfigurationNavigationTarget.General),
            CreateOverviewItem(ConfigurationOverviewItem.DriverOptions, overview, "StartMedia.DriverOptions.Header",
                driverReason.HasValue
                    ? GetBlockingReasonText(driverReason.Value)
                    : FormatDriverOptions(options.DriverVendors, options.CustomDriverDirectoryPath),
                ConfigurationNavigationTarget.General)
        ];
    }

    private IReadOnlyList<StartConfigurationOverviewItemViewModel> BuildNetworkOverview(
        FoundryConfigurationDocument configuration,
        NetworkSettings effectiveNetwork,
        ConfigurationOverviewEvaluation overview)
    {
        string ethernetDescription = configuration.Network.Dot1x.IsEnabled
            ? FormatValue(Path.GetFileName(configuration.Network.Dot1x.ProfileTemplatePath))
            : localizationService.GetString("Nav_EthernetDot1xKey.Description");
        string wifiDescription = configuration.Network.Wifi.IsEnabled
            ? string.Join(" · ", new[]
            {
                configuration.Network.Wifi.Ssid,
                configuration.Network.Wifi.SecurityType
            }.Where(value => !string.IsNullOrWhiteSpace(value)))
            : localizationService.GetString("Nav_WifiKey.Description");
        NetworkConfigurationValidationResult ethernetValidation = NetworkConfigurationValidator.Validate(effectiveNetwork with
        {
            WifiProvisioned = false,
            Wifi = new WifiSettings()
        });
        NetworkConfigurationValidationResult wifiValidation = NetworkConfigurationValidator.Validate(effectiveNetwork with
        {
            Dot1x = new Dot1xSettings()
        });

        return
        (StartConfigurationOverviewItemViewModel[])
        [
            CreateOverviewItem(ConfigurationOverviewItem.EthernetDot1x, overview, "Nav_EthernetDot1xKey.Title",
                GetNetworkDescription(overview[ConfigurationOverviewItem.EthernetDot1x], ethernetDescription, ethernetValidation),
                ConfigurationNavigationTarget.EthernetDot1x),
            CreateOverviewItem(ConfigurationOverviewItem.Wifi, overview, "Nav_WifiKey.Title",
                GetNetworkDescription(overview[ConfigurationOverviewItem.Wifi], FormatValue(wifiDescription), wifiValidation),
                ConfigurationNavigationTarget.Wifi)
        ];
    }

    private IReadOnlyList<StartConfigurationOverviewItemViewModel> BuildAutopilotOverview(
        MediaPreflightOptions options,
        ConfigurationOverviewEvaluation overview)
    {
        string activeDescription = options.IsAutopilotConfigurationReady
            ? FormatAutopilot(options)
            : GetAutopilotValidationText(options.AutopilotConfigurationValidationCode);

        return
        (StartConfigurationOverviewItemViewModel[])
        [
            CreateOverviewItem(ConfigurationOverviewItem.AutopilotJsonProfile, overview, "Nav_AutopilotJsonProfileKey.Title",
                overview[ConfigurationOverviewItem.AutopilotJsonProfile] == ConfigurationOverviewState.NotSelected
                    ? localizationService.GetString("Nav_AutopilotJsonProfileKey.Description")
                    : activeDescription,
                ConfigurationNavigationTarget.AutopilotJsonProfile),
            CreateOverviewItem(ConfigurationOverviewItem.AutopilotZeroTouch, overview, "Nav_AutopilotZeroTouchKey.Title",
                overview[ConfigurationOverviewItem.AutopilotZeroTouch] == ConfigurationOverviewState.NotSelected
                    ? localizationService.GetString("Nav_AutopilotZeroTouchKey.Description")
                    : activeDescription,
                ConfigurationNavigationTarget.AutopilotHardwareHashUpload),
            CreateOverviewItem(ConfigurationOverviewItem.AutopilotInteractive, overview, "Nav_AutopilotInteractiveHashUploadKey.Title",
                overview[ConfigurationOverviewItem.AutopilotInteractive] == ConfigurationOverviewState.NotSelected
                    ? localizationService.GetString("Nav_AutopilotInteractiveHashUploadKey.Description")
                    : activeDescription,
                ConfigurationNavigationTarget.AutopilotInteractiveHardwareHashUpload)
        ];
    }

    private IReadOnlyList<StartConfigurationOverviewItemViewModel> BuildCustomizationOverview(
        FoundryConfigurationDocument configuration,
        MediaPreflightEvaluation mediaEvaluation,
        ConfigurationOverviewEvaluation overview)
    {
        CustomizationSettings customization = configuration.Customization;
        bool oobePasswordRequiresDeploymentProtection =
            OobeAccountConfigurationValidator.RequiresProtectedMedia(customization.Oobe) &&
            !configuration.General.DeploymentProtection.IsEnabled;
        OperatingSystemSelectionSettings os = configuration.OperatingSystemSelection;
        string osDescription = string.Join(" · ", new[]
        {
            os.DefaultLanguageCode,
            os.DefaultReleaseId,
            os.DefaultEdition
        }.Where(value => !string.IsNullOrWhiteSpace(value)));
        WindowsOptionalFeatureSettings optionalFeatures = customization.WindowsOptionalFeatures;
        int configuredFeatureCount = optionalFeatures.EnabledFeatureIds.Count + optionalFeatures.DisabledFeatureIds.Count;
        string optionalFeatureDescription = string.Format(
            localizationService.GetString("Customization.WindowsOptionalFeatures.SummaryFormat"),
            configuredFeatureCount,
            optionalFeatures.EnabledFeatureIds.Count,
            optionalFeatures.DisabledFeatureIds.Count);

        return
        (StartConfigurationOverviewItemViewModel[])
        [
            CreateOverviewItem(ConfigurationOverviewItem.OperatingSystemSelection, overview, "Nav_OsSelectionKey.Title",
                string.IsNullOrWhiteSpace(osDescription) ? localizationService.GetString("Nav_OsSelectionKey.Description") : osDescription,
                ConfigurationNavigationTarget.OperatingSystemSelection),
            CreateOverviewItem(ConfigurationOverviewItem.MachineNaming, overview, "Nav_MachineNamingKey.Title",
                customization.MachineNaming.IsEnabled
                    ? localizationService.GetString(customization.MachineNaming.Mode == MachineNamingMode.Composed
                        ? "Customization.MachineNamingComposedLabel"
                        : "Customization.MachineNamingManualLabel")
                    : localizationService.GetString("Nav_MachineNamingKey.Description"),
                ConfigurationNavigationTarget.MachineNaming),
            CreateOverviewItem(ConfigurationOverviewItem.Oobe, overview, "Nav_OobeKey.Title",
                oobePasswordRequiresDeploymentProtection
                    ? localizationService.GetString("Customization.OobeAccountPasswordDescription")
                    : customization.Oobe.IsEnabled
                    ? FormatOobeSummary(customization.Oobe)
                    : localizationService.GetString("Nav_OobeKey.Description"),
                ConfigurationNavigationTarget.Oobe),
            CreateOverviewItem(ConfigurationOverviewItem.CustomImages, overview, "Nav_CustomImagesKey.Title",
                HasReason(mediaEvaluation, MediaPreflightBlockingReason.CustomImageArchitectureMismatch)
                    ? GetBlockingReasonText(MediaPreflightBlockingReason.CustomImageArchitectureMismatch)
                    : localizationService.GetString("Nav_CustomImagesKey.Description"),
                ConfigurationNavigationTarget.CustomImages),
            CreateOverviewItem(ConfigurationOverviewItem.CustomDrivers, overview, "Nav_CustomDriversKey.Title",
                localizationService.GetString("Nav_CustomDriversKey.Description"),
                ConfigurationNavigationTarget.CustomDrivers),
            CreateOverviewItem(ConfigurationOverviewItem.PostInstallation, overview, "Nav_PostInstallationKey.Title",
                localizationService.GetString("Nav_PostInstallationKey.Description"),
                ConfigurationNavigationTarget.PostInstallation),
            CreateOverviewItem(ConfigurationOverviewItem.Unattend, overview, "Nav_UnattendKey.Title",
                localizationService.GetString("Nav_UnattendKey.Description"),
                ConfigurationNavigationTarget.Unattend),
            CreateOverviewItem(ConfigurationOverviewItem.OptionalFeatures, overview, "Nav_OptionalFeaturesKey.Title",
                optionalFeatures.IsEnabled
                    ? optionalFeatureDescription
                    : localizationService.GetString("Nav_OptionalFeaturesKey.Description"),
                ConfigurationNavigationTarget.WindowsOptionalFeatures),
            CreateOverviewItem(ConfigurationOverviewItem.AppxRemoval, overview, "Nav_AppRemovalKey.Title",
                customization.AppxRemoval.IsEnabled
                    ? localizationService.FormatString("StartOverview.AppxRemovalPackageCountFormat", customization.AppxRemoval.PackageNames.Count)
                    : localizationService.GetString("Nav_AppRemovalKey.Description"),
                ConfigurationNavigationTarget.AppxRemoval),
            CreateOverviewItem(ConfigurationOverviewItem.AiComponents, overview, "Nav_AiComponentsKey.Title",
                localizationService.GetString("Nav_AiComponentsKey.Description"),
                ConfigurationNavigationTarget.AiComponentRemoval)
        ];
    }

    private StartConfigurationOverviewItemViewModel CreateOverviewItem(
        ConfigurationOverviewItem item,
        ConfigurationOverviewEvaluation evaluation,
        string titleKey,
        string description,
        ConfigurationNavigationTarget errorNavigationTarget)
    {
        ConfigurationOverviewState state = evaluation[item];
        string title = localizationService.GetString(titleKey);
        bool needsAttention = state == ConfigurationOverviewState.NeedsAttention;
        string actionText = needsAttention
            ? localizationService.FormatString("StartMedia.Readiness.Action.ReviewFormat", title)
            : string.Empty;

        return new StartConfigurationOverviewItemViewModel(
            title,
            description,
            GetOverviewStatus(state),
            GetOverviewGlyph(state),
            GetOverviewGlyphStyleKey(state),
            state,
            needsAttention ? errorNavigationTarget : ConfigurationNavigationTarget.None,
            actionText,
            actionText);
    }

    private string GetNetworkDescription(
        ConfigurationOverviewState state,
        string configuredDescription,
        NetworkConfigurationValidationResult validation) =>
        state == ConfigurationOverviewState.NeedsAttention
            ? NetworkConfigurationValidationTextFormatter.Format(localizationService, validation)
            : configuredDescription;

    private string FormatOobeSummary(OobeSettings settings)
    {
        string diagnosticData = localizationService.GetString($"Customization.OobeDiagnosticData{settings.DiagnosticDataLevel}");
        string locationAccess = localizationService.GetString(settings.LocationAccess == OobeLocationAccessMode.ForceOff
            ? "Customization.OobeLocationForceOff"
            : "Customization.OobeLocationUserControlled");
        List<string> parts =
        [
            diagnosticData,
            locationAccess
        ];

        if (settings.EnableAdministratorAccount)
        {
            parts.Add(localizationService.GetString("StartMedia.OobeSummaryAdministratorEnabled"));
        }

        if (settings.AdditionalAccounts.Count > 0)
        {
            parts.Add(localizationService.FormatString("StartMedia.OobeSummaryAdditionalAccountsFormat", settings.AdditionalAccounts.Count));
            parts.Add(localizationService.GetString("StartMedia.OobeSummarySkipUserAccountCreation"));
        }

        return string.Join(" · ", parts);
    }

    private string GetOverviewStatus(ConfigurationOverviewState state) => state switch
    {
        ConfigurationOverviewState.Configured => localizationService.GetString("NavigationStatus.Configured"),
        ConfigurationOverviewState.Disabled => localizationService.GetString("Common.Disabled"),
        ConfigurationOverviewState.NotConfigured => localizationService.GetString("NavigationStatus.NotConfigured"),
        ConfigurationOverviewState.Default => localizationService.GetString("StartOverview.State.Default"),
        ConfigurationOverviewState.NotSelected => localizationService.GetString("StartOverview.State.NotSelected"),
        _ => localizationService.GetString("Common.NeedsAttention")
    };

    private string GetOverviewSummary(IEnumerable<StartConfigurationOverviewItemViewModel> items)
    {
        List<StartConfigurationOverviewItemViewModel> attentionItems = items
            .Where(item => item.NavigationTarget != ConfigurationNavigationTarget.None)
            .ToList();
        if (attentionItems.Count == 0)
        {
            IReadOnlyList<ConfigurationOverviewState> states = items.Select(item => item.State).ToList();
            if (states.Contains(ConfigurationOverviewState.Configured))
            {
                return localizationService.GetString("NavigationStatus.Configured");
            }

            if (states.Contains(ConfigurationOverviewState.Default))
            {
                return localizationService.GetString("StartOverview.State.Default");
            }

            if (states.All(state => state == ConfigurationOverviewState.NotSelected))
            {
                return localizationService.GetString("StartOverview.State.NotSelected");
            }

            return states.All(state => state == ConfigurationOverviewState.NotConfigured)
                ? localizationService.GetString("NavigationStatus.NotConfigured")
                : localizationService.GetString("Common.Disabled");
        }

        return attentionItems.Count > 1
            ? localizationService.FormatString("StartOverview.NeedsAttentionWithMoreFormat", attentionItems[0].Title, attentionItems.Count - 1)
            : localizationService.FormatString("StartOverview.NeedsAttentionFormat", attentionItems[0].Title);
    }

    private static bool ReplaceOverviewItems(
        ObservableCollection<StartConfigurationOverviewItemViewModel> target,
        IEnumerable<StartConfigurationOverviewItemViewModel> items)
    {
        target.Clear();
        bool hasAttentionItem = false;
        foreach (StartConfigurationOverviewItemViewModel item in items)
        {
            target.Add(item);
            hasAttentionItem |= item.NavigationTarget != ConfigurationNavigationTarget.None;
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

    private static string GetOverviewGlyph(ConfigurationOverviewState state)
    {
        return state switch
        {
            ConfigurationOverviewState.Configured or ConfigurationOverviewState.Default => "\uE8FB",
            ConfigurationOverviewState.NeedsAttention => "\uE711",
            _ => "\uE946"
        };
    }

    private static string GetOverviewGlyphStyleKey(ConfigurationOverviewState state)
    {
        return state switch
        {
            ConfigurationOverviewState.Configured or ConfigurationOverviewState.Default => "FoundryReadyFontIconStyle",
            ConfigurationOverviewState.NeedsAttention => "FoundryBlockedFontIconStyle",
            _ => "FoundryNeutralFontIconStyle"
        };
    }

    /// <summary>
    /// Resolves the text of a blocking reason. When <paramref name="autopilotValidationCode"/> is supplied,
    /// an Autopilot reason shows the same specific text as the Windows Autopilot card instead of the generic fallback.
    /// </summary>
    private string GetBlockingReasonText(
        MediaPreflightBlockingReason reason,
        AutopilotConfigurationValidationCode? autopilotValidationCode = null)
    {
        return reason == MediaPreflightBlockingReason.AutopilotConfigurationNotReady && autopilotValidationCode is { } code
            ? GetAutopilotValidationText(code)
            : localizationService.GetString($"StartMedia.BlockingReason.{reason}");
    }

    private void LogPreflightSummary(MediaPreflightOptions options, MediaPreflightEvaluation evaluation)
    {
        IReadOnlyList<MediaPreflightBlockingReason> reasons = GetGlobalBlockingReasons(evaluation);

        logger.Information(
            "Media preflight summary refreshed. Architecture={Architecture}, WinPeLanguage={WinPeLanguage}, WinPeKeyboardLayout={WinPeKeyboardLayout}, IncludeWirelessSupport={IncludeWirelessSupport}, IsoOutputPath={IsoOutputPath}, UsbTargetSelected={UsbTargetSelected}, DiskNumber={DiskNumber}, DiskName={DiskName}, NetworkReady={NetworkReady}, DeployReady={DeployReady}, ConnectReady={ConnectReady}, SecretsReady={SecretsReady}, AutopilotEnabled={AutopilotEnabled}, AutopilotReady={AutopilotReady}, HasAutopilotProfile={HasAutopilotProfile}, HasAutopilotFolder={HasAutopilotFolder}, SummaryReady={SummaryReady}, BlockingReasons={BlockingReasons}",
            options.Architecture,
            NormalizeCultureName(options.WinPeLanguage),
            options.WinPeKeyboardLayoutId ?? "default",
            options.IncludeWirelessSupport,
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
            !string.IsNullOrWhiteSpace(options.AutopilotProfileDisplayName),
            !string.IsNullOrWhiteSpace(options.AutopilotProfileFolderName),
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
        if (!adkService.CurrentStatus.CanCreateMediaFor(SelectedArchitecture?.Value ?? WinPeArchitecture.X64))
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
            ?? foundryConfigurationStateService.Current.General.UsbFormatMode;

        FormatModes.Clear();
        FormatModes.Add(new(UsbFormatMode.Quick, localizationService.GetString("StartMedia.FormatMode.Quick")));
        FormatModes.Add(new(UsbFormatMode.Complete, localizationService.GetString("StartMedia.FormatMode.Complete")));
        SelectedFormatMode = SelectOption(FormatModes, selectedValue);
    }

    private void RebuildPartitionStyles()
    {
        UsbPartitionStyle selectedValue = SelectedArchitecture?.Value == WinPeArchitecture.Arm64
            ? UsbPartitionStyle.Gpt
            : SelectedPartitionStyle?.Value ?? foundryConfigurationStateService.Current.General.UsbPartitionStyle;

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
                ByteSizeFormatter.Format(localizationService, candidate.SizeBytes)));
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
            return GetAutopilotValidationText(options.AutopilotConfigurationValidationCode);
        }

        if (options.AutopilotProvisioningMode == AutopilotProvisioningMode.HardwareHashUpload)
        {
            return localizationService.GetString("StartMedia.Autopilot.HardwareHashUpload");
        }

        if (options.AutopilotProvisioningMode == AutopilotProvisioningMode.InteractiveHardwareHashUpload)
        {
            return localizationService.GetString("StartMedia.Autopilot.InteractiveHardwareHashUpload");
        }

        return string.Format(
            localizationService.GetString("StartMedia.Autopilot.ProfileFormat"),
            FormatValue(options.AutopilotProfileDisplayName),
            FormatValue(options.AutopilotProfileFolderName));
    }

    private string GetAutopilotValidationText(AutopilotConfigurationValidationCode code)
    {
        string key = $"StartMedia.Autopilot.Validation.{code}";
        string value = localizationService.GetString(key);
        return string.Equals(value, key, StringComparison.Ordinal)
            ? GetBlockingReasonText(MediaPreflightBlockingReason.AutopilotConfigurationNotReady)
            : value;
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

    private string FormatDriverVendor(WinPeVendorSelection vendor)
    {
        return localizationService.GetString($"StartMedia.DriverVendor.{vendor}");
    }

    private static bool IsIsoOutputLocked(WinPeDiagnostic diagnostic)
    {
        return diagnostic.Stage == WinPeIsoMediaService.FinalizeOutputStage
            && diagnostic.FailureReason == WinPeFailureReasons.AccessDenied;
    }

    /// <summary>
    /// Identifies WinPE workspace or image servicing failures where Windows denied access to image files,
    /// usually because security software or another imaging tool holds them.
    /// </summary>
    private static bool IsWinPeImageAccessBlocked(WinPeDiagnostic diagnostic)
    {
        return diagnostic.FailureReason == WinPeFailureReasons.AccessDenied &&
            diagnostic.Code is WinPeErrorCodes.BuildFailed or WinPeErrorCodes.WimMountFailed;
    }

    private string FormatWinPeImageAccessBlockedFailure()
    {
        string dismLogPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            "Logs",
            "DISM",
            "dism.log");
        return string.Format(
            CultureInfo.CurrentCulture,
            localizationService.GetString("StartMedia.Operation.WinPeImageAccessBlocked"),
            Constants.RootDirectoryPath,
            Constants.LogFilePath,
            dismLogPath);
    }

    private string FormatMediaCapacityFailure(string resourceKey, WinPeDiagnostic diagnostic, string? storageVolume = null)
    {
        string required = diagnostic.RequiredBytes is ulong requiredBytes ? ByteSizeFormatter.Format(localizationService, requiredBytes) : "—";
        string available = diagnostic.AvailableBytes is ulong availableBytes ? ByteSizeFormatter.Format(localizationService, availableBytes) : "—";
        // Near a limit, rounded sizes can look identical even though validation correctly rejects the payload.
        if (required == available && diagnostic.RequiredBytes > diagnostic.AvailableBytes)
        {
            string unit = localizationService.GetString("StartMedia.ByteUnit.B");
            required = $"{diagnostic.RequiredBytes:N0} {unit}";
            available = $"{diagnostic.AvailableBytes:N0} {unit}";
        }
        return string.Format(
            CultureInfo.CurrentCulture,
            localizationService.GetString(resourceKey),
            required,
            available,
            storageVolume);
    }

    private WinPeToolPaths ResolveWinPeToolsOrThrow()
    {
        WinPeResult<WinPeToolPaths> result = new WinPeToolResolver().ResolveTools(adkService.CurrentStatus.KitsRootPath);
        EnsureSuccess(result);
        return result.Value!;
    }

    private bool IsWirelessSupportRequested()
    {
        NetworkSettings network = foundryConfigurationStateService.Current.Network;
        return network.WifiProvisioned || network.Wifi.IsEnabled;
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

    private static WinPeRuntimePayloadProvisioningOptions AddReleaseRuntimeProvisioning(
        WinPeRuntimePayloadProvisioningOptions options)
    {
        return options with
        {
            Bootstrap = options.Bootstrap.IsEnabled ? options.Bootstrap : new WinPeRuntimePayloadApplicationOptions
            {
                IsEnabled = true,
                ProvisioningSource = WinPeProvisioningSource.Release
            },
            Connect = options.Connect.IsEnabled ? options.Connect : new WinPeRuntimePayloadApplicationOptions
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

        throw new WinPeOperationException(result.Error);
    }

    private sealed class WinPeOperationException(WinPeDiagnostic? diagnostic)
        : InvalidOperationException(FormatWinPeError(diagnostic), diagnostic?.Exception)
    {
        public WinPeDiagnostic? Diagnostic { get; } = diagnostic;
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

    private static SelectionOption<T>? SelectOption<T>(IEnumerable<SelectionOption<T>> options, T value)
    {
        return options.FirstOrDefault(option => EqualityComparer<T>.Default.Equals(option.Value, value));
    }

    private enum FinalMediaTarget
    {
        Iso,
        Usb,
        UsbUpdate,
        Pxe
    }

    private sealed record PreparedMediaWorkspace(
        WinPeWorkspaceLease Lease,
        WinPeWorkspacePreparationResult PreparedWorkspace,
        WinPeToolPaths Tools,
        WinPeRuntimePayloadProvisioningOptions RuntimePayloadProvisioning,
        WinPeCustomImageMediaLease? CustomImages,
        WinPeContentPackageMediaLease PostInstallation,
        WinPeContentPackageMediaLease? CustomDrivers,
        string DeployConfigurationJson);
}
