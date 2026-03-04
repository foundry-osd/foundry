using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net.NetworkInformation;
using System.Reflection;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Foundry.Deploy;
using Foundry.Deploy.Models;
using Foundry.Deploy.Services.ApplicationShell;
using Foundry.Deploy.Services.Catalog;
using Foundry.Deploy.Services.Deployment;
using Foundry.Deploy.Services.DriverPacks;
using Foundry.Deploy.Services.Hardware;
using Foundry.Deploy.Services.Logging;
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
    private const string DefaultWindowsRelease = "11";
    private const string DefaultReleaseId = "25H2";
    private const string DefaultLicenseChannel = "RET";
    private const string DefaultEdition = "Pro";
    private const string FallbackLanguageCode = "en-us";
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
    private static readonly string DefaultLanguageCode = ResolveDefaultLanguageCode();
    private static readonly string[] RetailEditionOptions =
    [
        "Home",
        "Home N",
        "Home Single Language",
        "Education",
        "Education N",
        "Pro",
        "Pro N",
        "Enterprise",
        "Enterprise N"
    ];
    private static readonly string[] VolumeEditionOptions =
    [
        "Education",
        "Education N",
        "Pro",
        "Pro N",
        "Enterprise",
        "Enterprise N"
    ];
    private static readonly string[] Arm64RetailEditionOptions =
    [
        "Home",
        "Pro",
        "Enterprise"
    ];
    private static readonly string[] Arm64VolumeEditionOptions =
    [
        "Pro",
        "Enterprise"
    ];
    private readonly IThemeService _themeService;
    private readonly IApplicationShellService _applicationShellService;
    private readonly IOperationProgressService _operationProgressService;
    private readonly IOperatingSystemCatalogService _operatingSystemCatalogService;
    private readonly IDriverPackCatalogService _driverPackCatalogService;
    private readonly IDeploymentOrchestrator _deploymentOrchestrator;
    private readonly IHardwareProfileService _hardwareProfileService;
    private readonly IOfflineWindowsComputerNameService _offlineWindowsComputerNameService;
    private readonly ITargetDiskService _targetDiskService;
    private readonly IDriverPackSelectionService _driverPackSelectionService;
    private readonly IProcessRunner _processRunner;
    private readonly ILogger<MainWindowViewModel> _logger;
    private readonly Dispatcher _dispatcher;
    private readonly DeploymentMode _resolvedDeploymentMode;
    private readonly string? _resolvedUsbCacheRuntimeRoot;
    private HardwareProfile? _detectedHardware;
    private DispatcherTimer? _elapsedTimeTimer;
    private DispatcherTimer? _rebootCountdownTimer;
    private DateTimeOffset? _deploymentStartTimeUtc;
    private int _activeStepIndex;
    private string _lastLogsDirectoryPath = string.Empty;
    private bool _isUpdatingOsFilters;
    private bool _isUpdatingDriverPackOptionSelection;
    private bool _hasUserSelectedDriverPackOption;
    private bool _isRebootInProgress;
    private bool _isDisposed;

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
    [NotifyPropertyChangedFor(nameof(IsWizardPage))]
    [NotifyPropertyChangedFor(nameof(IsProgressPage))]
    [NotifyPropertyChangedFor(nameof(IsSuccessPage))]
    [NotifyPropertyChangedFor(nameof(IsErrorPage))]
    [NotifyCanExecuteChangedFor(nameof(RebootNowCommand))]
    private DeploymentPage currentPage = DeploymentPage.Wizard;

    [ObservableProperty]
    private string deploymentStatus = "Ready";

    [ObservableProperty]
    private int deploymentProgress;

    [ObservableProperty]
    private bool isGlobalProgressIndeterminate = true;

    [ObservableProperty]
    private string globalProgressPercentText = "0%";

    [ObservableProperty]
    private string currentStepName = "Waiting for deployment...";

    [ObservableProperty]
    private string stepCounterText = "Step: ? of ?";

    [ObservableProperty]
    private double currentStepProgress;

    [ObservableProperty]
    private bool isCurrentStepProgressIndeterminate = true;

    [ObservableProperty]
    private string currentStepProgressText = "Waiting for progress...";

    [ObservableProperty]
    private string computerNameText = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartDeploymentCommand))]
    private string targetComputerName = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasTargetComputerNameValidationError))]
    private string targetComputerNameValidationMessage = string.Empty;

    [ObservableProperty]
    private string ipAddress = "N/A";

    [ObservableProperty]
    private string subnetMask = "N/A";

    [ObservableProperty]
    private string gatewayAddress = "N/A";

    [ObservableProperty]
    private string macAddress = "N/A";

    [ObservableProperty]
    private string startTimeText = "N/A";

    [ObservableProperty]
    private string elapsedTimeText = "00:00:00";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RebootNowCommand))]
    private int rebootCountdownSeconds = 10;

    [ObservableProperty]
    private string failedStepName = string.Empty;

    [ObservableProperty]
    private string failedStepErrorMessage = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(NextWizardStepCommand))]
    [NotifyCanExecuteChangedFor(nameof(StartDeploymentCommand))]
    private OperatingSystemCatalogItem? selectedOperatingSystem;

    [ObservableProperty]
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
    private bool useFullAutopilot = true;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartDeploymentCommand))]
    private bool allowAutopilotDeferredCompletion = true;

    [ObservableProperty]
    private string detectedHardwareSummary = "Detecting hardware...";

    [ObservableProperty]
    private string effectiveOsArchitecture = NormalizeArchitecture(Environment.GetEnvironmentVariable("PROCESSOR_ARCHITECTURE") ?? string.Empty);

    [ObservableProperty]
    private string selectedWindowsRelease = DefaultWindowsRelease;

    [ObservableProperty]
    private string selectedReleaseId = DefaultReleaseId;

    [ObservableProperty]
    private string selectedLanguageCode = DefaultLanguageCode;

    [ObservableProperty]
    private string selectedLicenseChannel = DefaultLicenseChannel;

    [ObservableProperty]
    private string selectedEdition = DefaultEdition;

    public ObservableCollection<OperatingSystemCatalogItem> OperatingSystems { get; } = [];
    public ObservableCollection<string> WindowsReleaseFilters { get; } = [];
    public ObservableCollection<string> ReleaseIdFilters { get; } = [];
    public ObservableCollection<string> LanguageFilters { get; } = [];
    public ObservableCollection<string> LicenseChannelFilters { get; } = [];
    public ObservableCollection<string> EditionFilters { get; } = [];
    public ObservableCollection<DriverPackCatalogItem> DriverPacks { get; } = [];
    public ObservableCollection<DriverPackOptionItem> DriverPackOptions { get; } = [];
    public ObservableCollection<string> DriverPackModelOptions { get; } = [];
    public ObservableCollection<string> DriverPackVersionOptions { get; } = [];
    public ObservableCollection<TargetDiskInfo> TargetDisks { get; } = [];

    public DeployThemeMode CurrentTheme => _themeService.CurrentTheme;
    public bool IsDebugSafeMode => DebugSafetyMode.IsEnabled;
    public bool IsWizardPage => CurrentPage == DeploymentPage.Wizard;
    public bool IsProgressPage => CurrentPage == DeploymentPage.Progress;
    public bool IsSuccessPage => CurrentPage == DeploymentPage.Success;
    public bool IsErrorPage => CurrentPage == DeploymentPage.Error;
    public string DriverPackModeDisplay => SelectedDriverPackOption?.Kind switch
    {
        DriverPackSelectionKind.MicrosoftUpdateCatalog => "Microsoft Update Catalog",
        DriverPackSelectionKind.OemCatalog => "OEM Driver Pack",
        _ => "None"
    };
    public bool IsOemDriverSourceSelected => SelectedDriverPackOption?.Kind == DriverPackSelectionKind.OemCatalog;
    public bool IsDriverPackModelSelectionEnabled => IsOemDriverSourceSelected && DriverPackModelOptions.Count > 0;
    public bool IsDriverPackVersionSelectionEnabled => IsDriverPackModelSelectionEnabled && DriverPackVersionOptions.Count > 0;
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
        _operationProgressService = operationProgressService;
        _operatingSystemCatalogService = operatingSystemCatalogService;
        _driverPackCatalogService = driverPackCatalogService;
        _deploymentOrchestrator = deploymentOrchestrator;
        _hardwareProfileService = hardwareProfileService;
        _offlineWindowsComputerNameService = offlineWindowsComputerNameService;
        _targetDiskService = targetDiskService;
        _driverPackSelectionService = driverPackSelectionService;
        _processRunner = processRunner;
        _logger = logger;
        _dispatcher = Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;
        (DeploymentMode resolvedMode, string? resolvedUsbCacheRuntimeRoot) = ResolveDeploymentRuntimeContext();
        _resolvedDeploymentMode = resolvedMode;
        _resolvedUsbCacheRuntimeRoot = resolvedUsbCacheRuntimeRoot;

        _operationProgressService.ProgressChanged += OnOperationProgressChanged;
        _deploymentOrchestrator.StepProgressChanged += OnStepProgressChanged;

        EnsureCachePathForMode();

        if (IsDebugSafeMode)
        {
            DeploymentStatus = "Debug Safe Mode enabled: deployment actions are simulated.";
        }

        _ = LoadOfflineComputerNameAsync();
        _ = LoadHardwareProfileAsync();
        _ = RefreshTargetDisksAsync();
        _ = RefreshCatalogsAsync();
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
        DeploymentStatus = "Loading catalogs...";

        try
        {
            IReadOnlyList<OperatingSystemCatalogItem> operatingSystems =
                await _operatingSystemCatalogService.GetCatalogAsync().ConfigureAwait(false);
            IReadOnlyList<DriverPackCatalogItem> driverPacks =
                await _driverPackCatalogService.GetCatalogAsync().ConfigureAwait(false);

            RunOnUi(() =>
            {
                OperatingSystems.Clear();
                foreach (OperatingSystemCatalogItem item in operatingSystems)
                {
                    OperatingSystems.Add(item);
                }

                DriverPacks.Clear();
                foreach (DriverPackCatalogItem item in driverPacks)
                {
                    DriverPacks.Add(item);
                }

                RefreshOsFilterOptions();
                ApplyOsFilter();
                RefreshDriverPackOptions();

                DeploymentStatus = $"Catalogs loaded: {OperatingSystems.Count} OS entries, {DriverPacks.Count} driver packs.";
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Catalog refresh failed.");
            RunOnUi(() => DeploymentStatus = $"Catalog load failed: {ex.Message}");
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
        DeploymentStatus = "Loading target disks...";

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
                    DeploymentStatus = "No disks detected.";
                    return;
                }

                TargetDiskInfo? currentSelection = SelectedTargetDisk is null
                    ? null
                    : TargetDisks.FirstOrDefault(item => item.DiskNumber == SelectedTargetDisk.DiskNumber);

                SelectedTargetDisk = currentSelection
                    ?? TargetDisks.FirstOrDefault(item => item.IsSelectable)
                    ?? (IsDebugSafeMode ? TargetDisks.FirstOrDefault(item => item.DiskNumber == BuildDebugVirtualDisk().DiskNumber) : null)
                    ?? TargetDisks.FirstOrDefault();

                DeploymentStatus = $"Target disks loaded: {TargetDisks.Count} detected.";
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Target disk discovery failed.");
            RunOnUi(() => DeploymentStatus = $"Target disk discovery failed: {ex.Message}");
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
        if (SelectedOperatingSystem is null)
        {
            return;
        }

        string normalizedComputerName = ComputerNameRules.Normalize(TargetComputerName);
        if (!ComputerNameRules.IsValid(normalizedComputerName))
        {
            DeploymentStatus = "Enter a valid computer name.";
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
            DeploymentStatus = "Select a target disk.";
            return;
        }

        if (!IsDebugSafeMode && !effectiveTargetDisk.IsSelectable)
        {
            DeploymentStatus = $"Selected disk is blocked: {effectiveTargetDisk.SelectionWarning}";
            return;
        }

        if (!IsDebugSafeMode && !ConfirmDestructiveDeployment(effectiveTargetDisk, SelectedOperatingSystem))
        {
            DeploymentStatus = "Deployment cancelled by user.";
            return;
        }

        DriverPackSelectionKind effectiveDriverPackKind = SelectedDriverPackOption?.Kind ?? DriverPackSelectionKind.None;
        DriverPackCatalogItem? effectiveDriverPack = ResolveEffectiveDriverPackSelection();

        if (effectiveDriverPackKind == DriverPackSelectionKind.OemCatalog &&
            effectiveDriverPack is null)
        {
            DeploymentStatus = "Select a valid OEM model/version before starting deployment.";
            return;
        }

        EnsureCachePathForMode();
        RunOnUi(() =>
        {
            IsDeploymentRunning = true;
            ClearFailureDetails();
            InitializeProgressState();
            CurrentPage = DeploymentPage.Progress;
            _lastLogsDirectoryPath = string.Empty;
            DeploymentStatus = "Deployment started.";
        });

        DeploymentContext context = new()
        {
            Mode = _resolvedDeploymentMode,
            CacheRootPath = CacheRootPath,
            TargetDiskNumber = effectiveTargetDisk.DiskNumber,
            TargetComputerName = normalizedComputerName,
            OperatingSystem = SelectedOperatingSystem,
            DriverPackSelectionKind = effectiveDriverPackKind,
            DriverPack = effectiveDriverPack,
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
                _lastLogsDirectoryPath = result.LogsDirectoryPath;
                if (result.IsSuccess)
                {
                    DeploymentStatus = "Deployment completed.";
                    CurrentPage = DeploymentPage.Success;
                    return;
                }

                string fallbackStep = string.IsNullOrWhiteSpace(FailedStepName)
                    ? CurrentStepName
                    : FailedStepName;
                string fallbackMessage = string.IsNullOrWhiteSpace(FailedStepErrorMessage)
                    ? result.Message
                    : FailedStepErrorMessage;
                SetFailureDetails(fallbackStep, fallbackMessage);
                DeploymentStatus = $"Deployment failed: {result.Message}";
                CurrentPage = DeploymentPage.Error;
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
                SetFailureDetails(CurrentStepName, ex.Message);
                DeploymentStatus = $"Deployment failed: {ex.Message}";
                CurrentPage = DeploymentPage.Error;
            });
        }
        finally
        {
            RunOnUi(() =>
            {
                IsDeploymentRunning = false;
                StopElapsedTimeTracking();
            });
        }
    }

    [RelayCommand]
    private void OpenLogFile()
    {
        try
        {
            string logFilePath = ResolveEffectiveLogFilePath();
            ProcessStartInfo startInfo = new("notepad.exe")
            {
                UseShellExecute = false
            };
            startInfo.ArgumentList.Add(logFilePath);
            _ = Process.Start(startInfo);
            _logger.LogInformation("Opened log file at {LogFilePath}.", logFilePath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to open log file.");
            DeploymentStatus = $"Unable to open log file: {ex.Message}";
        }
    }

    [RelayCommand(CanExecute = nameof(CanRebootNow))]
    private async Task RebootNowAsync()
    {
        await ExecuteRebootAsync("Manual reboot requested.").ConfigureAwait(false);
    }

    [RelayCommand(CanExecute = nameof(CanShowDebugPages))]
    private void ShowDebugProgressPage()
    {
        ClearFailureDetails();
        InitializeProgressState();
        DeploymentProgress = 42;
        UpdateGlobalProgressVisuals(DeploymentProgress);
        ComputerNameText = TargetComputerName;
        CurrentStepName = DeploymentStepNames.ApplyOperatingSystemImage;
        StepCounterText = BuildStepCounterText(8);
        CurrentStepProgress = 65;
        IsCurrentStepProgressIndeterminate = false;
        CurrentStepProgressText = "Applying image: 65%";
        DeploymentStatus = "Debug preview: progress page.";
        CurrentPage = DeploymentPage.Progress;
    }

    [RelayCommand(CanExecute = nameof(CanShowDebugPages))]
    private void ShowDebugSuccessPage()
    {
        StopElapsedTimeTracking();
        ClearFailureDetails();
        DeploymentProgress = 100;
        UpdateGlobalProgressVisuals(DeploymentProgress);
        ComputerNameText = TargetComputerName;
        CurrentStepName = DeploymentStepNames.FinalizeDeploymentAndWriteLogs;
        StepCounterText = BuildStepCounterText(_deploymentOrchestrator.PlannedSteps.Count);
        CurrentStepProgress = 100;
        IsCurrentStepProgressIndeterminate = false;
        CurrentStepProgressText = "Step completed.";
        DeploymentStatus = "Debug preview: success page.";
        CurrentPage = DeploymentPage.Success;
    }

    [RelayCommand(CanExecute = nameof(CanShowDebugPages))]
    private void ShowDebugErrorPage()
    {
        StopElapsedTimeTracking();
        ComputerNameText = TargetComputerName;
        StepCounterText = BuildStepCounterText(8);
        SetFailureDetails(
            DeploymentStepNames.ApplyOperatingSystemImage,
            "Debug preview: DISM apply failed because the target partition is read-only.\n\n" +
            "ErrorCode=0x80070005\n" +
            "Details: Access denied while mounting image to target path.\n" +
            "Action: Verify disk attributes and retry deployment.");
        DeploymentStatus = "Debug preview: error page.";
        CurrentPage = DeploymentPage.Error;
    }

    private string ResolveEffectiveLogFilePath()
    {
        if (!string.IsNullOrWhiteSpace(_lastLogsDirectoryPath))
        {
            return Path.Combine(_lastLogsDirectoryPath, FoundryDeployLogging.LogFileName);
        }

        return FoundryDeployLogging.ResolveStartupLogFilePath();
    }

    partial void OnSelectedOperatingSystemChanged(OperatingSystemCatalogItem? value)
    {
        RefreshDriverPackOptions();
    }

    partial void OnTargetComputerNameChanged(string value)
    {
        string normalized = ComputerNameRules.Normalize(value);
        if (!normalized.Equals(value, StringComparison.Ordinal))
        {
            TargetComputerName = normalized;
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

    partial void OnEffectiveOsArchitectureChanged(string value)
    {
        if (_isUpdatingOsFilters)
        {
            return;
        }

        RefreshOsFilterOptions();
        ApplyOsFilter();
        RefreshDriverPackOptions();
    }

    partial void OnSelectedWindowsReleaseChanged(string value)
    {
        HandleOsFilterSelectionChanged();
    }

    partial void OnSelectedReleaseIdChanged(string value)
    {
        HandleOsFilterSelectionChanged();
    }

    partial void OnSelectedLanguageCodeChanged(string value)
    {
        HandleOsFilterSelectionChanged();
    }

    partial void OnSelectedLicenseChannelChanged(string value)
    {
        HandleOsFilterSelectionChanged();
    }

    partial void OnSelectedEditionChanged(string value)
    {
        HandleOsFilterSelectionChanged();
    }

    private void HandleOsFilterSelectionChanged()
    {
        if (_isUpdatingOsFilters)
        {
            return;
        }

        RefreshOsFilterOptions();
        ApplyOsFilter();
    }

    partial void OnAutoSelectDriverPackWhenEmptyChanged(bool value)
    {
        RefreshDriverPackOptions();
    }

    partial void OnSelectedTargetDiskChanged(TargetDiskInfo? value)
    {
        if (value is null)
        {
            return;
        }

        if (!IsDebugSafeMode && !value.IsSelectable)
        {
            DeploymentStatus = $"Selected disk blocked: {value.SelectionWarning}";
        }
    }

    partial void OnCurrentPageChanged(DeploymentPage value)
    {
        if (value == DeploymentPage.Success)
        {
            StartRebootCountdown();
        }
        else
        {
            StopRebootCountdown(resetSeconds: true);
        }

        if (value != DeploymentPage.Progress && !IsDeploymentRunning)
        {
            StopElapsedTimeTracking();
        }

        RebootNowCommand.NotifyCanExecuteChanged();
    }

    private void OnOperationProgressChanged(object? sender, EventArgs e)
    {
        RunOnUi(() =>
        {
            if (!IsDeploymentRunning &&
                !_operationProgressService.IsOperationInProgress &&
                string.IsNullOrWhiteSpace(_operationProgressService.Status))
            {
                return;
            }

            int normalizedProgress = Math.Clamp(_operationProgressService.Progress, 0, 100);
            DeploymentProgress = Math.Max(DeploymentProgress, normalizedProgress);
            UpdateGlobalProgressVisuals(DeploymentProgress);

            if (!string.IsNullOrWhiteSpace(_operationProgressService.Status))
            {
                DeploymentStatus = _operationProgressService.Status!;
            }
        });
    }

    private void OnStepProgressChanged(object? sender, DeploymentStepProgress e)
    {
        RunOnUi(() =>
        {
            if (e.StepIndex != _activeStepIndex)
            {
                _activeStepIndex = e.StepIndex;
                CurrentStepProgress = 0;
                IsCurrentStepProgressIndeterminate = true;
                CurrentStepProgressText = "Starting step...";
            }

            CurrentStepName = e.StepName;
            StepCounterText = $"Step: {e.StepIndex} of {e.StepCount}";

            DeploymentProgress = Math.Max(DeploymentProgress, e.ProgressPercent);
            UpdateGlobalProgressVisuals(DeploymentProgress);
            UpdateCurrentStepProgressVisuals(e);

            if (!string.IsNullOrWhiteSpace(e.Message))
            {
                DeploymentStatus = e.Message;
            }

            if (e.State == DeploymentStepState.Failed)
            {
                SetFailureDetails(e.StepName, e.Message ?? "Step failed.");
            }
        });
    }

    private void InitializeProgressState()
    {
        _activeStepIndex = 0;
        DeploymentProgress = 0;
        UpdateGlobalProgressVisuals(0);

        CurrentStepName = "Preparing deployment...";
        CurrentStepProgress = 0;
        IsCurrentStepProgressIndeterminate = true;
        CurrentStepProgressText = "Waiting for progress...";

        StepCounterText = BuildStepCounterText(0);
        ComputerNameText = TargetComputerName;
        CaptureNetworkSnapshot();

        _deploymentStartTimeUtc = DateTimeOffset.Now;
        StartTimeText = _deploymentStartTimeUtc.Value.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
        ElapsedTimeText = "00:00:00";
        StartElapsedTimeTracking();
    }

    private void UpdateGlobalProgressVisuals(int progressValue)
    {
        int clampedProgress = Math.Clamp(progressValue, 0, 100);
        GlobalProgressPercentText = $"{clampedProgress}%";
        IsGlobalProgressIndeterminate = IsDeploymentRunning && clampedProgress <= 0;
    }

    private void UpdateCurrentStepProgressVisuals(DeploymentStepProgress stepProgress)
    {
        if (stepProgress.State == DeploymentStepState.Succeeded)
        {
            CurrentStepProgress = 100;
            IsCurrentStepProgressIndeterminate = false;
            CurrentStepProgressText = stepProgress.StepSubProgressLabel ?? "Step completed.";
            return;
        }

        if (stepProgress.State == DeploymentStepState.Failed)
        {
            IsCurrentStepProgressIndeterminate = false;
            CurrentStepProgressText = stepProgress.Message ?? "Step failed.";
            return;
        }

        if (stepProgress.State == DeploymentStepState.Skipped)
        {
            IsCurrentStepProgressIndeterminate = false;
            CurrentStepProgressText = stepProgress.Message ?? "Step skipped.";
            return;
        }

        if (stepProgress.StepSubProgressPercent.HasValue)
        {
            double normalized = Math.Clamp(stepProgress.StepSubProgressPercent.Value, 0d, 100d);
            CurrentStepProgress = normalized;
            IsCurrentStepProgressIndeterminate = false;
            CurrentStepProgressText = string.IsNullOrWhiteSpace(stepProgress.StepSubProgressLabel)
                ? $"{normalized:0.#}%"
                : stepProgress.StepSubProgressLabel!;
            return;
        }

        if (stepProgress.StepSubProgressIndeterminate)
        {
            IsCurrentStepProgressIndeterminate = true;
            CurrentStepProgressText = stepProgress.StepSubProgressLabel ?? "In progress...";
        }
    }

    private void StartElapsedTimeTracking()
    {
        StopElapsedTimeTracking();
        _elapsedTimeTimer = new DispatcherTimer(DispatcherPriority.Background, _dispatcher)
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _elapsedTimeTimer.Tick += OnElapsedTimeTick;
        _elapsedTimeTimer.Start();
    }

    private void StopElapsedTimeTracking()
    {
        if (_elapsedTimeTimer is null)
        {
            return;
        }

        _elapsedTimeTimer.Tick -= OnElapsedTimeTick;
        _elapsedTimeTimer.Stop();
        _elapsedTimeTimer = null;
    }

    private void OnElapsedTimeTick(object? sender, EventArgs e)
    {
        if (!_deploymentStartTimeUtc.HasValue)
        {
            return;
        }

        TimeSpan elapsed = DateTimeOffset.Now - _deploymentStartTimeUtc.Value;
        ElapsedTimeText = elapsed.ToString(@"hh\:mm\:ss", CultureInfo.InvariantCulture);
    }

    private void StartRebootCountdown()
    {
        StopRebootCountdown(resetSeconds: false);
        RebootCountdownSeconds = 10;
        _rebootCountdownTimer = new DispatcherTimer(DispatcherPriority.Background, _dispatcher)
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _rebootCountdownTimer.Tick += OnRebootCountdownTick;
        _rebootCountdownTimer.Start();
    }

    private void StopRebootCountdown(bool resetSeconds)
    {
        if (_rebootCountdownTimer is not null)
        {
            _rebootCountdownTimer.Tick -= OnRebootCountdownTick;
            _rebootCountdownTimer.Stop();
            _rebootCountdownTimer = null;
        }

        if (resetSeconds)
        {
            RebootCountdownSeconds = 10;
        }
    }

    private void OnRebootCountdownTick(object? sender, EventArgs e)
    {
        if (RebootCountdownSeconds > 0)
        {
            RebootCountdownSeconds--;
        }

        if (RebootCountdownSeconds > 0)
        {
            return;
        }

        StopRebootCountdown(resetSeconds: false);
        if (!IsDebugSafeMode)
        {
            _ = ExecuteRebootAsync("Automatic reboot countdown completed.");
        }
    }

    private bool CanRebootNow()
    {
        return IsSuccessPage && !IsDebugSafeMode && !_isRebootInProgress;
    }

    private bool CanShowDebugPages()
    {
        return IsDebugSafeMode && !IsDeploymentRunning;
    }

    private async Task ExecuteRebootAsync(string reason)
    {
        if (IsDebugSafeMode || _isRebootInProgress)
        {
            return;
        }

        _isRebootInProgress = true;
        RebootNowCommand.NotifyCanExecuteChanged();
        StopRebootCountdown(resetSeconds: false);

        try
        {
            string rebootExecutablePath = Path.Combine(Environment.SystemDirectory, "wpeutil.exe");
            if (!File.Exists(rebootExecutablePath))
            {
                throw new FileNotFoundException("Required reboot executable 'wpeutil.exe' was not found.", rebootExecutablePath);
            }

            DeploymentStatus = "Rebooting now...";

            ProcessExecutionResult result = await _processRunner
                .RunAsync(rebootExecutablePath, "Reboot", Path.GetTempPath())
                .ConfigureAwait(false);

            if (result.ExitCode == 0)
            {
                return;
            }

            RunOnUi(() =>
            {
                string diagnostic = string.IsNullOrWhiteSpace(result.StandardError)
                    ? result.StandardOutput
                    : result.StandardError;
                SetFailureDetails("System reboot", $"wpeutil.exe failed with exit code {result.ExitCode}. {diagnostic}".Trim());
                DeploymentStatus = "Reboot command failed.";
                CurrentPage = DeploymentPage.Error;
            });
        }
        catch (Exception ex)
        {
            RunOnUi(() =>
            {
                SetFailureDetails("System reboot", ex.Message);
                DeploymentStatus = $"Reboot command failed: {ex.Message}";
                CurrentPage = DeploymentPage.Error;
            });
        }
        finally
        {
            RunOnUi(() =>
            {
                _isRebootInProgress = false;
                RebootNowCommand.NotifyCanExecuteChanged();
            });
        }
    }

    private void SetFailureDetails(string? stepName, string? errorMessage)
    {
        FailedStepName = string.IsNullOrWhiteSpace(stepName) ? "Unknown step" : stepName;
        FailedStepErrorMessage = string.IsNullOrWhiteSpace(errorMessage) ? "No error details were provided." : errorMessage;
    }

    private void ClearFailureDetails()
    {
        FailedStepName = string.Empty;
        FailedStepErrorMessage = string.Empty;
    }

    private void CaptureNetworkSnapshot()
    {
        IpAddress = "N/A";
        SubnetMask = "N/A";
        GatewayAddress = "N/A";
        MacAddress = "N/A";

        try
        {
            foreach (NetworkInterface networkInterface in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (networkInterface.OperationalStatus != OperationalStatus.Up)
                {
                    continue;
                }

                if (networkInterface.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel)
                {
                    continue;
                }

                IPInterfaceProperties ipProperties = networkInterface.GetIPProperties();
                UnicastIPAddressInformation? ipv4AddressInfo = ipProperties.UnicastAddresses
                    .FirstOrDefault(item => item.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork);
                if (ipv4AddressInfo is null)
                {
                    continue;
                }

                GatewayIPAddressInformation? gatewayInfo = ipProperties.GatewayAddresses
                    .FirstOrDefault(item => item.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork);
                byte[] macBytes = networkInterface.GetPhysicalAddress().GetAddressBytes();

                IpAddress = ipv4AddressInfo.Address.ToString();
                SubnetMask = ipv4AddressInfo.IPv4Mask?.ToString() ?? "N/A";
                GatewayAddress = gatewayInfo?.Address.ToString() ?? "N/A";
                MacAddress = macBytes.Length == 0
                    ? "N/A"
                    : string.Join("-", macBytes.Select(value => value.ToString("X2", CultureInfo.InvariantCulture)));
                return;
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Unable to resolve network snapshot for the progress page.");
        }
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
               OperatingSystems.Count > 0 &&
               WindowsReleaseFilters.Count > 0 &&
               ReleaseIdFilters.Count > 0 &&
               LanguageFilters.Count > 0 &&
               LicenseChannelFilters.Count > 0 &&
               EditionFilters.Count > 0 &&
               SelectedOperatingSystem is not null;
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
               SelectedOperatingSystem is not null &&
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

    private void ApplyOsFilter()
    {
        OperatingSystemCatalogItem[] filtered = BuildFilteredOperatingSystems();

        OperatingSystemCatalogItem? matchingCurrent = SelectedOperatingSystem is null
            ? null
            : filtered.FirstOrDefault(item => IsSameOperatingSystemMedia(item, SelectedOperatingSystem));

        OperatingSystemCatalogItem? selected = matchingCurrent ?? filtered.FirstOrDefault();
        if (selected is null)
        {
            SelectedOperatingSystem = null;
            return;
        }

        SelectedOperatingSystem = ApplyEditionSelection(selected);
    }

    private void RefreshOsFilterOptions()
    {
        _isUpdatingOsFilters = true;

        try
        {
            string previousWindowsRelease = SelectedWindowsRelease;
            string previousReleaseId = SelectedReleaseId;
            string previousLanguageCode = SelectedLanguageCode;
            string previousLicenseChannel = SelectedLicenseChannel;
            string previousEdition = SelectedEdition;

            IEnumerable<OperatingSystemCatalogItem> baseQuery = BuildOsQueryWithArchitecture(OperatingSystems);

            SelectedWindowsRelease = UpdateFilterSelection(
                WindowsReleaseFilters,
                baseQuery.Select(item => item.WindowsRelease),
                previousWindowsRelease,
                DefaultWindowsRelease,
                selectFirstWhenNoMatch: true);

            IEnumerable<OperatingSystemCatalogItem> releaseScope = ApplyWindowsReleaseFilter(baseQuery);
            SelectedReleaseId = UpdateFilterSelection(
                ReleaseIdFilters,
                releaseScope.Select(item => item.ReleaseId),
                previousReleaseId,
                DefaultReleaseId,
                selectFirstWhenNoMatch: true);

            IEnumerable<OperatingSystemCatalogItem> languageScope = ApplyReleaseIdFilter(releaseScope);
            SelectedLanguageCode = UpdateLanguageFilterSelection(
                LanguageFilters,
                languageScope.Select(GetLanguageFilterValue),
                previousLanguageCode);

            IEnumerable<OperatingSystemCatalogItem> licenseScope = ApplyLanguageFilter(languageScope);
            SelectedLicenseChannel = UpdateFilterSelection(
                LicenseChannelFilters,
                licenseScope.Select(item => item.LicenseChannel),
                previousLicenseChannel,
                DefaultLicenseChannel,
                selectFirstWhenNoMatch: true);

            IEnumerable<OperatingSystemCatalogItem> editionScope = ApplyLicenseChannelFilter(licenseScope);
            IEnumerable<string> recommendedEditions = BuildRecommendedEditionOptions(editionScope);
            SelectedEdition = UpdateFilterSelection(
                EditionFilters,
                recommendedEditions,
                previousEdition,
                DefaultEdition,
                selectFirstWhenNoMatch: true);
        }
        finally
        {
            _isUpdatingOsFilters = false;
        }
    }

    private IEnumerable<OperatingSystemCatalogItem> BuildOsQueryWithArchitecture(IEnumerable<OperatingSystemCatalogItem> source)
    {
        string architecture = NormalizeArchitecture(EffectiveOsArchitecture);
        if (string.IsNullOrWhiteSpace(architecture))
        {
            return source;
        }

        return source.Where(item => IsArchitectureMatch(item.Architecture, architecture));
    }

    private IEnumerable<OperatingSystemCatalogItem> ApplyWindowsReleaseFilter(IEnumerable<OperatingSystemCatalogItem> source)
    {
        return IsFilterUnset(SelectedWindowsRelease)
            ? source
            : source.Where(item => item.WindowsRelease.Equals(SelectedWindowsRelease, StringComparison.OrdinalIgnoreCase));
    }

    private IEnumerable<OperatingSystemCatalogItem> ApplyReleaseIdFilter(IEnumerable<OperatingSystemCatalogItem> source)
    {
        return IsFilterUnset(SelectedReleaseId)
            ? source
            : source.Where(item => item.ReleaseId.Equals(SelectedReleaseId, StringComparison.OrdinalIgnoreCase));
    }

    private IEnumerable<OperatingSystemCatalogItem> ApplyLanguageFilter(IEnumerable<OperatingSystemCatalogItem> source)
    {
        return IsFilterUnset(SelectedLanguageCode)
            ? source
            : source.Where(item => GetLanguageFilterValue(item).Equals(SelectedLanguageCode, StringComparison.OrdinalIgnoreCase));
    }

    private IEnumerable<OperatingSystemCatalogItem> ApplyLicenseChannelFilter(IEnumerable<OperatingSystemCatalogItem> source)
    {
        return IsFilterUnset(SelectedLicenseChannel)
            ? source
            : source.Where(item => item.LicenseChannel.Equals(SelectedLicenseChannel, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsFilterUnset(string value)
    {
        return string.IsNullOrWhiteSpace(value);
    }

    private static string GetLanguageFilterValue(OperatingSystemCatalogItem item)
    {
        return !string.IsNullOrWhiteSpace(item.LanguageCode)
            ? item.LanguageCode
            : item.Language;
    }

    private IEnumerable<string> BuildRecommendedEditionOptions(IEnumerable<OperatingSystemCatalogItem> scope)
    {
        string architecture = NormalizeArchitecture(EffectiveOsArchitecture);
        bool hasRetail = scope.Any(item => item.LicenseChannel.Equals("RET", StringComparison.OrdinalIgnoreCase));
        bool hasVolume = scope.Any(item => item.LicenseChannel.Equals("VOL", StringComparison.OrdinalIgnoreCase));

        List<string> recommended = [];

        if (IsFilterUnset(SelectedLicenseChannel))
        {
            if (hasRetail)
            {
                recommended.AddRange(GetEditionOptionsForChannel("RET", architecture));
            }

            if (hasVolume)
            {
                recommended.AddRange(GetEditionOptionsForChannel("VOL", architecture));
            }
        }
        else
        {
            recommended.AddRange(GetEditionOptionsForChannel(SelectedLicenseChannel, architecture));
        }

        // Fall back to raw catalog editions if no curated mapping applies.
        if (recommended.Count == 0)
        {
            recommended.AddRange(scope.Select(item => item.Edition));
        }

        return recommended;
    }

    private static IEnumerable<string> GetEditionOptionsForChannel(string licenseChannel, string architecture)
    {
        bool isArm64 = architecture.Equals("arm64", StringComparison.OrdinalIgnoreCase);

        if (licenseChannel.Equals("VOL", StringComparison.OrdinalIgnoreCase))
        {
            return isArm64 ? Arm64VolumeEditionOptions : VolumeEditionOptions;
        }

        if (licenseChannel.Equals("RET", StringComparison.OrdinalIgnoreCase))
        {
            return isArm64 ? Arm64RetailEditionOptions : RetailEditionOptions;
        }

        return [];
    }

    private static void UpdateFilterCollection(ObservableCollection<string> target, IEnumerable<string> values)
    {
        string[] normalizedValues = values
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        target.Clear();

        foreach (string value in normalizedValues)
        {
            target.Add(value);
        }
    }

    private static bool TryGetFilterSelection(string? selectedValue, ObservableCollection<string> options, out string matched)
    {
        matched = string.Empty;

        if (options.Count == 0 || IsFilterUnset(selectedValue ?? string.Empty))
        {
            return false;
        }

        string? matchingOption = options.FirstOrDefault(option =>
            option.Equals(selectedValue, StringComparison.OrdinalIgnoreCase));

        if (string.IsNullOrWhiteSpace(matchingOption))
        {
            return false;
        }

        matched = matchingOption;
        return true;
    }

    private static string UpdateFilterSelection(
        ObservableCollection<string> target,
        IEnumerable<string> values,
        string previousSelection,
        string? defaultSelection = null,
        bool selectFirstWhenNoMatch = false)
    {
        UpdateFilterCollection(target, values);

        if (target.Count == 0)
        {
            return string.Empty;
        }

        if (TryGetFilterSelection(previousSelection, target, out string selected))
        {
            return selected;
        }

        if (!string.IsNullOrWhiteSpace(defaultSelection) &&
            TryGetFilterSelection(defaultSelection, target, out selected))
        {
            return selected;
        }

        return selectFirstWhenNoMatch ? target[0] : string.Empty;
    }

    private static string UpdateLanguageFilterSelection(
        ObservableCollection<string> target,
        IEnumerable<string> values,
        string previousSelection)
    {
        UpdateFilterCollection(target, values);
        if (target.Count == 0)
        {
            return string.Empty;
        }

        foreach (string candidate in new[] { previousSelection, DefaultLanguageCode, FallbackLanguageCode })
        {
            string selected = EnsureLanguageSelection(candidate, target);
            if (!string.IsNullOrWhiteSpace(selected))
            {
                return selected;
            }
        }

        return target[0];
    }

    private static string EnsureLanguageSelection(string languageCode, ObservableCollection<string> options)
    {
        if (options.Count == 0)
        {
            return string.Empty;
        }

        if (IsFilterUnset(languageCode))
        {
            return string.Empty;
        }

        string normalized = NormalizeLanguageCode(languageCode);

        string? exact = options.FirstOrDefault(option =>
            NormalizeLanguageCode(option).Equals(normalized, StringComparison.OrdinalIgnoreCase));

        if (!string.IsNullOrWhiteSpace(exact))
        {
            return exact;
        }

        string neutral = normalized.Split('-', 2, StringSplitOptions.RemoveEmptyEntries)[0];
        if (!string.IsNullOrWhiteSpace(neutral))
        {
            string? sameLanguage = options.FirstOrDefault(option =>
            {
                string candidate = NormalizeLanguageCode(option);
                return candidate.Equals(neutral, StringComparison.OrdinalIgnoreCase) ||
                       candidate.StartsWith($"{neutral}-", StringComparison.OrdinalIgnoreCase);
            });

            if (!string.IsNullOrWhiteSpace(sameLanguage))
            {
                return sameLanguage;
            }
        }

        return string.Empty;
    }

    private static string ResolveDefaultLanguageCode()
    {
        string[] candidates =
        [
            CultureInfo.CurrentUICulture.Name,
            CultureInfo.CurrentCulture.Name,
            CultureInfo.InstalledUICulture.Name
        ];

        foreach (string candidate in candidates)
        {
            string normalized = NormalizeLanguageCode(candidate);
            if (!string.IsNullOrWhiteSpace(normalized))
            {
                return normalized;
            }
        }

        return FallbackLanguageCode;
    }

    private static string NormalizeLanguageCode(string languageCode)
    {
        return languageCode.Trim().Replace('_', '-').ToLowerInvariant();
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

    private OperatingSystemCatalogItem[] BuildFilteredOperatingSystems()
    {
        IEnumerable<OperatingSystemCatalogItem> query = BuildOsQueryWithArchitecture(OperatingSystems);
        query = ApplyWindowsReleaseFilter(query);
        query = ApplyReleaseIdFilter(query);
        query = ApplyLanguageFilter(query);
        query = ApplyLicenseChannelFilter(query);

        return query
            .GroupBy(
                item => string.IsNullOrWhiteSpace(item.Url) ? item.FileName : item.Url,
                StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
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
                EffectiveOsArchitecture = NormalizeArchitecture(profile.Architecture);
                DetectedHardwareSummary = $"{profile.DisplayLabel} | TPM: {(profile.IsTpmPresent ? "Yes" : "No")} | Autopilot: {(profile.IsAutopilotCapable ? "Capable" : "Needs checks")}";
                RefreshOsFilterOptions();
                ApplyOsFilter();
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

    private string BuildStepCounterText(int currentStep)
    {
        int plannedStepCount = _deploymentOrchestrator.PlannedSteps.Count;
        if (plannedStepCount <= 0)
        {
            return "Step: ? of ?";
        }

        int normalizedStep = Math.Clamp(currentStep, 0, plannedStepCount);
        return $"Step: {normalizedStep} of {plannedStepCount}";
    }

    private OperatingSystemCatalogItem ApplyEditionSelection(OperatingSystemCatalogItem item)
    {
        if (IsFilterUnset(SelectedEdition))
        {
            return item;
        }

        if (item.Edition.Equals(SelectedEdition, StringComparison.OrdinalIgnoreCase))
        {
            return item;
        }

        return item with { Edition = SelectedEdition };
    }

    private static bool IsSameOperatingSystemMedia(OperatingSystemCatalogItem left, OperatingSystemCatalogItem right)
    {
        string leftKey = string.IsNullOrWhiteSpace(left.Url) ? left.FileName : left.Url;
        string rightKey = string.IsNullOrWhiteSpace(right.Url) ? right.FileName : right.Url;
        return leftKey.Equals(rightKey, StringComparison.OrdinalIgnoreCase);
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

            TargetComputerName = effectiveName;
            ComputerNameText = effectiveName;
            TargetComputerNameValidationMessage = ComputerNameRules.GetValidationMessage(effectiveName);
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

        _operationProgressService.ProgressChanged -= OnOperationProgressChanged;
        _deploymentOrchestrator.StepProgressChanged -= OnStepProgressChanged;
        StopElapsedTimeTracking();
        StopRebootCountdown(resetSeconds: false);
        _isDisposed = true;
    }

}
