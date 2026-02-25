using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Foundry.Deploy.Models;
using Foundry.Deploy.Services.ApplicationShell;
using Foundry.Deploy.Services.Catalog;
using Foundry.Deploy.Services.Deployment;
using Foundry.Deploy.Services.DriverPacks;
using Foundry.Deploy.Services.Hardware;
using Foundry.Deploy.Services.Operations;
using Foundry.Deploy.Services.Runtime;
using Foundry.Deploy.Services.Theme;
using DeployThemeMode = Foundry.Deploy.Services.Theme.ThemeMode;

namespace Foundry.Deploy.ViewModels;

public partial class MainWindowViewModel : ObservableObject
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
    private const string WinPeLogsRoot = @"X:\Foundry\Logs";
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
    private readonly ITargetDiskService _targetDiskService;
    private readonly IDriverPackSelectionService _driverPackSelectionService;
    private readonly Dispatcher _dispatcher;
    private readonly DeploymentMode _resolvedDeploymentMode;
    private readonly string? _resolvedUsbCacheRuntimeRoot;
    private readonly Dictionary<string, DeploymentStepItemViewModel> _stepIndex = new(StringComparer.Ordinal);
    private HardwareProfile? _detectedHardware;
    private string _lastLogsDirectoryPath = string.Empty;
    private bool _isUpdatingOsFilters;
    private bool _isUpdatingDriverPackOptionSelection;
    private bool _hasUserSelectedDriverPackOption;

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
    private bool isDeploymentRunning;

    [ObservableProperty]
    private bool showProgressPage;

    [ObservableProperty]
    private string deploymentStatus = "Ready";

    [ObservableProperty]
    private int deploymentProgress;

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

    [ObservableProperty]
    private bool isAutopilotDeferred;

    [ObservableProperty]
    private string autopilotDeferredMessage = string.Empty;

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
    public ObservableCollection<DeploymentStepItemViewModel> DeploymentSteps { get; } = [];
    public ObservableCollection<string> DeploymentLogs { get; } = [];

    public DeployThemeMode CurrentTheme => _themeService.CurrentTheme;
    public bool IsDebugSafeMode => DebugSafetyMode.IsEnabled;
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

    public MainWindowViewModel(
        IThemeService themeService,
        IApplicationShellService applicationShellService,
        IOperationProgressService operationProgressService,
        IOperatingSystemCatalogService operatingSystemCatalogService,
        IDriverPackCatalogService driverPackCatalogService,
        IDeploymentOrchestrator deploymentOrchestrator,
        IHardwareProfileService hardwareProfileService,
        ITargetDiskService targetDiskService,
        IDriverPackSelectionService driverPackSelectionService)
    {
        _themeService = themeService;
        _applicationShellService = applicationShellService;
        _operationProgressService = operationProgressService;
        _operatingSystemCatalogService = operatingSystemCatalogService;
        _driverPackCatalogService = driverPackCatalogService;
        _deploymentOrchestrator = deploymentOrchestrator;
        _hardwareProfileService = hardwareProfileService;
        _targetDiskService = targetDiskService;
        _driverPackSelectionService = driverPackSelectionService;
        _dispatcher = Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;
        (DeploymentMode resolvedMode, string? resolvedUsbCacheRuntimeRoot) = ResolveDeploymentRuntimeContext();
        _resolvedDeploymentMode = resolvedMode;
        _resolvedUsbCacheRuntimeRoot = resolvedUsbCacheRuntimeRoot;

        _operationProgressService.ProgressChanged += OnOperationProgressChanged;
        _deploymentOrchestrator.StepProgressChanged += OnStepProgressChanged;
        _deploymentOrchestrator.LogEmitted += OnLogEmitted;

        EnsureCachePathForMode();

        if (IsDebugSafeMode)
        {
            DeploymentStatus = "Debug Safe Mode enabled: deployment actions are simulated.";
        }

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
        if (SelectedOperatingSystem is null)
        {
            return;
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

        EnsureCachePathForMode();
        InitializeProgressCollections();

        ShowProgressPage = true;
        IsDeploymentRunning = true;
        DeploymentProgress = 0;
        _lastLogsDirectoryPath = string.Empty;
        IsAutopilotDeferred = false;
        AutopilotDeferredMessage = string.Empty;
        DeploymentStatus = "Deployment started.";

        DriverPackSelectionKind effectiveDriverPackKind = SelectedDriverPackOption?.Kind ?? DriverPackSelectionKind.None;
        DriverPackCatalogItem? effectiveDriverPack = ResolveEffectiveDriverPackSelection();

        if (effectiveDriverPackKind == DriverPackSelectionKind.OemCatalog &&
            effectiveDriverPack is null)
        {
            DeploymentStatus = "Select a valid OEM model/version before starting deployment.";
            IsDeploymentRunning = false;
            ShowProgressPage = false;
            return;
        }

        DeploymentContext context = new()
        {
            Mode = _resolvedDeploymentMode,
            CacheRootPath = CacheRootPath,
            TargetDiskNumber = effectiveTargetDisk.DiskNumber,
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
                DeploymentStatus = result.IsSuccess
                    ? "Deployment completed."
                    : $"Deployment failed: {result.Message}";
            });
        }
        catch (Exception ex)
        {
            RunOnUi(() => DeploymentStatus = $"Deployment failed: {ex.Message}");
        }
        finally
        {
            RunOnUi(() => IsDeploymentRunning = false);
        }
    }

    [RelayCommand]
    private void OpenLogsFolder()
    {
        try
        {
            string logsPath = ResolveEffectiveLogsPath();
            Directory.CreateDirectory(logsPath);
            _ = Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = logsPath,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            DeploymentStatus = $"Unable to open logs folder: {ex.Message}";
        }
    }

    private string ResolveEffectiveLogsPath()
    {
        return string.IsNullOrWhiteSpace(_lastLogsDirectoryPath)
            ? WinPeLogsRoot
            : _lastLogsDirectoryPath;
    }

    partial void OnSelectedOperatingSystemChanged(OperatingSystemCatalogItem? value)
    {
        RefreshDriverPackOptions();
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

    private void OnOperationProgressChanged(object? sender, EventArgs e)
    {
        RunOnUi(() =>
        {
            DeploymentProgress = _operationProgressService.Progress;
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
            if (_stepIndex.TryGetValue(e.StepName, out DeploymentStepItemViewModel? vm))
            {
                vm.State = e.State;
                vm.Message = e.Message ?? string.Empty;
            }

            if (string.Equals(e.StepName, "Execute full Autopilot workflow", StringComparison.Ordinal) &&
                e.State == DeploymentStepState.Succeeded &&
                !string.IsNullOrWhiteSpace(e.Message) &&
                e.Message.Contains("deferred", StringComparison.OrdinalIgnoreCase))
            {
                IsAutopilotDeferred = true;
                AutopilotDeferredMessage = e.Message;
            }

            DeploymentProgress = Math.Max(DeploymentProgress, e.ProgressPercent);
        });
    }

    private void OnLogEmitted(object? sender, string message)
    {
        RunOnUi(() =>
        {
            DeploymentLogs.Add(message);
            const int maxLines = 400;
            while (DeploymentLogs.Count > maxLines)
            {
                DeploymentLogs.RemoveAt(0);
            }
        });
    }

    private void InitializeProgressCollections()
    {
        DeploymentSteps.Clear();
        DeploymentLogs.Clear();
        _stepIndex.Clear();

        foreach (string step in _deploymentOrchestrator.PlannedSteps)
        {
            DeploymentStepItemViewModel vm = new(step);
            DeploymentSteps.Add(vm);
            _stepIndex[step] = vm;
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
        }
        catch (Exception ex)
        {
            RunOnUi(() => DetectedHardwareSummary = $"Hardware detection failed: {ex.Message}");
        }
    }

    private static bool IsArchitectureMatch(string osArchitecture, string driverArchitecture)
    {
        string os = NormalizeArchitecture(osArchitecture);
        string driver = NormalizeArchitecture(driverArchitecture);
        return os.Equals(driver, StringComparison.OrdinalIgnoreCase);
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

}
