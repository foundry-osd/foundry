using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Foundry.Deploy;
using Foundry.Deploy.Models.Configuration;
using Foundry.Deploy.Models;
using Foundry.Deploy.Services.ApplicationShell;
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
    private const string NoneDriverPackOptionKey = "none";
    private const string MicrosoftUpdateCatalogDriverPackOptionKey = "microsoft-update-catalog";
    private const string DellDriverPackOptionKey = "oem:dell";
    private const string LenovoDriverPackOptionKey = "oem:lenovo";
    private const string HpDriverPackOptionKey = "oem:hp";
    private const string MicrosoftOemDriverPackOptionKey = "oem:microsoft";
    private const string DeploymentModeEnvironmentVariable = "FOUNDRY_DEPLOYMENT_MODE";
    private const string CacheVolumeLabel = "Foundry Cache";
    private const string CacheMarkerFolderName = "Foundry Cache";
    private const string RuntimeFolderName = "Runtime";
    private const string WinPeTransientRuntimeRoot = @"X:\Foundry\Runtime";
    private static readonly string AppVersion = ResolveAppVersion();
    private readonly IThemeService _themeService;
    private readonly IApplicationShellService _applicationShellService;
    private readonly IExpertDeployConfigurationService _expertDeployConfigurationService;
    private readonly IOperatingSystemCatalogService _operatingSystemCatalogService;
    private readonly IDriverPackCatalogService _driverPackCatalogService;
    private readonly IDeploymentOrchestrator _deploymentOrchestrator;
    private readonly IHardwareProfileService _hardwareProfileService;
    private readonly IOfflineWindowsComputerNameService _offlineWindowsComputerNameService;
    private readonly ITargetDiskService _targetDiskService;
    private readonly IDriverPackSelectionService _driverPackSelectionService;
    private readonly ILogger<MainWindowViewModel> _logger;
    private readonly Dispatcher _dispatcher;
    private readonly DeploymentMode _resolvedDeploymentMode;
    private readonly string? _resolvedUsbCacheRuntimeRoot;
    private HardwareProfile? _detectedHardware;
    private DeployMachineNamingSettings _machineNamingConfiguration = new();
    private string _lockedComputerNamePrefix = string.Empty;
    private bool _isApplyingManagedComputerName;
    private bool _isUpdatingDriverPackOptionSelection;
    private bool _hasUserSelectedDriverPackOption;
    private bool _isUpdatingFirmwareOptionSelection;
    private bool _hasUserSelectedFirmwareOption;
    private bool _firmwareUpdatesPreference = true;
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
    [NotifyCanExecuteChangedFor(nameof(RefreshTargetDisksCommand))]
    [NotifyCanExecuteChangedFor(nameof(StartDeploymentCommand))]
    private bool isTargetDiskLoading;

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
    private string targetComputerName = string.Empty;

    [ObservableProperty]
    private bool isTargetComputerNameReadOnly;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasTargetComputerNameValidationError))]
    private string targetComputerNameValidationMessage = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(NextWizardStepCommand))]
    [NotifyCanExecuteChangedFor(nameof(StartDeploymentCommand))]
    private TargetDiskInfo? selectedTargetDisk;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DriverPackModeDisplay))]
    [NotifyPropertyChangedFor(nameof(IsOemDriverSourceSelected))]
    [NotifyPropertyChangedFor(nameof(IsDriverPackModelSelectionEnabled))]
    [NotifyPropertyChangedFor(nameof(IsDriverPackVersionSelectionEnabled))]
    [NotifyPropertyChangedFor(nameof(SelectedDriverPackSelectionDisplay))]
    [NotifyCanExecuteChangedFor(nameof(StartDeploymentCommand))]
    private DriverPackOptionItem? selectedDriverPackOption;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectedDriverPackSelectionDisplay))]
    [NotifyCanExecuteChangedFor(nameof(StartDeploymentCommand))]
    private string selectedDriverPackModel = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectedDriverPackSelectionDisplay))]
    [NotifyCanExecuteChangedFor(nameof(StartDeploymentCommand))]
    private string selectedDriverPackVersion = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartDeploymentCommand))]
    private string cacheRootPath = WinPeTransientRuntimeRoot;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartDeploymentCommand))]
    private bool autoSelectDriverPackWhenEmpty = true;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartDeploymentCommand))]
    private bool applyFirmwareUpdates = true;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartDeploymentCommand))]
    private bool useFullAutopilot = true;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartDeploymentCommand))]
    private bool allowAutopilotDeferredCompletion = true;

    [ObservableProperty]
    private string detectedHardwareSummary = "Detecting hardware...";
    public ObservableCollection<DriverPackCatalogItem> DriverPacks { get; } = [];
    public ObservableCollection<DriverPackOptionItem> DriverPackOptions { get; } = [];
    public ObservableCollection<string> DriverPackModelOptions { get; } = [];
    public ObservableCollection<string> DriverPackVersionOptions { get; } = [];
    public ObservableCollection<TargetDiskInfo> TargetDisks { get; } = [];
    public DeploymentSessionViewModel Session { get; }
    public OperatingSystemCatalogViewModel OperatingSystemCatalog { get; }

    public DeployThemeMode CurrentTheme => _themeService.CurrentTheme;
    public bool IsDebugSafeMode => DebugSafetyMode.IsEnabled;
    public string EffectiveOsArchitecture => OperatingSystemCatalog.EffectiveOsArchitecture;
    public OperatingSystemCatalogItem? SelectedOperatingSystem => OperatingSystemCatalog.SelectedOperatingSystem;
    public string DriverPackModeDisplay => SelectedDriverPackOption?.Kind switch
    {
        DriverPackSelectionKind.MicrosoftUpdateCatalog => "Microsoft Update Catalog",
        DriverPackSelectionKind.OemCatalog => "OEM Driver Pack",
        _ => "None"
    };
    public bool IsOemDriverSourceSelected => SelectedDriverPackOption?.Kind == DriverPackSelectionKind.OemCatalog;
    public bool IsDriverPackModelSelectionEnabled => IsOemDriverSourceSelected && DriverPackModelOptions.Count > 0;
    public bool IsDriverPackVersionSelectionEnabled => IsDriverPackModelSelectionEnabled && DriverPackVersionOptions.Count > 0;
    public bool IsFirmwareUpdatesOptionEnabled => _detectedHardware?.IsVirtualMachine != true;
    public string SelectedDriverPackSelectionDisplay => BuildSelectedDriverPackSelectionDisplay();
    public bool HasTargetComputerNameValidationError => !string.IsNullOrWhiteSpace(TargetComputerNameValidationMessage);
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
        IApplicationShellService applicationShellService,
        IOperationProgressService operationProgressService,
        IExpertDeployConfigurationService expertDeployConfigurationService,
        IOperatingSystemCatalogService operatingSystemCatalogService,
        IDriverPackCatalogService driverPackCatalogService,
        IDeploymentOrchestrator deploymentOrchestrator,
        IHardwareProfileService hardwareProfileService,
        IOfflineWindowsComputerNameService offlineWindowsComputerNameService,
        ITargetDiskService targetDiskService,
        IDriverPackSelectionService driverPackSelectionService,
        IProcessRunner processRunner,
        ILogger<MainWindowViewModel> logger)
    {
        _themeService = themeService;
        _applicationShellService = applicationShellService;
        _expertDeployConfigurationService = expertDeployConfigurationService;
        _operatingSystemCatalogService = operatingSystemCatalogService;
        _driverPackCatalogService = driverPackCatalogService;
        _deploymentOrchestrator = deploymentOrchestrator;
        _hardwareProfileService = hardwareProfileService;
        _offlineWindowsComputerNameService = offlineWindowsComputerNameService;
        _targetDiskService = targetDiskService;
        _driverPackSelectionService = driverPackSelectionService;
        _logger = logger;
        _dispatcher = Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;
        (DeploymentMode resolvedMode, string? resolvedUsbCacheRuntimeRoot) = ResolveDeploymentRuntimeContext();
        _resolvedDeploymentMode = resolvedMode;
        _resolvedUsbCacheRuntimeRoot = resolvedUsbCacheRuntimeRoot;
        OperatingSystemCatalog = new OperatingSystemCatalogViewModel(
            _logger,
            Environment.GetEnvironmentVariable("PROCESSOR_ARCHITECTURE") ?? string.Empty);
        OperatingSystemCatalog.StateChanged += OnOperatingSystemCatalogStateChanged;
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
                RefreshTargetDisksAsync(),
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
        _machineNamingConfiguration = document.Customization.MachineNaming ?? new DeployMachineNamingSettings();
        _lockedComputerNamePrefix = ComputerNameRules.Normalize(_machineNamingConfiguration.Prefix);
        IsTargetComputerNameReadOnly = _machineNamingConfiguration.IsEnabled && !_machineNamingConfiguration.AllowManualSuffixEdit;

        if (_machineNamingConfiguration.IsEnabled)
        {
            string seed = string.IsNullOrWhiteSpace(TargetComputerName)
                ? ResolveInitialComputerName()
                : TargetComputerName;
            ApplyManagedComputerNameValue(BuildConfiguredComputerName(seed));
        }
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
                DriverPacks.Clear();
                foreach (DriverPackCatalogItem item in driverPacks)
                {
                    DriverPacks.Add(item);
                }

                RefreshDriverPackOptions();

                Session.SetStatus($"Catalogs loaded: {OperatingSystemCatalog.OperatingSystems.Count} OS entries, {DriverPacks.Count} driver packs.");
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

    [RelayCommand(CanExecute = nameof(CanRefreshTargetDisks))]
    private async Task RefreshTargetDisksAsync()
    {
        _logger.LogInformation("Refreshing target disk list.");
        if (IsTargetDiskLoading)
        {
            return;
        }

        IsTargetDiskLoading = true;
        Session.SetStatus("Loading target disks...");

        try
        {
            IReadOnlyList<TargetDiskInfo> disks = await _targetDiskService
                .GetDisksAsync()
                .ConfigureAwait(false);

            RunOnUi(() =>
            {
                TargetDisks.Clear();
                foreach (TargetDiskInfo disk in disks)
                {
                    TargetDisks.Add(disk);
                }

                if (IsDebugSafeMode && !TargetDisks.Any(item => item.IsSelectable))
                {
                    TargetDisks.Insert(0, BuildDebugVirtualDisk());
                }

                if (TargetDisks.Count == 0)
                {
                    SelectedTargetDisk = null;
                    Session.SetStatus("No disks detected.");
                    return;
                }

                TargetDiskInfo? currentSelection = SelectedTargetDisk is null
                    ? null
                    : TargetDisks.FirstOrDefault(item => item.DiskNumber == SelectedTargetDisk.DiskNumber);

                SelectedTargetDisk = currentSelection
                    ?? TargetDisks.FirstOrDefault(item => item.IsSelectable)
                    ?? (IsDebugSafeMode ? TargetDisks.FirstOrDefault(item => item.DiskNumber == BuildDebugVirtualDisk().DiskNumber) : null)
                    ?? TargetDisks.FirstOrDefault();

                Session.SetStatus($"Target disks loaded: {TargetDisks.Count} detected.");
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Target disk discovery failed.");
            RunOnUi(() => Session.SetStatus($"Target disk discovery failed: {ex.Message}"));
        }
        finally
        {
            RunOnUi(() => IsTargetDiskLoading = false);
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
        if (OperatingSystemCatalog.SelectedOperatingSystem is null)
        {
            return;
        }

        string normalizedComputerName = ComputerNameRules.Normalize(TargetComputerName);
        if (!ComputerNameRules.IsValid(normalizedComputerName))
        {
            Session.SetStatus("Enter a valid computer name.");
            return;
        }

        if (!normalizedComputerName.Equals(TargetComputerName, StringComparison.Ordinal))
        {
            TargetComputerName = normalizedComputerName;
        }

        TargetDiskInfo? effectiveTargetDisk = SelectedTargetDisk;
        if (effectiveTargetDisk is null && IsDebugSafeMode)
        {
            effectiveTargetDisk = BuildDebugVirtualDisk();
        }

        if (effectiveTargetDisk is null)
        {
            Session.SetStatus("Select a target disk.");
            return;
        }

        if (!IsDebugSafeMode && !effectiveTargetDisk.IsSelectable)
        {
            Session.SetStatus($"Selected disk is blocked: {effectiveTargetDisk.SelectionWarning}");
            return;
        }

        if (!IsDebugSafeMode && !ConfirmDestructiveDeployment(effectiveTargetDisk, OperatingSystemCatalog.SelectedOperatingSystem))
        {
            Session.SetStatus("Deployment cancelled by user.");
            return;
        }

        DriverPackSelectionKind effectiveDriverPackKind = SelectedDriverPackOption?.Kind ?? DriverPackSelectionKind.None;
        DriverPackCatalogItem? effectiveDriverPack = ResolveEffectiveDriverPackSelection();

        if (effectiveDriverPackKind == DriverPackSelectionKind.OemCatalog &&
            effectiveDriverPack is null)
        {
            Session.SetStatus("Select a valid OEM model/version before starting deployment.");
            return;
        }

        EnsureCachePathForMode();
        RunOnUi(() =>
        {
            IsDeploymentRunning = true;
            Session.BeginDeployment(normalizedComputerName, _deploymentOrchestrator.PlannedSteps.Count);
        });

        DeploymentContext context = new()
        {
            Mode = _resolvedDeploymentMode,
            CacheRootPath = CacheRootPath,
            TargetDiskNumber = effectiveTargetDisk.DiskNumber,
            TargetComputerName = normalizedComputerName,
            OperatingSystem = OperatingSystemCatalog.SelectedOperatingSystem,
            DriverPackSelectionKind = effectiveDriverPackKind,
            DriverPack = effectiveDriverPack,
            ApplyFirmwareUpdates = ApplyFirmwareUpdates,
            UseFullAutopilot = UseFullAutopilot,
            AllowAutopilotDeferredCompletion = AllowAutopilotDeferredCompletion,
            IsDryRun = IsDebugSafeMode
        };

        try
        {
            DeploymentResult result = await _deploymentOrchestrator
                .RunAsync(context)
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
            TargetComputerName,
            currentStepIndex: 7,
            plannedStepCount: _deploymentOrchestrator.PlannedSteps.Count,
            currentStepName: DeploymentStepNames.ApplyOperatingSystemImage,
            progressPercent: 42);
    }

    [RelayCommand(CanExecute = nameof(CanShowDebugPages))]
    private void ShowDebugSuccessPage()
    {
        Session.ShowDebugSuccess(
            TargetComputerName,
            _deploymentOrchestrator.PlannedSteps.Count,
            DeploymentStepNames.FinalizeDeploymentAndWriteLogs);
    }

    [RelayCommand(CanExecute = nameof(CanShowDebugPages))]
    private void ShowDebugErrorPage()
    {
        Session.ShowDebugError(
            TargetComputerName,
            currentStepIndex: 7,
            failedStepName: DeploymentStepNames.ApplyOperatingSystemImage,
            failedStepErrorMessage:
            "Debug preview: DISM apply failed because the target partition is read-only.\n\n" +
            "ErrorCode=0x80070005\n" +
            "Details: Access denied while mounting image to target path.\n" +
            "Action: Verify disk attributes and retry deployment.");
    }

    partial void OnTargetComputerNameChanged(string value)
    {
        if (_isApplyingManagedComputerName)
        {
            TargetComputerNameValidationMessage = ComputerNameRules.GetValidationMessage(value);
            return;
        }

        string normalized = NormalizeManagedComputerNameValue(value);
        if (!normalized.Equals(value, StringComparison.Ordinal))
        {
            ApplyManagedComputerNameValue(normalized);
            return;
        }

        TargetComputerNameValidationMessage = ComputerNameRules.GetValidationMessage(normalized);
    }

    partial void OnSelectedDriverPackOptionChanged(DriverPackOptionItem? value)
    {
        if (_isUpdatingDriverPackOptionSelection)
        {
            return;
        }

        _hasUserSelectedDriverPackOption = true;
        RefreshDriverPackModelAndVersionOptions();
    }

    partial void OnSelectedDriverPackModelChanged(string value)
    {
        RefreshDriverPackVersionOptions();
    }

    partial void OnAutoSelectDriverPackWhenEmptyChanged(bool value)
    {
        RefreshDriverPackOptions();
    }

    partial void OnApplyFirmwareUpdatesChanged(bool value)
    {
        if (_isUpdatingFirmwareOptionSelection)
        {
            return;
        }

        _hasUserSelectedFirmwareOption = true;
        _firmwareUpdatesPreference = value;
    }

    partial void OnSelectedTargetDiskChanged(TargetDiskInfo? value)
    {
        if (value is null)
        {
            return;
        }

        if (!IsDebugSafeMode && !value.IsSelectable)
        {
            Session.SetStatus($"Selected disk blocked: {value.SelectionWarning}");
        }
    }

    private void OnOperatingSystemCatalogStateChanged(object? sender, EventArgs e)
    {
        OnPropertyChanged(nameof(EffectiveOsArchitecture));
        OnPropertyChanged(nameof(SelectedOperatingSystem));
        RefreshDriverPackOptions();
        NextWizardStepCommand.NotifyCanExecuteChanged();
        StartDeploymentCommand.NotifyCanExecuteChanged();
    }

    private bool CanShowDebugPages()
    {
        return IsDebugSafeMode && !IsDeploymentRunning;
    }

    private void EnsureCachePathForMode()
    {
        if (IsDebugSafeMode)
        {
            CacheRootPath = Path.Combine(Path.GetTempPath(), "Foundry", "Runtime", "Debug");
            return;
        }

        if (_resolvedDeploymentMode == DeploymentMode.Usb)
        {
            CacheRootPath = _resolvedUsbCacheRuntimeRoot ?? WinPeTransientRuntimeRoot;
            return;
        }

        CacheRootPath = WinPeTransientRuntimeRoot;
    }

    private bool CanRefreshCatalogs()
    {
        return !IsCatalogLoading && !IsDeploymentRunning;
    }

    private bool CanRefreshTargetDisks()
    {
        return !IsTargetDiskLoading && !IsDeploymentRunning;
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
        bool hasTargetDisk = SelectedTargetDisk is not null && (IsDebugSafeMode || SelectedTargetDisk.IsSelectable);

        if (IsDebugSafeMode && SelectedTargetDisk is null)
        {
            hasTargetDisk = true;
        }

        return !IsDeploymentRunning &&
               !IsCatalogLoading &&
               !IsTargetDiskLoading &&
               WizardStepIndex == 3 &&
               ComputerNameRules.IsValid(TargetComputerName) &&
               OperatingSystemCatalog.SelectedOperatingSystem is not null &&
               hasTargetDisk &&
               HasValidDriverPackSelection();
    }

    private bool HasValidDriverPackSelection()
    {
        if (SelectedDriverPackOption?.Kind != DriverPackSelectionKind.OemCatalog)
        {
            return true;
        }

        return ResolveEffectiveDriverPackSelection() is not null;
    }

    private string NormalizeManagedComputerNameValue(string? value)
    {
        string normalized = ComputerNameRules.Normalize(value);
        if (!_machineNamingConfiguration.IsEnabled)
        {
            return normalized;
        }

        if (string.IsNullOrWhiteSpace(_lockedComputerNamePrefix))
        {
            return normalized;
        }

        if (!_machineNamingConfiguration.AllowManualSuffixEdit)
        {
            return BuildConfiguredComputerName(normalized);
        }

        string suffix = normalized.StartsWith(_lockedComputerNamePrefix, StringComparison.OrdinalIgnoreCase)
            ? normalized[_lockedComputerNamePrefix.Length..]
            : normalized;

        return CombineComputerName(_lockedComputerNamePrefix, suffix);
    }

    private string BuildConfiguredComputerName(string seed)
    {
        string normalizedSeed = ComputerNameRules.Normalize(seed);
        if (!_machineNamingConfiguration.IsEnabled)
        {
            return normalizedSeed;
        }

        if (string.IsNullOrWhiteSpace(_lockedComputerNamePrefix))
        {
            return _machineNamingConfiguration.AutoGenerateName
                ? NormalizeAutoGeneratedComputerNameSuffix(normalizedSeed)
                : normalizedSeed;
        }

        if (_machineNamingConfiguration.AutoGenerateName)
        {
            string generatedSuffix = NormalizeAutoGeneratedComputerNameSuffix(
                ExtractComputerNameSuffix(normalizedSeed, _lockedComputerNamePrefix));
            return CombineComputerName(_lockedComputerNamePrefix, generatedSuffix);
        }

        string existingSuffix = ExtractComputerNameSuffix(TargetComputerName, _lockedComputerNamePrefix);
        return CombineComputerName(_lockedComputerNamePrefix, existingSuffix);
    }

    private void ApplyManagedComputerNameValue(string value)
    {
        _isApplyingManagedComputerName = true;

        try
        {
            TargetComputerName = value;
            TargetComputerNameValidationMessage = ComputerNameRules.GetValidationMessage(value);
        }
        finally
        {
            _isApplyingManagedComputerName = false;
        }
    }

    private static string CombineComputerName(string prefix, string suffix)
    {
        string normalizedPrefix = ComputerNameRules.Normalize(prefix);
        if (normalizedPrefix.Length >= ComputerNameRules.MaxLength)
        {
            return normalizedPrefix[..ComputerNameRules.MaxLength];
        }

        string normalizedSuffix = ComputerNameRules.Normalize(suffix);
        int remainingLength = ComputerNameRules.MaxLength - normalizedPrefix.Length;
        if (remainingLength <= 0)
        {
            return normalizedPrefix;
        }

        if (normalizedSuffix.Length > remainingLength)
        {
            normalizedSuffix = normalizedSuffix[..remainingLength];
        }

        return $"{normalizedPrefix}{normalizedSuffix}";
    }

    private static string ExtractComputerNameSuffix(string? computerName, string prefix)
    {
        string normalizedPrefix = ComputerNameRules.Normalize(prefix);
        if (string.IsNullOrWhiteSpace(normalizedPrefix))
        {
            return ComputerNameRules.Normalize(computerName);
        }

        string normalizedComputerName = ComputerNameRules.Normalize(computerName);
        if (normalizedComputerName.StartsWith(normalizedPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return normalizedComputerName[normalizedPrefix.Length..];
        }

        return normalizedComputerName;
    }

    private static string NormalizeAutoGeneratedComputerNameSuffix(string? value)
    {
        string normalized = ComputerNameRules.Normalize(value);
        return string.IsNullOrWhiteSpace(normalized)
            ? ComputerNameRules.FallbackName
            : normalized;
    }

    private void RefreshDriverPackOptions()
    {
        string previousKey = _hasUserSelectedDriverPackOption
            ? SelectedDriverPackOption?.Key ?? string.Empty
            : string.Empty;
        DriverPackOptionItem[] options = BuildDriverPackOptions();

        _isUpdatingDriverPackOptionSelection = true;
        try
        {
            DriverPackOptions.Clear();
            foreach (DriverPackOptionItem option in options)
            {
                DriverPackOptions.Add(option);
            }

            DriverPackOptionItem? selected = null;
            if (!string.IsNullOrWhiteSpace(previousKey))
            {
                selected = options.FirstOrDefault(option =>
                    option.Key.Equals(previousKey, StringComparison.OrdinalIgnoreCase));
            }

            selected ??= ResolveDefaultDriverPackOption(options);
            SelectedDriverPackOption = selected;
        }
        finally
        {
            _isUpdatingDriverPackOptionSelection = false;
        }

        RefreshDriverPackModelAndVersionOptions();
    }

    private DriverPackOptionItem[] BuildDriverPackOptions()
    {
        return
        [
            CreateNoneDriverPackOption(),
            CreateMicrosoftUpdateCatalogOption(),
            CreateOemDriverPackOption(DellDriverPackOptionKey, "Dell"),
            CreateOemDriverPackOption(LenovoDriverPackOptionKey, "Lenovo"),
            CreateOemDriverPackOption(HpDriverPackOptionKey, "HP"),
            CreateOemDriverPackOption(MicrosoftOemDriverPackOptionKey, "Microsoft")
        ];
    }

    private DriverPackCatalogItem[] BuildSourceDriverPackCandidates()
    {
        if (!IsOemDriverSourceSelected)
        {
            return [];
        }

        string sourceManufacturer = ResolveManufacturerFromSourceOptionKey(SelectedDriverPackOption?.Key ?? string.Empty);
        if (string.IsNullOrWhiteSpace(sourceManufacturer))
        {
            return [];
        }

        return BuildFilteredDriverPackCandidates(forceManufacturer: sourceManufacturer);
    }

    private void RefreshDriverPackModelAndVersionOptions()
    {
        string previousModel = SelectedDriverPackModel;

        DriverPackModelOptions.Clear();
        DriverPackVersionOptions.Clear();
        SelectedDriverPackModel = string.Empty;
        SelectedDriverPackVersion = string.Empty;

        if (!IsOemDriverSourceSelected)
        {
            NotifyDriverPackSelectionStateChanged();
            return;
        }

        DriverPackCatalogItem[] sourceCandidates = BuildSourceDriverPackCandidates();
        string[] models = sourceCandidates
            .SelectMany(GetSelectableModelNames)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(model => model, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        foreach (string model in models)
        {
            DriverPackModelOptions.Add(model);
        }

        if (models.Length > 0)
        {
            string preferredModel = models.FirstOrDefault(model =>
                model.Equals(previousModel, StringComparison.OrdinalIgnoreCase))
                ?? ResolvePreferredModelFromHardware(sourceCandidates, models);

            SelectedDriverPackModel = preferredModel;
        }

        NotifyDriverPackSelectionStateChanged();
    }

    private void RefreshDriverPackVersionOptions()
    {
        string previousVersion = SelectedDriverPackVersion;
        DriverPackVersionOptions.Clear();
        SelectedDriverPackVersion = string.Empty;

        if (!IsOemDriverSourceSelected || string.IsNullOrWhiteSpace(SelectedDriverPackModel))
        {
            NotifyDriverPackSelectionStateChanged();
            return;
        }

        DriverPackCatalogItem[] modelCandidates = FilterDriverPackCandidatesBySelectedModel(BuildSourceDriverPackCandidates());
        DriverPackCatalogItem[] orderedCandidates = SortDriverPackCandidates(modelCandidates);

        string[] versions = orderedCandidates
            .Select(GetDriverPackVersionDisplay)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        foreach (string version in versions)
        {
            DriverPackVersionOptions.Add(version);
        }

        if (versions.Length > 0)
        {
            SelectedDriverPackVersion = versions.FirstOrDefault(version =>
                version.Equals(previousVersion, StringComparison.OrdinalIgnoreCase))
                ?? versions[0];
        }

        NotifyDriverPackSelectionStateChanged();
    }

    private DriverPackCatalogItem[] FilterDriverPackCandidatesBySelectedModel(IEnumerable<DriverPackCatalogItem> candidates)
    {
        if (string.IsNullOrWhiteSpace(SelectedDriverPackModel))
        {
            return candidates.ToArray();
        }

        string selectedModel = SelectedDriverPackModel.Trim();
        return candidates
            .Where(item => GetSelectableModelNames(item).Any(model =>
                model.Equals(selectedModel, StringComparison.OrdinalIgnoreCase)))
            .ToArray();
    }

    private string ResolvePreferredModelFromHardware(
        IReadOnlyList<DriverPackCatalogItem> sourceCandidates,
        IReadOnlyList<string> modelOptions)
    {
        if (modelOptions.Count == 0)
        {
            return string.Empty;
        }

        if (_detectedHardware is null)
        {
            return modelOptions[0];
        }

        string[] hardwareTokens =
        [
            _detectedHardware.Model.Trim(),
            _detectedHardware.Product.Trim()
        ];
        hardwareTokens = hardwareTokens
            .Where(token => !string.IsNullOrWhiteSpace(token))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (hardwareTokens.Length == 0)
        {
            return modelOptions[0];
        }

        string? exactOptionMatch = modelOptions.FirstOrDefault(option =>
            hardwareTokens.Any(token => option.Equals(token, StringComparison.OrdinalIgnoreCase)));
        if (!string.IsNullOrWhiteSpace(exactOptionMatch))
        {
            return exactOptionMatch;
        }

        string? containsOptionMatch = modelOptions.FirstOrDefault(option =>
            hardwareTokens.Any(token => IsFuzzyModelMatch(option, token)));
        if (!string.IsNullOrWhiteSpace(containsOptionMatch))
        {
            return containsOptionMatch;
        }

        DriverPackCatalogItem? bestPackMatch = sourceCandidates
            .Where(item => item.ModelNames.Any(modelName =>
                hardwareTokens.Any(token => IsFuzzyModelMatch(modelName, token))))
            .OrderByDescending(item => item.ReleaseDate ?? DateTimeOffset.MinValue)
            .ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();

        if (bestPackMatch is not null)
        {
            string? modelFromPack = GetSelectableModelNames(bestPackMatch)
                .FirstOrDefault(model => modelOptions.Any(option =>
                    option.Equals(model, StringComparison.OrdinalIgnoreCase)));
            if (!string.IsNullOrWhiteSpace(modelFromPack))
            {
                return modelFromPack;
            }
        }

        return modelOptions[0];
    }

    private DriverPackCatalogItem[] BuildFilteredDriverPackCandidates(string forceManufacturer = "")
    {
        IEnumerable<DriverPackCatalogItem> query = DriverPacks;

        string architecture = NormalizeArchitecture(SelectedOperatingSystem?.Architecture ?? EffectiveOsArchitecture);
        if (!string.IsNullOrWhiteSpace(architecture))
        {
            query = query.Where(item => IsArchitectureMatch(architecture, item.OsArchitecture));
        }

        string selectedOsRelease = SelectedOperatingSystem?.WindowsRelease?.Trim() ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(selectedOsRelease))
        {
            string windowsLabel = $"Windows {selectedOsRelease}";
            query = query.Where(item =>
                item.OsName.Contains(windowsLabel, StringComparison.OrdinalIgnoreCase) ||
                item.OsName.Contains(selectedOsRelease, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(forceManufacturer))
        {
            query = query.Where(item =>
                NormalizeManufacturer(item.Manufacturer).Equals(forceManufacturer, StringComparison.OrdinalIgnoreCase));
        }

        DriverPackCatalogItem[] baseCandidates = query.ToArray();
        return SortDriverPackCandidates(baseCandidates);
    }

    private DriverPackOptionItem? ResolveDefaultDriverPackOption(IReadOnlyList<DriverPackOptionItem> options)
    {
        if (options.Count == 0)
        {
            return null;
        }

        if (!AutoSelectDriverPackWhenEmpty)
        {
            return options.FirstOrDefault(option => option.Kind == DriverPackSelectionKind.None) ?? options[0];
        }

        if (_detectedHardware?.IsVirtualMachine == true)
        {
            return options.FirstOrDefault(option => option.Kind == DriverPackSelectionKind.MicrosoftUpdateCatalog)
                   ?? options[0];
        }

        if (_detectedHardware is not null && SelectedOperatingSystem is not null && DriverPacks.Count > 0)
        {
            DriverPackSelectionResult selection = _driverPackSelectionService.SelectBest(
                DriverPacks.ToArray(),
                _detectedHardware,
                SelectedOperatingSystem);

            if (selection.DriverPack is not null)
            {
                string selectedKey = ResolveSourceOptionKey(selection.DriverPack.Manufacturer);
                DriverPackOptionItem? oemMatch = options.FirstOrDefault(option =>
                    option.Key.Equals(selectedKey, StringComparison.OrdinalIgnoreCase));

                if (oemMatch is not null)
                {
                    return oemMatch;
                }
            }
        }

        return options.FirstOrDefault(option => option.Kind == DriverPackSelectionKind.MicrosoftUpdateCatalog)
               ?? options[0];
    }

    private static DriverPackOptionItem CreateNoneDriverPackOption()
    {
        return new DriverPackOptionItem
        {
            Key = NoneDriverPackOptionKey,
            DisplayName = "None",
            Kind = DriverPackSelectionKind.None,
            DriverPack = null
        };
    }

    private static DriverPackOptionItem CreateMicrosoftUpdateCatalogOption()
    {
        return new DriverPackOptionItem
        {
            Key = MicrosoftUpdateCatalogDriverPackOptionKey,
            DisplayName = "Microsoft Update Catalog",
            Kind = DriverPackSelectionKind.MicrosoftUpdateCatalog,
            DriverPack = null
        };
    }

    private static DriverPackOptionItem CreateOemDriverPackOption(string key, string displayName)
    {
        return new DriverPackOptionItem
        {
            Key = key,
            DisplayName = displayName,
            Kind = DriverPackSelectionKind.OemCatalog,
            DriverPack = null
        };
    }

    private DriverPackCatalogItem? ResolveEffectiveDriverPackSelection()
    {
        DriverPackSelectionKind selectionKind = SelectedDriverPackOption?.Kind ?? DriverPackSelectionKind.None;
        if (selectionKind != DriverPackSelectionKind.OemCatalog)
        {
            return SelectedDriverPackOption?.DriverPack;
        }

        DriverPackCatalogItem[] sourceCandidates = BuildSourceDriverPackCandidates();
        if (sourceCandidates.Length == 0)
        {
            return null;
        }

        DriverPackCatalogItem[] modelCandidates = FilterDriverPackCandidatesBySelectedModel(sourceCandidates);
        if (modelCandidates.Length == 0)
        {
            return null;
        }

        DriverPackCatalogItem[] versionCandidates = string.IsNullOrWhiteSpace(SelectedDriverPackVersion)
            ? modelCandidates
            : modelCandidates
                .Where(item => GetDriverPackVersionDisplay(item).Equals(SelectedDriverPackVersion.Trim(), StringComparison.OrdinalIgnoreCase))
                .ToArray();

        DriverPackCatalogItem[] finalCandidates = versionCandidates.Length > 0
            ? versionCandidates
            : modelCandidates;

        return finalCandidates
            .OrderByDescending(item => item.ReleaseDate ?? DateTimeOffset.MinValue)
            .ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
    }

    private string BuildSelectedDriverPackSelectionDisplay()
    {
        DriverPackSelectionKind selectionKind = SelectedDriverPackOption?.Kind ?? DriverPackSelectionKind.None;
        if (selectionKind == DriverPackSelectionKind.None)
        {
            return "None";
        }

        if (selectionKind == DriverPackSelectionKind.MicrosoftUpdateCatalog)
        {
            return "Microsoft Update Catalog";
        }

        DriverPackCatalogItem? selectedPack = ResolveEffectiveDriverPackSelection();
        if (selectedPack is null)
        {
            string sourceName = SelectedDriverPackOption?.DisplayName ?? "OEM";
            return $"{sourceName} | No matching model/version";
        }

        string modelName = string.IsNullOrWhiteSpace(SelectedDriverPackModel)
            ? ResolveDriverPackFriendlyName(selectedPack)
            : SelectedDriverPackModel;
        string version = GetDriverPackVersionDisplay(selectedPack);

        return $"{selectedPack.Manufacturer} | {modelName} | {version}";
    }

    private static string ResolveManufacturerFromSourceOptionKey(string optionKey)
    {
        return optionKey.Trim().ToLowerInvariant() switch
        {
            DellDriverPackOptionKey => "dell",
            LenovoDriverPackOptionKey => "lenovo",
            HpDriverPackOptionKey => "hp",
            MicrosoftOemDriverPackOptionKey => "microsoft",
            _ => string.Empty
        };
    }

    private static string ResolveSourceOptionKey(string manufacturer)
    {
        string normalized = NormalizeManufacturer(manufacturer);
        return normalized switch
        {
            "dell" => DellDriverPackOptionKey,
            "lenovo" => LenovoDriverPackOptionKey,
            "hp" => HpDriverPackOptionKey,
            "microsoft" => MicrosoftOemDriverPackOptionKey,
            _ => string.Empty
        };
    }

    private static string[] GetSelectableModelNames(DriverPackCatalogItem driverPack)
    {
        string[] models = driverPack.ModelNames
            .Where(model => !string.IsNullOrWhiteSpace(model))
            .Select(model => model.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (models.Length > 0)
        {
            return models;
        }

        string fallback = ResolveDriverPackFriendlyName(driverPack);
        return string.IsNullOrWhiteSpace(fallback)
            ? []
            : [fallback];
    }

    private static string GetDriverPackVersionDisplay(DriverPackCatalogItem driverPack)
    {
        if (!string.IsNullOrWhiteSpace(driverPack.Version))
        {
            return driverPack.Version.Trim();
        }

        if (driverPack.ReleaseDate is not null)
        {
            return driverPack.ReleaseDate.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }

        if (!string.IsNullOrWhiteSpace(driverPack.PackageId))
        {
            return driverPack.PackageId.Trim();
        }

        if (!string.IsNullOrWhiteSpace(driverPack.FileName))
        {
            return Path.GetFileNameWithoutExtension(driverPack.FileName.Trim());
        }

        return "Unknown";
    }

    private void NotifyDriverPackSelectionStateChanged()
    {
        OnPropertyChanged(nameof(IsOemDriverSourceSelected));
        OnPropertyChanged(nameof(IsDriverPackModelSelectionEnabled));
        OnPropertyChanged(nameof(IsDriverPackVersionSelectionEnabled));
        OnPropertyChanged(nameof(SelectedDriverPackSelectionDisplay));
        StartDeploymentCommand.NotifyCanExecuteChanged();
    }

    private static string ResolveDriverPackFriendlyName(DriverPackCatalogItem driverPack)
    {
        string[] models = driverPack.ModelNames
            .Where(model => !string.IsNullOrWhiteSpace(model))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (models.Length > 0)
        {
            return models.Length == 1
                ? models[0]
                : $"{models[0]} (+{models.Length - 1} models)";
        }

        if (!LooksLikeArchiveOrInstallerName(driverPack.Name))
        {
            return driverPack.Name.Trim();
        }

        if (!LooksLikeArchiveOrInstallerName(driverPack.PackageId))
        {
            return driverPack.PackageId.Trim();
        }

        string fallback = !string.IsNullOrWhiteSpace(driverPack.FileName)
            ? driverPack.FileName
            : driverPack.Name;

        return Path.GetFileNameWithoutExtension(fallback).Trim();
    }

    private static bool LooksLikeArchiveOrInstallerName(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        string extension = Path.GetExtension(value.Trim());
        if (string.IsNullOrWhiteSpace(extension))
        {
            return false;
        }

        return extension.Equals(".exe", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".cab", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".msi", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".zip", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".7z", StringComparison.OrdinalIgnoreCase);
    }

    private static bool ContainsIgnoreCase(string source, string value)
    {
        if (string.IsNullOrWhiteSpace(source) || string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        return source.Contains(value, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsFuzzyModelMatch(string source, string value)
    {
        return ContainsIgnoreCase(source, value) || ContainsIgnoreCase(value, source);
    }

    private static DriverPackCatalogItem[] SortDriverPackCandidates(IEnumerable<DriverPackCatalogItem> candidates)
    {
        return candidates
            .OrderByDescending(item => item.ReleaseDate ?? DateTimeOffset.MinValue)
            .ThenBy(item => item.Manufacturer, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private async Task LoadHardwareProfileAsync()
    {
        try
        {
            HardwareProfile profile = await _hardwareProfileService.GetCurrentAsync().ConfigureAwait(false);
            RunOnUi(() =>
            {
                _detectedHardware = profile;
                OperatingSystemCatalog.SetEffectiveArchitecture(profile.Architecture);
                SyncFirmwareOptionFromHardware(profile);
                DetectedHardwareSummary =
                    $"{profile.DisplayLabel} | TPM: {(profile.IsTpmPresent ? "Yes" : "No")} | Autopilot: {(profile.IsAutopilotCapable ? "Capable" : "Needs checks")} | Power: {(profile.IsOnBattery ? "Battery" : "AC")} | Firmware: {(profile.SystemFirmwareHardwareId.Length > 0 ? "Detected" : "Unavailable")}";
                RefreshDriverPackOptions();
            });
            _logger.LogInformation("Hardware profile loaded in view model. DisplayLabel={DisplayLabel}", profile.DisplayLabel);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Hardware profile loading failed in view model.");
            RunOnUi(() => DetectedHardwareSummary = $"Hardware detection failed: {ex.Message}");
        }
    }

    private static bool IsArchitectureMatch(string osArchitecture, string driverArchitecture)
    {
        string os = NormalizeArchitecture(osArchitecture);
        string driver = NormalizeArchitecture(driverArchitecture);
        return os.Equals(driver, StringComparison.OrdinalIgnoreCase);
    }

    private void SyncFirmwareOptionFromHardware(HardwareProfile profile)
    {
        bool desiredValue = profile.IsVirtualMachine
            ? false
            : _hasUserSelectedFirmwareOption
                ? _firmwareUpdatesPreference
                : true;

        _isUpdatingFirmwareOptionSelection = true;
        try
        {
            ApplyFirmwareUpdates = desiredValue;
        }
        finally
        {
            _isUpdatingFirmwareOptionSelection = false;
        }

        OnPropertyChanged(nameof(IsFirmwareUpdatesOptionEnabled));
    }

    private async Task LoadOfflineComputerNameAsync()
    {
        string? resolvedName = null;
        try
        {
            resolvedName = await _offlineWindowsComputerNameService
                .TryGetOfflineComputerNameAsync()
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load offline Windows computer name.");
        }

        string effectiveName = !string.IsNullOrWhiteSpace(resolvedName)
            ? resolvedName
            : ResolveInitialComputerName();

        RunOnUi(() =>
        {
            // Only apply if the user hasn't typed anything yet.
            if (!string.IsNullOrEmpty(TargetComputerName))
            {
                return;
            }

            string configuredName = BuildConfiguredComputerName(effectiveName);
            ApplyManagedComputerNameValue(configuredName);
            Session.SetComputerName(TargetComputerName);
        });
    }

    private static string ResolveInitialComputerName()
    {
        string normalized = ComputerNameRules.Normalize(Environment.MachineName);
        return normalized.Length > 0
            ? normalized
            : ComputerNameRules.FallbackName;
    }

    private static string NormalizeArchitecture(string architecture)
    {
        string normalized = architecture.Trim().ToLowerInvariant();
        return normalized switch
        {
            "amd64" => "x64",
            "x64" => "x64",
            "arm64" => "arm64",
            "aarch64" => "arm64",
            _ => normalized
        };
    }

    private static string NormalizeManufacturer(string manufacturer)
    {
        string normalized = manufacturer.Trim().ToLowerInvariant();
        if (normalized.Contains("hewlett") || normalized == "hp")
        {
            return "hp";
        }

        if (normalized.Contains("dell"))
        {
            return "dell";
        }

        if (normalized.Contains("lenovo"))
        {
            return "lenovo";
        }

        if (normalized.Contains("microsoft"))
        {
            return "microsoft";
        }

        return normalized;
    }

    private static (DeploymentMode Mode, string? UsbCacheRuntimeRoot) ResolveDeploymentRuntimeContext()
    {
        if (TryResolveDeploymentModeFromEnvironment(out DeploymentMode modeFromEnvironment))
        {
            string? usbRoot = modeFromEnvironment == DeploymentMode.Usb
                ? TryGetUsbCacheRuntimeRoot()
                : null;
            return (modeFromEnvironment, usbRoot);
        }

        string? detectedUsbRoot = TryGetUsbCacheRuntimeRoot();
        return string.IsNullOrWhiteSpace(detectedUsbRoot)
            ? (DeploymentMode.Iso, null)
            : (DeploymentMode.Usb, detectedUsbRoot);
    }

    private static bool TryResolveDeploymentModeFromEnvironment(out DeploymentMode mode)
    {
        string? raw = Environment.GetEnvironmentVariable(DeploymentModeEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(raw))
        {
            mode = default;
            return false;
        }

        string normalized = raw.Trim().ToLowerInvariant();
        mode = normalized switch
        {
            "usb" => DeploymentMode.Usb,
            "iso" => DeploymentMode.Iso,
            _ => default
        };

        return normalized is "usb" or "iso";
    }

    private static string? TryGetUsbCacheRuntimeRoot()
    {
        foreach (DriveInfo drive in DriveInfo.GetDrives())
        {
            if (!drive.IsReady)
            {
                continue;
            }

            try
            {
                if (string.Equals(drive.VolumeLabel, CacheVolumeLabel, StringComparison.OrdinalIgnoreCase))
                {
                    return Path.Combine(drive.RootDirectory.FullName, RuntimeFolderName);
                }
            }
            catch
            {
                // Ignore drives that cannot expose a label.
            }

            string markerPath = Path.Combine(drive.RootDirectory.FullName, CacheMarkerFolderName);
            if (Directory.Exists(markerPath))
            {
                return Path.Combine(drive.RootDirectory.FullName, RuntimeFolderName);
            }
        }

        return null;
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

    private bool ConfirmDestructiveDeployment(TargetDiskInfo targetDisk, OperatingSystemCatalogItem operatingSystem)
    {
        string sizeGiB = targetDisk.SizeBytes > 0
            ? $"{(targetDisk.SizeBytes / 1024d / 1024d / 1024d):0.0} GiB"
            : "Unknown size";

        string message =
            "This operation will ERASE the selected disk and apply a new operating system." + Environment.NewLine +
            Environment.NewLine +
            $"Disk: {targetDisk.DiskNumber}" + Environment.NewLine +
            $"Model: {targetDisk.FriendlyName}" + Environment.NewLine +
            $"Bus: {targetDisk.BusType}" + Environment.NewLine +
            $"Size: {sizeGiB}" + Environment.NewLine +
            Environment.NewLine +
            $"OS: {operatingSystem.DisplayLabel}" + Environment.NewLine +
            "Continue?";

        return _applicationShellService.ConfirmWarning("Confirm Disk Erase", message);
    }

    private static TargetDiskInfo BuildDebugVirtualDisk()
    {
        return new TargetDiskInfo
        {
            DiskNumber = 999,
            FriendlyName = "DEBUG VIRTUAL TARGET",
            SerialNumber = "DEBUG-ONLY",
            BusType = "Virtual",
            PartitionStyle = "GPT",
            SizeBytes = 128UL * 1024UL * 1024UL * 1024UL,
            IsSystem = false,
            IsBoot = false,
            IsReadOnly = false,
            IsOffline = false,
            IsRemovable = false,
            IsSelectable = true,
            SelectionWarning = string.Empty
        };
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        OperatingSystemCatalog.StateChanged -= OnOperatingSystemCatalogStateChanged;
        Session.Dispose();
        _isDisposed = true;
    }

}
