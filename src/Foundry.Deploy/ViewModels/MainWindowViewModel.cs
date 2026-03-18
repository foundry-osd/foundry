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
    private readonly ILogger<MainWindowViewModel> _logger;
    private readonly Dispatcher _dispatcher;
    private readonly DeploymentMode _resolvedDeploymentMode;
    private readonly string? _resolvedUsbCacheRuntimeRoot;
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
        _logger = logger;
        _dispatcher = Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;
        (DeploymentMode resolvedMode, string? resolvedUsbCacheRuntimeRoot) = ResolveDeploymentRuntimeContext();
        _resolvedDeploymentMode = resolvedMode;
        _resolvedUsbCacheRuntimeRoot = resolvedUsbCacheRuntimeRoot;
        Preparation = new DeploymentPreparationViewModel();
        Preparation.StateChanged += OnPreparationStateChanged;
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
                Preparation.TargetDisks.Clear();
                foreach (TargetDiskInfo disk in disks)
                {
                    Preparation.TargetDisks.Add(disk);
                }

                if (IsDebugSafeMode && !Preparation.TargetDisks.Any(item => item.IsSelectable))
                {
                    Preparation.TargetDisks.Insert(0, BuildDebugVirtualDisk());
                }

                if (Preparation.TargetDisks.Count == 0)
                {
                    Preparation.SelectedTargetDisk = null;
                    Session.SetStatus("No disks detected.");
                    return;
                }

                TargetDiskInfo? currentSelection = Preparation.SelectedTargetDisk is null
                    ? null
                    : Preparation.TargetDisks.FirstOrDefault(item => item.DiskNumber == Preparation.SelectedTargetDisk.DiskNumber);

                Preparation.SelectedTargetDisk = currentSelection
                    ?? Preparation.TargetDisks.FirstOrDefault(item => item.IsSelectable)
                    ?? (IsDebugSafeMode ? Preparation.TargetDisks.FirstOrDefault(item => item.DiskNumber == BuildDebugVirtualDisk().DiskNumber) : null)
                    ?? Preparation.TargetDisks.FirstOrDefault();

                Session.SetStatus($"Target disks loaded: {Preparation.TargetDisks.Count} detected.");
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

        string normalizedComputerName = ComputerNameRules.Normalize(Preparation.TargetComputerName);
        if (!ComputerNameRules.IsValid(normalizedComputerName))
        {
            Session.SetStatus("Enter a valid computer name.");
            return;
        }

        if (!normalizedComputerName.Equals(Preparation.TargetComputerName, StringComparison.Ordinal))
        {
            Preparation.TargetComputerName = normalizedComputerName;
        }

        TargetDiskInfo? effectiveTargetDisk = Preparation.SelectedTargetDisk;
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

        DriverPackSelectionKind effectiveDriverPackKind = DriverPackSelection.EffectiveSelectionKind;
        DriverPackCatalogItem? effectiveDriverPack = DriverPackSelection.ResolveEffectiveSelection();

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
            CacheRootPath = Preparation.CacheRootPath,
            TargetDiskNumber = effectiveTargetDisk.DiskNumber,
            TargetComputerName = normalizedComputerName,
            OperatingSystem = OperatingSystemCatalog.SelectedOperatingSystem,
            DriverPackSelectionKind = effectiveDriverPackKind,
            DriverPack = effectiveDriverPack,
            ApplyFirmwareUpdates = Preparation.ApplyFirmwareUpdates,
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
        RefreshTargetDisksCommand.NotifyCanExecuteChanged();
        NextWizardStepCommand.NotifyCanExecuteChanged();
        StartDeploymentCommand.NotifyCanExecuteChanged();

        if (Preparation.SelectedTargetDisk is not null && !IsDebugSafeMode && !Preparation.SelectedTargetDisk.IsSelectable)
        {
            Session.SetStatus($"Selected disk blocked: {Preparation.SelectedTargetDisk.SelectionWarning}");
        }
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

        if (_resolvedDeploymentMode == DeploymentMode.Usb)
        {
            Preparation.CacheRootPath = _resolvedUsbCacheRuntimeRoot ?? WinPeTransientRuntimeRoot;
            return;
        }

        Preparation.CacheRootPath = WinPeTransientRuntimeRoot;
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
        bool hasTargetDisk = Preparation.SelectedTargetDisk is not null && (IsDebugSafeMode || Preparation.SelectedTargetDisk.IsSelectable);

        if (IsDebugSafeMode && Preparation.SelectedTargetDisk is null)
        {
            hasTargetDisk = true;
        }

        return !IsDeploymentRunning &&
               !IsCatalogLoading &&
               !IsTargetDiskLoading &&
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
        try
        {
            HardwareProfile profile = await _hardwareProfileService.GetCurrentAsync().ConfigureAwait(false);
            RunOnUi(() =>
            {
                _detectedHardware = profile;
                OperatingSystemCatalog.SetEffectiveArchitecture(profile.Architecture);
                RefreshDriverPackSelectionContext();
                Preparation.SetDetectedHardware(profile);
            });
            _logger.LogInformation("Hardware profile loaded in view model. DisplayLabel={DisplayLabel}", profile.DisplayLabel);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Hardware profile loading failed in view model.");
            RunOnUi(() => Preparation.SetHardwareDetectionFailure($"Hardware detection failed: {ex.Message}"));
        }
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
            Preparation.ApplyOfflineComputerName(effectiveName);
            Session.SetComputerName(Preparation.TargetComputerName);
        });
    }

    private static string ResolveInitialComputerName()
    {
        string normalized = ComputerNameRules.Normalize(Environment.MachineName);
        return normalized.Length > 0
            ? normalized
            : ComputerNameRules.FallbackName;
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

        Preparation.StateChanged -= OnPreparationStateChanged;
        OperatingSystemCatalog.StateChanged -= OnOperatingSystemCatalogStateChanged;
        DriverPackSelection.StateChanged -= OnDriverPackSelectionStateChanged;
        Session.Dispose();
        _isDisposed = true;
    }

}
