using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Foundry.Deploy;
using Foundry.Deploy.Models.Configuration;
using Foundry.Deploy.Models;
using Foundry.Deploy.Services.Catalog;
using Foundry.Deploy.Services.Configuration;
using Foundry.Deploy.Services.Deployment;
using Foundry.Deploy.Services.DriverPacks;
using Foundry.Deploy.Services.Hardware;
using Foundry.Deploy.Services.Operations;
using Foundry.Deploy.Services.Runtime;
using Foundry.Deploy.Services.System;
using Foundry.Deploy.Services.Theme;
using Foundry.Deploy.Validation;
using Microsoft.Extensions.Logging;
using DeployThemeMode = Foundry.Deploy.Services.Theme.ThemeMode;

namespace Foundry.Deploy.ViewModels;

public partial class MainWindowViewModel : ObservableObject, IDisposable
{
    private const string WinPeTransientRuntimeRoot = @"X:\Foundry\Runtime";
    private static readonly string AppVersion = ResolveAppVersion();
    private readonly IThemeService _themeService;
    private readonly IExpertDeployConfigurationService _expertDeployConfigurationService;
    private readonly IOperatingSystemCatalogService _operatingSystemCatalogService;
    private readonly IDriverPackCatalogService _driverPackCatalogService;
    private readonly IDeploymentLaunchPreparationService _deploymentLaunchPreparationService;
    private readonly IDeploymentOrchestrator _deploymentOrchestrator;
    private readonly ILogger<MainWindowViewModel> _logger;
    private readonly Dispatcher _dispatcher;
    private readonly DeploymentRuntimeContext _deploymentRuntimeContext;
    private HardwareProfile? _detectedHardware;
    private bool _isInitialized;
    private bool _isDisposed;
    private Task? _initializationTask;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PreviousWizardStepCommand))]
    [NotifyCanExecuteChangedFor(nameof(NextWizardStepCommand))]
    [NotifyCanExecuteChangedFor(nameof(StartDeploymentCommand))]
    private int wizardStepIndex;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RefreshCatalogsCommand))]
    [NotifyCanExecuteChangedFor(nameof(NextWizardStepCommand))]
    private bool isCatalogLoading;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PreviousWizardStepCommand))]
    [NotifyCanExecuteChangedFor(nameof(NextWizardStepCommand))]
    [NotifyCanExecuteChangedFor(nameof(StartDeploymentCommand))]
    [NotifyCanExecuteChangedFor(nameof(ShowDebugProgressPageCommand))]
    [NotifyCanExecuteChangedFor(nameof(ShowDebugSuccessPageCommand))]
    [NotifyCanExecuteChangedFor(nameof(ShowDebugErrorPageCommand))]
    private bool isDeploymentRunning;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartDeploymentCommand))]
    private bool useFullAutopilot = true;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartDeploymentCommand))]
    private bool allowAutopilotDeferredCompletion = true;

    public DeploymentPreparationViewModel Preparation { get; }
    public DeploymentSessionViewModel Session { get; }
    public OperatingSystemCatalogViewModel OperatingSystemCatalog { get; }
    public DriverPackSelectionViewModel DriverPackSelection { get; }

    public DeployThemeMode CurrentTheme => _themeService.CurrentTheme;
    public bool IsDebugSafeMode => DebugSafetyMode.IsEnabled;
    public string EffectiveOsArchitecture => OperatingSystemCatalog.EffectiveOsArchitecture;
    public OperatingSystemCatalogItem? SelectedOperatingSystem => OperatingSystemCatalog.SelectedOperatingSystem;
    public string VersionDisplay => $"Version: {AppVersion}";

    private static string ResolveAppVersion()
    {
        Assembly assembly = typeof(MainWindowViewModel).Assembly;
        string? informationalVersion = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;

        if (!string.IsNullOrWhiteSpace(informationalVersion))
        {
            return informationalVersion.Trim();
        }

        return assembly.GetName().Version?.ToString() ?? "0.0.0.0";
    }

    public MainWindowViewModel(
        IThemeService themeService,
        IOperationProgressService operationProgressService,
        IExpertDeployConfigurationService expertDeployConfigurationService,
        IDeploymentRuntimeContextService deploymentRuntimeContextService,
        IOperatingSystemCatalogService operatingSystemCatalogService,
        IDriverPackCatalogService driverPackCatalogService,
        IDeploymentLaunchPreparationService deploymentLaunchPreparationService,
        IDeploymentOrchestrator deploymentOrchestrator,
        IHardwareProfileService hardwareProfileService,
        IOfflineWindowsComputerNameService offlineWindowsComputerNameService,
        ITargetDiskService targetDiskService,
        IDriverPackSelectionService driverPackSelectionService,
        IProcessRunner processRunner,
        ILogger<MainWindowViewModel> logger)
    {
        _themeService = themeService;
        _expertDeployConfigurationService = expertDeployConfigurationService;
        _operatingSystemCatalogService = operatingSystemCatalogService;
        _driverPackCatalogService = driverPackCatalogService;
        _deploymentLaunchPreparationService = deploymentLaunchPreparationService;
        _deploymentOrchestrator = deploymentOrchestrator;
        _logger = logger;
        _dispatcher = Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;
        _deploymentRuntimeContext = deploymentRuntimeContextService.Resolve();
        Preparation = new DeploymentPreparationViewModel(
            targetDiskService,
            hardwareProfileService,
            offlineWindowsComputerNameService,
            _logger,
            IsDebugSafeMode);
        Preparation.StateChanged += OnPreparationStateChanged;
        Preparation.StatusMessageGenerated += OnPreparationStatusMessageGenerated;
        OperatingSystemCatalog = new OperatingSystemCatalogViewModel(
            _logger,
            Environment.GetEnvironmentVariable("PROCESSOR_ARCHITECTURE") ?? string.Empty);
        OperatingSystemCatalog.StateChanged += OnOperatingSystemCatalogStateChanged;
        DriverPackSelection = new DriverPackSelectionViewModel(
            driverPackSelectionService,
            OperatingSystemCatalog.EffectiveOsArchitecture);
        DriverPackSelection.StateChanged += OnDriverPackSelectionStateChanged;
        Session = new DeploymentSessionViewModel(
            _dispatcher,
            _logger,
            operationProgressService,
            _deploymentOrchestrator,
            processRunner,
            IsDebugSafeMode);
    }

    public Task InitializeAsync()
    {
        return _initializationTask ??= InitializeCoreAsync();
    }

    private async Task InitializeCoreAsync()
    {
        if (_isInitialized)
        {
            return;
        }

        LoadExpertDeployConfiguration();
        EnsureCachePathForMode();

        if (IsDebugSafeMode)
        {
            Session.SetStatus("Debug Safe Mode enabled: deployment actions are simulated.");
        }

        await Task.WhenAll(
                LoadOfflineComputerNameAsync(),
                LoadHardwareProfileAsync(),
                Preparation.RefreshTargetDisksCommand.ExecuteAsync(null),
                RefreshCatalogsAsync())
            .ConfigureAwait(false);

        _isInitialized = true;
    }

    private void LoadExpertDeployConfiguration()
    {
        ExpertDeployConfigurationLoadResult loadResult = _expertDeployConfigurationService.LoadOptional();
        if (loadResult.Document is null)
        {
            return;
        }

        ApplyExpertDeployConfiguration(loadResult.Document);
    }

    private void ApplyExpertDeployConfiguration(FoundryDeployConfigurationDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        OperatingSystemCatalog.ApplyExpertLocalization(
            document.Localization.VisibleLanguageCodes,
            document.Localization.DefaultLanguageCodeOverride,
            document.Localization.ForceSingleVisibleLanguage);
        Preparation.ApplyMachineNamingConfiguration(
            document.Customization.MachineNaming ?? new DeployMachineNamingSettings(),
            string.IsNullOrWhiteSpace(Preparation.TargetComputerName)
            ? ResolveInitialComputerName()
            : Preparation.TargetComputerName);
    }

    [RelayCommand]
    private void SetSystemTheme()
    {
        _themeService.SetTheme(DeployThemeMode.System);
        OnPropertyChanged(nameof(CurrentTheme));
    }

    [RelayCommand]
    private void SetLightTheme()
    {
        _themeService.SetTheme(DeployThemeMode.Light);
        OnPropertyChanged(nameof(CurrentTheme));
    }

    [RelayCommand]
    private void SetDarkTheme()
    {
        _themeService.SetTheme(DeployThemeMode.Dark);
        OnPropertyChanged(nameof(CurrentTheme));
    }

    [RelayCommand(CanExecute = nameof(CanRefreshCatalogs))]
    private async Task RefreshCatalogsAsync()
    {
        _logger.LogInformation("Refreshing deployment catalogs.");
        if (IsCatalogLoading)
        {
            return;
        }

        IsCatalogLoading = true;
        Session.SetStatus("Loading catalogs...");

        try
        {
            IReadOnlyList<OperatingSystemCatalogItem> operatingSystems =
                await _operatingSystemCatalogService.GetCatalogAsync().ConfigureAwait(false);
            IReadOnlyList<DriverPackCatalogItem> driverPacks =
                await _driverPackCatalogService.GetCatalogAsync().ConfigureAwait(false);

            RunOnUi(() =>
            {
                OperatingSystemCatalog.ApplyCatalog(operatingSystems);
                DriverPackSelection.ReplaceCatalog(driverPacks);
                RefreshDriverPackSelectionContext();
                Session.SetStatus($"Catalogs loaded: {OperatingSystemCatalog.OperatingSystems.Count} OS entries, {DriverPackSelection.CatalogCount} driver packs.");
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Catalog refresh failed.");
            RunOnUi(() => Session.SetStatus($"Catalog load failed: {ex.Message}"));
        }
        finally
        {
            RunOnUi(() => IsCatalogLoading = false);
        }
    }

    [RelayCommand(CanExecute = nameof(CanGoPrevious))]
    private void PreviousWizardStep()
    {
        if (WizardStepIndex > 0)
        {
            WizardStepIndex--;
        }
    }

    [RelayCommand(CanExecute = nameof(CanGoNext))]
    private void NextWizardStep()
    {
        if (WizardStepIndex < 3)
        {
            WizardStepIndex++;
        }
    }

    [RelayCommand(CanExecute = nameof(CanStartDeployment))]
    private async Task StartDeploymentAsync()
    {
        _logger.LogInformation("Start deployment requested.");
        EnsureCachePathForMode();
        DriverPackSelectionKind effectiveDriverPackKind = DriverPackSelection.EffectiveSelectionKind;
        DriverPackCatalogItem? effectiveDriverPack = DriverPackSelection.ResolveEffectiveSelection();
        DeploymentLaunchPreparationResult launchPreparation = _deploymentLaunchPreparationService.Prepare(
            new DeploymentLaunchRequest
            {
                Mode = _deploymentRuntimeContext.Mode,
                CacheRootPath = Preparation.CacheRootPath,
                TargetComputerName = Preparation.TargetComputerName,
                SelectedTargetDisk = Preparation.SelectedTargetDisk,
                SelectedOperatingSystem = OperatingSystemCatalog.SelectedOperatingSystem,
                DriverPackSelectionKind = effectiveDriverPackKind,
                SelectedDriverPack = effectiveDriverPack,
                ApplyFirmwareUpdates = Preparation.ApplyFirmwareUpdates,
                UseFullAutopilot = UseFullAutopilot,
                AllowAutopilotDeferredCompletion = AllowAutopilotDeferredCompletion,
                IsDryRun = IsDebugSafeMode
            });

        if (!string.Equals(Preparation.TargetComputerName, launchPreparation.NormalizedComputerName, StringComparison.Ordinal))
        {
            Preparation.TargetComputerName = launchPreparation.NormalizedComputerName;
        }

        if (!launchPreparation.IsReadyToStart || launchPreparation.Context is null)
        {
            Session.SetStatus(launchPreparation.StatusMessage);
            return;
        }

        RunOnUi(() =>
        {
            IsDeploymentRunning = true;
            Session.BeginDeployment(launchPreparation.NormalizedComputerName, _deploymentOrchestrator.PlannedSteps.Count);
        });

        try
        {
            DeploymentResult result = await _deploymentOrchestrator
                .RunAsync(launchPreparation.Context)
                .ConfigureAwait(false);

            RunOnUi(() =>
            {
                if (result.IsSuccess)
                {
                    Session.CompleteDeployment("Deployment completed.", result.LogsDirectoryPath);
                    return;
                }

                string fallbackStep = string.IsNullOrWhiteSpace(Session.FailedStepName)
                    ? Session.CurrentStepName
                    : Session.FailedStepName;
                string fallbackMessage = string.IsNullOrWhiteSpace(Session.FailedStepErrorMessage)
                    ? result.Message
                    : Session.FailedStepErrorMessage;
                Session.FailDeployment($"Deployment failed: {result.Message}", fallbackStep, fallbackMessage, result.LogsDirectoryPath);
            });
            _logger.LogInformation("Deployment run completed. IsSuccess={IsSuccess}, LogsDirectoryPath={LogsDirectoryPath}",
                result.IsSuccess,
                result.LogsDirectoryPath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Deployment execution failed in view model.");
            RunOnUi(() =>
            {
                Session.FailDeployment($"Deployment failed: {ex.Message}", Session.CurrentStepName, ex.Message);
            });
        }
        finally
        {
            RunOnUi(() =>
            {
                IsDeploymentRunning = false;
            });
        }
    }

    [RelayCommand(CanExecute = nameof(CanShowDebugPages))]
    private void ShowDebugProgressPage()
    {
        Session.ShowDebugProgress(
            Preparation.TargetComputerName,
            currentStepIndex: 7,
            plannedStepCount: _deploymentOrchestrator.PlannedSteps.Count,
            currentStepName: DeploymentStepNames.ApplyOperatingSystemImage,
            progressPercent: 42);
    }

    [RelayCommand(CanExecute = nameof(CanShowDebugPages))]
    private void ShowDebugSuccessPage()
    {
        Session.ShowDebugSuccess(
            Preparation.TargetComputerName,
            _deploymentOrchestrator.PlannedSteps.Count,
            DeploymentStepNames.FinalizeDeploymentAndWriteLogs);
    }

    [RelayCommand(CanExecute = nameof(CanShowDebugPages))]
    private void ShowDebugErrorPage()
    {
        Session.ShowDebugError(
            Preparation.TargetComputerName,
            currentStepIndex: 7,
            failedStepName: DeploymentStepNames.ApplyOperatingSystemImage,
            failedStepErrorMessage:
            "Debug preview: DISM apply failed because the target partition is read-only.\n\n" +
            "ErrorCode=0x80070005\n" +
            "Details: Access denied while mounting image to target path.\n" +
            "Action: Verify disk attributes and retry deployment.");
    }

    private void OnOperatingSystemCatalogStateChanged(object? sender, EventArgs e)
    {
        OnPropertyChanged(nameof(EffectiveOsArchitecture));
        OnPropertyChanged(nameof(SelectedOperatingSystem));
        RefreshDriverPackSelectionContext();
        NextWizardStepCommand.NotifyCanExecuteChanged();
        StartDeploymentCommand.NotifyCanExecuteChanged();
    }

    private void OnDriverPackSelectionStateChanged(object? sender, EventArgs e)
    {
        StartDeploymentCommand.NotifyCanExecuteChanged();
    }

    private void OnPreparationStateChanged(object? sender, EventArgs e)
    {
        NextWizardStepCommand.NotifyCanExecuteChanged();
        StartDeploymentCommand.NotifyCanExecuteChanged();

        if (Preparation.SelectedTargetDisk is not null && !IsDebugSafeMode && !Preparation.SelectedTargetDisk.IsSelectable)
        {
            Session.SetStatus($"Selected disk blocked: {Preparation.SelectedTargetDisk.SelectionWarning}");
        }
    }

    private void OnPreparationStatusMessageGenerated(string message)
    {
        Session.SetStatus(message);
    }

    private bool CanShowDebugPages()
    {
        return IsDebugSafeMode && !IsDeploymentRunning;
    }

    private void EnsureCachePathForMode()
    {
        if (IsDebugSafeMode)
        {
            Preparation.CacheRootPath = Path.Combine(Path.GetTempPath(), "Foundry", "Runtime", "Debug");
            return;
        }

        if (_deploymentRuntimeContext.Mode == DeploymentMode.Usb)
        {
            Preparation.CacheRootPath = _deploymentRuntimeContext.UsbCacheRuntimeRoot ?? WinPeTransientRuntimeRoot;
            return;
        }

        Preparation.CacheRootPath = WinPeTransientRuntimeRoot;
    }

    private bool CanRefreshCatalogs()
    {
        return !IsCatalogLoading && !IsDeploymentRunning;
    }

    private bool CanGoPrevious()
    {
        return !IsDeploymentRunning && WizardStepIndex > 0;
    }

    private bool CanGoNext()
    {
        if (IsDeploymentRunning || WizardStepIndex >= 3)
        {
            return false;
        }

        if (WizardStepIndex == 0)
        {
            return IsOsCatalogReadyForNavigation();
        }

        return true;
    }

    private bool IsOsCatalogReadyForNavigation()
    {
        return !IsCatalogLoading &&
               OperatingSystemCatalog.IsReadyForNavigation();
    }

    private bool CanStartDeployment()
    {
        bool hasTargetDisk = Preparation.SelectedTargetDisk is not null && (IsDebugSafeMode || Preparation.SelectedTargetDisk.IsSelectable);

        if (IsDebugSafeMode && Preparation.SelectedTargetDisk is null)
        {
            hasTargetDisk = true;
        }

        return !IsDeploymentRunning &&
               !IsCatalogLoading &&
               !Preparation.IsTargetDiskLoading &&
               WizardStepIndex == 3 &&
               ComputerNameRules.IsValid(Preparation.TargetComputerName) &&
               OperatingSystemCatalog.SelectedOperatingSystem is not null &&
               hasTargetDisk &&
               HasValidDriverPackSelection();
    }

    private bool HasValidDriverPackSelection()
    {
        return DriverPackSelection.HasValidSelection();
    }

    private void RefreshDriverPackSelectionContext()
    {
        DriverPackSelection.UpdateSelectionContext(
            _detectedHardware,
            OperatingSystemCatalog.SelectedOperatingSystem,
            OperatingSystemCatalog.EffectiveOsArchitecture);
    }

    private async Task LoadHardwareProfileAsync()
    {
        await Preparation.LoadHardwareProfileAsync().ConfigureAwait(false);
        RunOnUi(() =>
        {
            _detectedHardware = Preparation.DetectedHardware;
            if (Preparation.DetectedHardware is not null)
            {
                OperatingSystemCatalog.SetEffectiveArchitecture(Preparation.DetectedHardware.Architecture);
            }

            RefreshDriverPackSelectionContext();
        });
    }

    private async Task LoadOfflineComputerNameAsync()
    {
        await Preparation.LoadOfflineComputerNameAsync(ResolveInitialComputerName()).ConfigureAwait(false);
        RunOnUi(() => Session.SetComputerName(Preparation.TargetComputerName));
    }

    private static string ResolveInitialComputerName()
    {
        string normalized = ComputerNameRules.Normalize(Environment.MachineName);
        return normalized.Length > 0
            ? normalized
            : ComputerNameRules.FallbackName;
    }

    private void RunOnUi(Action action)
    {
        if (_dispatcher.CheckAccess())
        {
            action();
            return;
        }

        _dispatcher.Invoke(action);
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        Preparation.StatusMessageGenerated -= OnPreparationStatusMessageGenerated;
        Preparation.StateChanged -= OnPreparationStateChanged;
        OperatingSystemCatalog.StateChanged -= OnOperatingSystemCatalogStateChanged;
        DriverPackSelection.StateChanged -= OnDriverPackSelectionStateChanged;
        Session.Dispose();
        _isDisposed = true;
    }

}
