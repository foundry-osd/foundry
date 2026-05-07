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
using Serilog;

namespace Foundry.ViewModels;

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
    private readonly IExpertDeployConfigurationStateService expertDeployConfigurationStateService;
    private readonly IOperationProgressService operationProgressService;
    private readonly IShellNavigationGuardService shellNavigationGuardService;
    private readonly IDialogService dialogService;
    private readonly IApplicationLocalizationService localizationService;
    private readonly IAppDispatcher appDispatcher;
    private readonly ILogger logger;
    private IReadOnlyList<string> availableWinPeLanguages = [];

    public StartMediaViewModel(
        IAppSettingsService appSettingsService,
        IAdkService adkService,
        IWinPeLanguageDiscoveryService languageDiscoveryService,
        IWinPeEmbeddedAssetService embeddedAssetService,
        IWinPeBuildService buildService,
        IWinPeWorkspacePreparationService workspacePreparationService,
        IWinPeIsoMediaService isoMediaService,
        IWinPeUsbMediaService usbMediaService,
        IExpertDeployConfigurationStateService expertDeployConfigurationStateService,
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
        this.expertDeployConfigurationStateService = expertDeployConfigurationStateService;
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
        PartitionStyles =
        [
            new(UsbPartitionStyle.Gpt, "GPT"),
            new(UsbPartitionStyle.Mbr, "MBR")
        ];
        FormatModes = [];

        IsoOutputPath = appSettingsService.Current.Media.IsoOutputPath;
        SelectedArchitecture = SelectOption(Architectures, ParseEnum(appSettingsService.Current.Media.Architecture, WinPeArchitecture.X64));
        UseCa2023Signature = appSettingsService.Current.Media.UseCa2023Signature;
        SelectedPartitionStyle = SelectOption(PartitionStyles, ParseEnum(appSettingsService.Current.Media.UsbPartitionStyle, UsbPartitionStyle.Gpt));
        IncludeDellDrivers = appSettingsService.Current.Media.IncludeDellDrivers;
        IncludeHpDrivers = appSettingsService.Current.Media.IncludeHpDrivers;
        CustomDriverDirectoryPath = appSettingsService.Current.Media.CustomDriverDirectoryPath ?? string.Empty;

        adkService.StatusChanged += OnAdkStatusChanged;
        expertDeployConfigurationStateService.StateChanged += OnExpertDeployConfigurationStateChanged;
        localizationService.LanguageChanged += OnLanguageChanged;

        ApplyLocalizedText();
        RefreshEvaluation();
    }

    public ObservableCollection<SelectionOption<WinPeArchitecture>> Architectures { get; }
    public ObservableCollection<SelectionOption<UsbPartitionStyle>> PartitionStyles { get; }
    public ObservableCollection<SelectionOption<UsbFormatMode>> FormatModes { get; }
    public ObservableCollection<SelectionOption<WinPeUsbDiskCandidate>> UsbCandidates { get; } = [];

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

    public void Dispose()
    {
        adkService.StatusChanged -= OnAdkStatusChanged;
        expertDeployConfigurationStateService.StateChanged -= OnExpertDeployConfigurationStateChanged;
        localizationService.LanguageChanged -= OnLanguageChanged;
    }

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
    private async Task RefreshUsbCandidatesAsync()
    {
        if (!adkService.CurrentStatus.CanCreateMedia)
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
                UsbCandidateStatus = string.Format(localizationService.GetString("StartMedia.Usb.CandidatesFound"), UsbCandidates.Count);
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
        shellNavigationGuardService.SetState(ShellNavigationState.OperationRunning);
        string terminalStatus = string.Empty;
        string? failureMessage = null;

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
                await CreateIsoMediaAsync(options, CancellationToken.None);
            }
            else
            {
                await CreateUsbMediaAsync(options, CancellationToken.None);
            }

            terminalStatus = localizationService.GetString("StartMedia.Operation.Completed");
            operationProgressService.Complete(terminalStatus);
            logger.Information("Final media creation completed. Target={Target}", target);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            terminalStatus = localizationService.GetString("StartMedia.Operation.Failed");
            operationProgressService.Report(100, terminalStatus);
            failureMessage = ex.Message;
            logger.Error(ex, "Final media creation failed. Target={Target}", target);
        }
        finally
        {
            shellNavigationGuardService.SetState(adkService.CurrentStatus.CanCreateMedia
                ? ShellNavigationState.Ready
                : ShellNavigationState.AdkBlocked);
            operationProgressService.Reset(terminalStatus);
            IsMediaOperationRunning = false;
            RefreshEvaluation();
        }

        if (!string.IsNullOrWhiteSpace(failureMessage))
        {
            await dialogService.ShowMessageAsync(new DialogRequest(
                localizationService.GetString("StartMedia.Operation.FailedTitle"),
                failureMessage,
                localizationService.GetString("Common.Close")));
        }
    }

    private async Task CreateIsoMediaAsync(MediaPreflightOptions options, CancellationToken cancellationToken)
    {
        PreparedMediaWorkspace workspace = await PrepareMediaWorkspaceAsync(
            options,
            includeRuntimePayloadInImage: true,
            cancellationToken);

        operationProgressService.Report(90, localizationService.GetString("StartMedia.Operation.CreatingIso"));
        WinPeResult result = await isoMediaService.CreateAsync(
            new WinPeIsoMediaOptions
            {
                PreparedWorkspace = workspace.PreparedWorkspace,
                OutputIsoPath = options.IsoOutputPath,
                IsoTempDirectoryPath = Path.Combine(Constants.TempDirectoryPath, "Iso")
            },
            cancellationToken);

        EnsureSuccess(result);
    }

    private async Task CreateUsbMediaAsync(MediaPreflightOptions options, CancellationToken cancellationToken)
    {
        if (options.SelectedUsbDisk is null)
        {
            throw new InvalidOperationException(localizationService.GetString("StartMedia.BlockingReason.NoUsbTarget"));
        }

        PreparedMediaWorkspace workspace = await PrepareMediaWorkspaceAsync(
            options,
            includeRuntimePayloadInImage: false,
            cancellationToken);

        WinPeUsbDiskCandidate selectedDisk = options.SelectedUsbDisk;
        operationProgressService.Report(90, localizationService.GetString("StartMedia.Operation.CreatingUsb"));
        WinPeResult<WinPeUsbProvisionResult> result = await usbMediaService.ProvisionAndPopulateAsync(
            new UsbOutputOptions
            {
                TargetDiskNumber = selectedDisk.DiskNumber,
                ExpectedDiskFriendlyName = selectedDisk.FriendlyName,
                ExpectedDiskSerialNumber = selectedDisk.SerialNumber,
                ExpectedDiskUniqueId = selectedDisk.UniqueId,
                PartitionStyle = options.UsbPartitionStyle,
                FormatMode = options.UsbFormatMode,
                RuntimePayloadProvisioning = workspace.RuntimePayloadProvisioning
            },
            workspace.PreparedWorkspace.Artifact,
            workspace.Tools,
            workspace.PreparedWorkspace.UseBootEx,
            cancellationToken);

        EnsureSuccess(result);
    }

    private async Task<PreparedMediaWorkspace> PrepareMediaWorkspaceAsync(
        MediaPreflightOptions options,
        bool includeRuntimePayloadInImage,
        CancellationToken cancellationToken)
    {
        WinPeToolPaths tools = ResolveWinPeToolsOrThrow();
        WinPeRuntimePayloadProvisioningOptions runtimePayloadProvisioning = CreateRuntimePayloadProvisioningOptions(
            options.Architecture,
            Constants.WinPeWorkspaceDirectoryPath,
            Constants.WinPeWorkspaceDirectoryPath,
            Constants.WinPeWorkspaceDirectoryPath);

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

        WinPeBuildArtifact artifact = buildResult.Value!;
        FoundryConnectProvisioningBundle connectBundle = expertDeployConfigurationStateService.GenerateConnectProvisioningBundle(
            Path.Combine(artifact.WorkingDirectoryPath, "Provisioning"));

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
                AssetProvisioning = CreateAssetProvisioningOptions(options, connectBundle),
                RuntimePayloadProvisioning = includeRuntimePayloadInImage ? artifactRuntimePayloadProvisioning : null,
                WinReCacheDirectoryPath = Constants.WinReTempDirectoryPath,
                Progress = new Progress<WinPeWorkspacePreparationStage>(ReportWorkspacePreparationStage),
                CustomizationProgress = new Progress<WinPeMountedImageCustomizationProgress>(ReportCustomizationProgress)
            },
            cancellationToken);
        EnsureSuccess(preparationResult);

        return new PreparedMediaWorkspace(
            preparationResult.Value!,
            tools,
            artifactRuntimePayloadProvisioning with
            {
                MountedImagePath = string.Empty,
                UsbCacheRootPath = string.Empty
            });
    }

    private WinPeMountedImageAssetProvisioningOptions CreateAssetProvisioningOptions(
        MediaPreflightOptions options,
        FoundryConnectProvisioningBundle connectBundle)
    {
        return new WinPeMountedImageAssetProvisioningOptions
        {
            BootstrapScriptContent = embeddedAssetService.GetBootstrapScriptContent(),
            CurlExecutableSourcePath = ResolveCurlExecutablePath(),
            SevenZipSourceDirectoryPath = embeddedAssetService.GetSevenZipSourceDirectoryPath(),
            IanaWindowsTimeZoneMapJson = embeddedAssetService.GetIanaWindowsTimeZoneMapJson(),
            FoundryConnectConfigurationJson = connectBundle.ConfigurationJson,
            ExpertDeployConfigurationJson = expertDeployConfigurationStateService.GenerateDeployConfigurationJson(),
            MediaSecretsKey = connectBundle.MediaSecretsKey,
            FoundryConnectAssetFiles = connectBundle.AssetFiles,
            AutopilotProfiles = options.IsAutopilotEnabled
                ? expertDeployConfigurationStateService.Current.Autopilot.Profiles
                : [],
            ConnectProvisioningSource = WinPeProvisioningSource.Local,
            DeployProvisioningSource = WinPeProvisioningSource.Local
        };
    }

    private void ReportWorkspacePreparationStage(WinPeWorkspacePreparationStage stage)
    {
        int progress = stage switch
        {
            WinPeWorkspacePreparationStage.ResolvingDrivers => 35,
            WinPeWorkspacePreparationStage.CustomizingImage => 45,
            WinPeWorkspacePreparationStage.EvaluatingSignaturePolicy => 85,
            _ => 45
        };

        operationProgressService.Report(progress, localizationService.GetString("StartMedia.Operation.PreparingWorkspace"));
    }

    private void ReportCustomizationProgress(WinPeMountedImageCustomizationProgress progress)
    {
        int normalizedProgress = Math.Clamp(45 + (progress.Percent * 35 / 100), 45, 80);
        string status = string.IsNullOrWhiteSpace(progress.Status)
            ? localizationService.GetString("StartMedia.Operation.PreparingWorkspace")
            : progress.Status;

        operationProgressService.Report(normalizedProgress, status);
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
        LoadConfigurationFromSettings();
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
        StatusSummary = BuildStatusText(evaluation);
        GlobalSummary = BuildGlobalSummary(options, evaluation);

    }

    private void LoadConfigurationFromSettings()
    {
        IsoOutputPath = appSettingsService.Current.Media.IsoOutputPath;
        SelectedArchitecture = SelectOption(Architectures, ParseEnum(appSettingsService.Current.Media.Architecture, WinPeArchitecture.X64));
        UseCa2023Signature = appSettingsService.Current.Media.UseCa2023Signature;
        SelectedPartitionStyle = SelectOption(PartitionStyles, ParseEnum(appSettingsService.Current.Media.UsbPartitionStyle, UsbPartitionStyle.Gpt));
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

        RuntimePayloadReadiness runtimeReadiness = EvaluateRuntimePayloadReadiness(
            CreateRuntimePayloadProvisioningOptions(
                SelectedArchitecture?.Value ?? WinPeArchitecture.X64,
                Constants.WinPeWorkspaceDirectoryPath,
                Constants.WinPeWorkspaceDirectoryPath,
                Constants.WinPeWorkspaceDirectoryPath));

        return new MediaPreflightOptions
        {
            IsAdkReady = adkService.CurrentStatus.CanCreateMedia,
            IsRuntimePayloadReady = runtimeReadiness.IsReady,
            IsConnectRuntimePayloadReady = runtimeReadiness.IsConnectReady,
            IsDeployRuntimePayloadReady = runtimeReadiness.IsDeployReady,
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
            BootImageSource = WinPeBootImageSource.WinPe,
            DriverVendors = vendors,
            CustomDriverDirectoryPath = CustomDriverDirectoryPath,
            SelectedUsbDisk = SelectedUsbDisk?.Value
        };
    }

    private string BuildStatusText(MediaPreflightEvaluation evaluation)
    {
        IReadOnlyList<MediaPreflightBlockingReason> reasons = GetGlobalBlockingReasons(evaluation);

        return string.Format(
            localizationService.GetString("StartMedia.Status"),
            FormatReady(evaluation.CanGenerateIsoSummary || evaluation.CanGenerateUsbSummary),
            reasons.Count);
    }

    private string BuildGlobalSummary(MediaPreflightOptions options, MediaPreflightEvaluation evaluation)
    {
        IReadOnlyList<MediaPreflightBlockingReason> reasons = GetGlobalBlockingReasons(evaluation);
        var builder = new StringBuilder();
        builder.AppendLine($"{localizationService.GetString("StartMedia.Field.Adk")}: {FormatReady(adkService.CurrentStatus.CanCreateMedia)}");
        builder.AppendLine($"{localizationService.GetString("StartMedia.Field.WinPeLanguage")}: {FormatValue(NormalizeCultureName(options.WinPeLanguage))}");
        builder.AppendLine($"{localizationService.GetString("StartMedia.Field.Architecture")}: {FormatArchitecture(options.Architecture)}");
        builder.AppendLine($"{localizationService.GetString("StartMedia.Field.IsoPath")}: {FormatValue(options.IsoOutputPath)}");
        builder.AppendLine($"{localizationService.GetString("StartMedia.Field.UsbTarget")}: {FormatUsbCandidate(options.SelectedUsbDisk)}");
        builder.AppendLine($"{localizationService.GetString("StartMedia.Field.Signature")}: {FormatSignatureMode(options.SignatureMode)}");
        builder.AppendLine($"{localizationService.GetString("StartMedia.Field.PartitionStyle")}: {evaluation.EffectiveUsbPartitionStyle}");
        builder.AppendLine($"{localizationService.GetString("StartMedia.Field.FormatMode")}: {FormatUsbFormatMode(options.UsbFormatMode)}");
        builder.AppendLine($"{localizationService.GetString("StartMedia.Field.Drivers")}: {FormatDriverOptions(options.DriverVendors, options.CustomDriverDirectoryPath)}");
        builder.AppendLine($"{localizationService.GetString("StartMedia.Field.Runtime")}: {FormatReady(options.IsRuntimePayloadReady)}");
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
            "Media preflight summary refreshed. Architecture={Architecture}, WinPeLanguage={WinPeLanguage}, BootImageSource={BootImageSource}, IsoOutputPath={IsoOutputPath}, UsbTargetSelected={UsbTargetSelected}, DiskNumber={DiskNumber}, DiskName={DiskName}, RuntimeReady={RuntimeReady}, NetworkReady={NetworkReady}, DeployReady={DeployReady}, ConnectReady={ConnectReady}, SecretsReady={SecretsReady}, AutopilotEnabled={AutopilotEnabled}, AutopilotReady={AutopilotReady}, AutopilotProfile={AutopilotProfile}, AutopilotFolder={AutopilotFolder}, SummaryReady={SummaryReady}, BlockingReasons={BlockingReasons}",
            options.Architecture,
            NormalizeCultureName(options.WinPeLanguage),
            options.BootImageSource,
            options.IsoOutputPath,
            options.SelectedUsbDisk is not null,
            options.SelectedUsbDisk?.DiskNumber,
            options.SelectedUsbDisk?.FriendlyName,
            options.IsRuntimePayloadReady,
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

    private static RuntimePayloadReadiness EvaluateRuntimePayloadReadiness(WinPeRuntimePayloadProvisioningOptions options)
    {
        bool connectReady = IsRuntimeApplicationReady(options.Connect);
        bool deployReady = IsRuntimeApplicationReady(options.Deploy);
        return new RuntimePayloadReadiness(connectReady, deployReady);
    }

    private static bool IsRuntimeApplicationReady(WinPeRuntimePayloadApplicationOptions options)
    {
        if (!options.IsEnabled)
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(options.ArchivePath))
        {
            return File.Exists(options.ArchivePath);
        }

        return !string.IsNullOrWhiteSpace(options.ProjectPath) && File.Exists(options.ProjectPath);
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

    private sealed record RuntimePayloadReadiness(bool IsConnectReady, bool IsDeployReady)
    {
        public bool IsReady => IsConnectReady && IsDeployReady;
    }

    private sealed record PreparedMediaWorkspace(
        WinPeWorkspacePreparationResult PreparedWorkspace,
        WinPeToolPaths Tools,
        WinPeRuntimePayloadProvisioningOptions RuntimePayloadProvisioning);
}
