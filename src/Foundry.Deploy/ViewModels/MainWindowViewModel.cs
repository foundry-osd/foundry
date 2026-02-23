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
    private const string AnyFilterOption = "Any";
    private const string DefaultWindowsRelease = "11";
    private const string DefaultReleaseId = "25H2";
    private const string DefaultLicenseChannel = "RET";
    private const string DefaultEdition = "Pro";
    private const string FallbackLanguageCode = "en-us";
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
    private readonly Dictionary<string, DeploymentStepItemViewModel> _stepIndex = new(StringComparer.Ordinal);
    private HardwareProfile? _detectedHardware;
    private bool _isUpdatingOsFilters;
    private bool _isUpdatingDriverFilters;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PreviousWizardStepCommand))]
    [NotifyCanExecuteChangedFor(nameof(NextWizardStepCommand))]
    [NotifyCanExecuteChangedFor(nameof(StartDeploymentCommand))]
    private int wizardStepIndex;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RefreshCatalogsCommand))]
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
    [NotifyCanExecuteChangedFor(nameof(StartDeploymentCommand))]
    private OperatingSystemCatalogItem? selectedOperatingSystem;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartDeploymentCommand))]
    private TargetDiskInfo? selectedTargetDisk;

    [ObservableProperty]
    private DriverPackCatalogItem? selectedDriverPack;

    [ObservableProperty]
    private DeploymentMode selectedDeploymentMode = DeploymentMode.Usb;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartDeploymentCommand))]
    private string cacheRootPath = @"X:\Windows\Temp\Foundry\Deploy";

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
    private string selectedDriverManufacturer = AnyFilterOption;

    [ObservableProperty]
    private string selectedDriverOsName = AnyFilterOption;

    [ObservableProperty]
    private string selectedDriverReleaseYear = AnyFilterOption;

    [ObservableProperty]
    private string selectedDriverVersion = AnyFilterOption;

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
    public ObservableCollection<string> DriverManufacturerFilters { get; } = [];
    public ObservableCollection<string> DriverOsNameFilters { get; } = [];
    public ObservableCollection<string> DriverReleaseYearFilters { get; } = [];
    public ObservableCollection<string> DriverVersionFilters { get; } = [];
    public ObservableCollection<TargetDiskInfo> TargetDisks { get; } = [];
    public ObservableCollection<DeploymentStepItemViewModel> DeploymentSteps { get; } = [];
    public ObservableCollection<string> DeploymentLogs { get; } = [];

    public IReadOnlyList<DeploymentMode> DeploymentModes { get; } = Enum.GetValues<DeploymentMode>();

    public DeployThemeMode CurrentTheme => _themeService.CurrentTheme;
    public bool IsDebugSafeMode => DebugSafetyMode.IsEnabled;

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

        _operationProgressService.ProgressChanged += OnOperationProgressChanged;
        _deploymentOrchestrator.StepProgressChanged += OnStepProgressChanged;
        _deploymentOrchestrator.LogEmitted += OnLogEmitted;

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
                RefreshDriverFilterOptions();
                ApplyDriverFilter();

                AutoSelectDriverPackFromHardware();

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
        IsAutopilotDeferred = false;
        AutopilotDeferredMessage = string.Empty;
        DeploymentStatus = "Deployment started.";

        DeploymentContext context = new()
        {
            Mode = SelectedDeploymentMode,
            CacheRootPath = CacheRootPath,
            TargetDiskNumber = effectiveTargetDisk.DiskNumber,
            OperatingSystem = SelectedOperatingSystem,
            DriverPack = SelectedDriverPack,
            AutoSelectDriverPackWhenEmpty = AutoSelectDriverPackWhenEmpty,
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
            string logsPath = Path.Combine(CacheRootPath, "Logs");
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

    partial void OnSelectedOperatingSystemChanged(OperatingSystemCatalogItem? value)
    {
        RefreshDriverFilterOptions();
        ApplyDriverFilter();
        AutoSelectDriverPackFromHardware();
    }

    partial void OnSelectedDeploymentModeChanged(DeploymentMode value)
    {
        EnsureCachePathForMode();
    }

    partial void OnEffectiveOsArchitectureChanged(string value)
    {
        if (_isUpdatingOsFilters)
        {
            return;
        }

        RefreshOsFilterOptions();
        ApplyOsFilter();
        RefreshDriverFilterOptions();
        ApplyDriverFilter();
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

    partial void OnSelectedDriverManufacturerChanged(string value)
    {
        HandleDriverFilterSelectionChanged();
    }

    partial void OnSelectedDriverOsNameChanged(string value)
    {
        HandleDriverFilterSelectionChanged();
    }

    partial void OnSelectedDriverReleaseYearChanged(string value)
    {
        HandleDriverFilterSelectionChanged();
    }

    partial void OnSelectedDriverVersionChanged(string value)
    {
        HandleDriverFilterSelectionChanged();
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

    private void HandleDriverFilterSelectionChanged()
    {
        if (_isUpdatingDriverFilters)
        {
            return;
        }

        RefreshDriverFilterOptions();
        ApplyDriverFilter();
    }

    partial void OnAutoSelectDriverPackWhenEmptyChanged(bool value)
    {
        AutoSelectDriverPackFromHardware();
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
            CacheRootPath = Path.Combine(Path.GetTempPath(), "Foundry", "Deploy", "Debug");
            return;
        }

        CacheRootPath = SelectedDeploymentMode switch
        {
            DeploymentMode.Iso => @"C:\Foundry\Deploy",
            _ => @"X:\Windows\Temp\Foundry\Deploy"
        };
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
        return !IsDeploymentRunning && WizardStepIndex < 3;
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
               hasTargetDisk;
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
                previousWindowsRelease);

            IEnumerable<OperatingSystemCatalogItem> releaseScope = ApplyWindowsReleaseFilter(baseQuery);
            SelectedReleaseId = UpdateFilterSelection(
                ReleaseIdFilters,
                releaseScope.Select(item => item.ReleaseId),
                previousReleaseId);

            IEnumerable<OperatingSystemCatalogItem> languageScope = ApplyReleaseIdFilter(releaseScope);
            SelectedLanguageCode = UpdateLanguageFilterSelection(
                LanguageFilters,
                languageScope.Select(GetLanguageFilterValue),
                previousLanguageCode);

            IEnumerable<OperatingSystemCatalogItem> licenseScope = ApplyLanguageFilter(languageScope);
            SelectedLicenseChannel = UpdateFilterSelection(
                LicenseChannelFilters,
                licenseScope.Select(item => item.LicenseChannel),
                previousLicenseChannel);

            IEnumerable<OperatingSystemCatalogItem> editionScope = ApplyLicenseChannelFilter(licenseScope);
            IEnumerable<string> recommendedEditions = BuildRecommendedEditionOptions(editionScope);
            SelectedEdition = UpdateFilterSelection(
                EditionFilters,
                recommendedEditions,
                previousEdition);
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
        return IsAnyFilter(SelectedWindowsRelease)
            ? source
            : source.Where(item => item.WindowsRelease.Equals(SelectedWindowsRelease, StringComparison.OrdinalIgnoreCase));
    }

    private IEnumerable<OperatingSystemCatalogItem> ApplyReleaseIdFilter(IEnumerable<OperatingSystemCatalogItem> source)
    {
        return IsAnyFilter(SelectedReleaseId)
            ? source
            : source.Where(item => item.ReleaseId.Equals(SelectedReleaseId, StringComparison.OrdinalIgnoreCase));
    }

    private IEnumerable<OperatingSystemCatalogItem> ApplyLanguageFilter(IEnumerable<OperatingSystemCatalogItem> source)
    {
        return IsAnyFilter(SelectedLanguageCode)
            ? source
            : source.Where(item => GetLanguageFilterValue(item).Equals(SelectedLanguageCode, StringComparison.OrdinalIgnoreCase));
    }

    private IEnumerable<OperatingSystemCatalogItem> ApplyLicenseChannelFilter(IEnumerable<OperatingSystemCatalogItem> source)
    {
        return IsAnyFilter(SelectedLicenseChannel)
            ? source
            : source.Where(item => item.LicenseChannel.Equals(SelectedLicenseChannel, StringComparison.OrdinalIgnoreCase));
    }

    private IEnumerable<OperatingSystemCatalogItem> ApplyEditionFilter(IEnumerable<OperatingSystemCatalogItem> source)
    {
        return IsAnyFilter(SelectedEdition)
            ? source
            : source.Where(item => item.Edition.Equals(SelectedEdition, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsAnyFilter(string value)
    {
        return string.IsNullOrWhiteSpace(value) || value.Equals(AnyFilterOption, StringComparison.OrdinalIgnoreCase);
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

        if (IsAnyFilter(SelectedLicenseChannel))
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

    private static string EnsureFilterSelection(string selectedValue, ObservableCollection<string> options)
    {
        if (options.Count == 0)
        {
            return string.Empty;
        }

        if (string.IsNullOrWhiteSpace(selectedValue) || selectedValue.Equals(AnyFilterOption, StringComparison.OrdinalIgnoreCase))
        {
            return string.Empty;
        }

        string? matchingOption = options.FirstOrDefault(option =>
            option.Equals(selectedValue, StringComparison.OrdinalIgnoreCase));

        return matchingOption ?? string.Empty;
    }

    private static string UpdateFilterSelection(
        ObservableCollection<string> target,
        IEnumerable<string> values,
        string previousSelection)
    {
        UpdateFilterCollection(target, values);
        return EnsureFilterSelection(previousSelection, target);
    }

    private static string UpdateLanguageFilterSelection(
        ObservableCollection<string> target,
        IEnumerable<string> values,
        string previousSelection)
    {
        UpdateFilterCollection(target, values);

        string selected = EnsureLanguageSelection(previousSelection, target);
        if (!IsAnyFilter(selected))
        {
            return selected;
        }

        selected = EnsureLanguageSelection(DefaultLanguageCode, target);
        if (!IsAnyFilter(selected))
        {
            return selected;
        }

        selected = EnsureLanguageSelection(FallbackLanguageCode, target);
        return selected;
    }

    private static string EnsureLanguageSelection(string languageCode, ObservableCollection<string> options)
    {
        if (options.Count == 0)
        {
            return string.Empty;
        }

        if (string.IsNullOrWhiteSpace(languageCode) || IsAnyFilter(languageCode))
        {
            return string.Empty;
        }

        string normalized = NormalizeLanguageCode(languageCode);

        string? exact = options.FirstOrDefault(option =>
            !IsAnyFilter(option) &&
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
                if (IsAnyFilter(option))
                {
                    return false;
                }

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

    private void ApplyDriverFilter()
    {
        DriverPackCatalogItem[] filtered = BuildFilteredDriverPacks();

        if (SelectedDriverPack is null || !filtered.Contains(SelectedDriverPack))
        {
            SelectedDriverPack = filtered.FirstOrDefault();
        }
    }

    private void RefreshDriverFilterOptions()
    {
        _isUpdatingDriverFilters = true;

        try
        {
            string previousDriverManufacturer = SelectedDriverManufacturer;
            string previousDriverOsName = SelectedDriverOsName;
            string previousDriverReleaseYear = SelectedDriverReleaseYear;
            string previousDriverVersion = SelectedDriverVersion;

            IEnumerable<DriverPackCatalogItem> baseQuery = BuildDriverQueryWithArchitecture(DriverPacks);

            SelectedDriverManufacturer = UpdateFilterSelection(
                DriverManufacturerFilters,
                baseQuery.Select(item => item.Manufacturer),
                previousDriverManufacturer);

            IEnumerable<DriverPackCatalogItem> manufacturerScope = ApplyDriverManufacturerFilter(baseQuery);
            SelectedDriverOsName = UpdateFilterSelection(
                DriverOsNameFilters,
                manufacturerScope.Select(item => item.OsName),
                previousDriverOsName);

            IEnumerable<DriverPackCatalogItem> osScope = ApplyDriverOsNameFilter(manufacturerScope);
            SelectedDriverReleaseYear = UpdateFilterSelection(
                DriverReleaseYearFilters,
                osScope.Select(GetDriverReleaseYear),
                previousDriverReleaseYear);

            IEnumerable<DriverPackCatalogItem> yearScope = ApplyDriverReleaseYearFilter(osScope);
            SelectedDriverVersion = UpdateFilterSelection(
                DriverVersionFilters,
                yearScope.Select(item => item.Version),
                previousDriverVersion);
        }
        finally
        {
            _isUpdatingDriverFilters = false;
        }
    }

    private IEnumerable<DriverPackCatalogItem> BuildDriverQueryWithArchitecture(IEnumerable<DriverPackCatalogItem> source)
    {
        string architecture = NormalizeArchitecture(SelectedOperatingSystem?.Architecture ?? EffectiveOsArchitecture);
        if (!string.IsNullOrWhiteSpace(architecture))
        {
            source = source.Where(item => IsArchitectureMatch(architecture, item.OsArchitecture));
        }

        string selectedOsRelease = SelectedOperatingSystem?.WindowsRelease?.Trim() ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(selectedOsRelease))
        {
            source = source.Where(item =>
                item.OsName.Contains(selectedOsRelease, StringComparison.OrdinalIgnoreCase));
        }

        return source;
    }

    private IEnumerable<DriverPackCatalogItem> ApplyDriverManufacturerFilter(IEnumerable<DriverPackCatalogItem> source)
    {
        return IsAnyFilter(SelectedDriverManufacturer)
            ? source
            : source.Where(item => item.Manufacturer.Equals(SelectedDriverManufacturer, StringComparison.OrdinalIgnoreCase));
    }

    private IEnumerable<DriverPackCatalogItem> ApplyDriverOsNameFilter(IEnumerable<DriverPackCatalogItem> source)
    {
        return IsAnyFilter(SelectedDriverOsName)
            ? source
            : source.Where(item => item.OsName.Equals(SelectedDriverOsName, StringComparison.OrdinalIgnoreCase));
    }

    private IEnumerable<DriverPackCatalogItem> ApplyDriverReleaseYearFilter(IEnumerable<DriverPackCatalogItem> source)
    {
        return IsAnyFilter(SelectedDriverReleaseYear)
            ? source
            : source.Where(item => GetDriverReleaseYear(item).Equals(SelectedDriverReleaseYear, StringComparison.OrdinalIgnoreCase));
    }

    private IEnumerable<DriverPackCatalogItem> ApplyDriverVersionFilter(IEnumerable<DriverPackCatalogItem> source)
    {
        return IsAnyFilter(SelectedDriverVersion)
            ? source
            : source.Where(item => item.Version.Equals(SelectedDriverVersion, StringComparison.OrdinalIgnoreCase));
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

    private DriverPackCatalogItem[] BuildFilteredDriverPacks()
    {
        IEnumerable<DriverPackCatalogItem> query = BuildDriverQueryWithArchitecture(DriverPacks);
        query = ApplyDriverManufacturerFilter(query);
        query = ApplyDriverOsNameFilter(query);
        query = ApplyDriverReleaseYearFilter(query);
        query = ApplyDriverVersionFilter(query);
        return query.Take(1000).ToArray();
    }

    private static string GetDriverReleaseYear(DriverPackCatalogItem item)
    {
        return item.ReleaseDate?.Year.ToString() ?? "Unknown";
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
                RefreshDriverFilterOptions();
                ApplyDriverFilter();
                AutoSelectDriverPackFromHardware();
            });
        }
        catch (Exception ex)
        {
            RunOnUi(() => DetectedHardwareSummary = $"Hardware detection failed: {ex.Message}");
        }
    }

    private void AutoSelectDriverPackFromHardware()
    {
        if (!AutoSelectDriverPackWhenEmpty)
        {
            return;
        }

        if (_detectedHardware is null || SelectedOperatingSystem is null || DriverPacks.Count == 0)
        {
            return;
        }

        DriverPackSelectionResult selection = _driverPackSelectionService.SelectBest(
            DriverPacks.ToArray(),
            _detectedHardware,
            SelectedOperatingSystem);

        if (selection.DriverPack is null)
        {
            return;
        }

        if (BuildFilteredDriverPacks().Contains(selection.DriverPack))
        {
            SelectedDriverPack = selection.DriverPack;
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
        if (IsAnyFilter(SelectedEdition))
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
            $"Mode: {SelectedDeploymentMode}" + Environment.NewLine +
            Environment.NewLine +
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
