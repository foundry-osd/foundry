using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Foundry.Deploy;
using Foundry.Deploy.Models;
using Foundry.Deploy.Services.Catalog;
using Foundry.Deploy.Services.Deployment;
using Foundry.Deploy.Services.Operations;
using Foundry.Deploy.Services.Runtime;
using Foundry.Deploy.Services.Startup;
using Foundry.Deploy.Services.ApplicationShell;
using Foundry.Deploy.Services.Localization;
using Foundry.Deploy.Services.System;
using Foundry.Deploy.Services.Theme;
using Foundry.Deploy.Services.Wizard;
using Foundry.Deploy.Validation;
using Microsoft.Extensions.Logging;
using DeployThemeMode = Foundry.Deploy.Services.Theme.ThemeMode;

namespace Foundry.Deploy.ViewModels;

public partial class MainWindowViewModel : LocalizedViewModelBase
{
    private readonly IThemeService _themeService;
    private readonly IDeploymentStartupCoordinator _deploymentStartupCoordinator;
    private readonly IDeploymentCatalogLoadService _deploymentCatalogLoadService;
    private readonly IDeploymentLaunchPreparationService _deploymentLaunchPreparationService;
    private readonly IDeploymentExecutionService _deploymentExecutionService;
    private readonly IDeploymentWizardStateService _deploymentWizardStateService;
    private readonly IDeploymentOrchestrator _deploymentOrchestrator;
    private readonly IApplicationShellService _applicationShellService;
    private readonly ILogger<MainWindowViewModel> _logger;
    private readonly Dispatcher _dispatcher;
    private readonly DeploymentRuntimeContext _deploymentRuntimeContext;
    private readonly DeploymentWizardContext _wizardContext;
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

    public DeploymentPreparationViewModel Preparation { get; }
    public DeploymentSessionViewModel Session { get; }
    public OperatingSystemCatalogViewModel OperatingSystemCatalog { get; }
    public DriverPackSelectionViewModel DriverPackSelection { get; }
    public ObservableCollection<SupportedCultureOption> SupportedCultures { get; } = [];

    public CultureInfo CurrentCulture => LocalizationService.CurrentCulture;
    public DeployThemeMode CurrentTheme => _themeService.CurrentTheme;
    public bool IsDebugSafeMode => DebugSafetyMode.IsEnabled;
    public string EffectiveOsArchitecture => OperatingSystemCatalog.EffectiveOsArchitecture;
    public OperatingSystemCatalogItem? SelectedOperatingSystem => OperatingSystemCatalog.SelectedOperatingSystem;
    public string WindowTitle => GetString("App.WindowTitle");
    public string VersionDisplay => Format("Common.VersionFormat", FoundryDeployApplicationInfo.Version);
    public string OperatingSystemArchitectureDisplay => Format("Catalog.ArchitectureFormat", OperatingSystemCatalog.EffectiveOsArchitecture);
    public string SummaryTargetDiskText => Preparation.SelectedTargetDisk?.DisplayLabel ?? GetString("Summary.NoDiskSelected");
    public string SummaryOperatingSystemText => SelectedOperatingSystem is null
        ? GetString("Summary.NoSelection")
        : new Converters.OperatingSystemSummaryConverter().Convert(SelectedOperatingSystem, typeof(string), string.Empty, LocalizationService.CurrentCulture)?.ToString() ?? GetString("Summary.NoSelection");
    public string SummaryFirmwareText => Format(
        "Summary.FirmwareEnabledFormat",
        Preparation.ApplyFirmwareUpdates ? GetString("Common.Enabled") : GetString("Common.Disabled"));
    public string SummaryAutopilotEnabledText => Format(
        "Summary.EnabledFormat",
        Preparation.IsAutopilotEnabled ? GetString("Common.Yes") : GetString("Common.No"));
    public string SummaryAutopilotProfileText => Format(
        "Summary.SelectedProfileFormat",
        Preparation.SelectedAutopilotProfile?.DisplayName ?? GetString("Common.None"));

    public MainWindowViewModel(
        ILocalizationService localizationService,
        IThemeService themeService,
        IOperationProgressService operationProgressService,
        IDeploymentStartupCoordinator deploymentStartupCoordinator,
        IDeploymentRuntimeContextService deploymentRuntimeContextService,
        IDeploymentCatalogLoadService deploymentCatalogLoadService,
        IDeploymentLaunchPreparationService deploymentLaunchPreparationService,
        IDeploymentExecutionService deploymentExecutionService,
        IDeploymentWizardStateService deploymentWizardStateService,
        IDeploymentOrchestrator deploymentOrchestrator,
        IApplicationShellService applicationShellService,
        IDeploymentWizardContextFactory deploymentWizardContextFactory,
        IProcessRunner processRunner,
        ILogger<MainWindowViewModel> logger)
        : base(localizationService)
    {
        _themeService = themeService;
        _deploymentStartupCoordinator = deploymentStartupCoordinator;
        _deploymentCatalogLoadService = deploymentCatalogLoadService;
        _deploymentLaunchPreparationService = deploymentLaunchPreparationService;
        _deploymentExecutionService = deploymentExecutionService;
        _deploymentWizardStateService = deploymentWizardStateService;
        _deploymentOrchestrator = deploymentOrchestrator;
        _applicationShellService = applicationShellService;
        _logger = logger;
        _dispatcher = Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;
        _deploymentRuntimeContext = deploymentRuntimeContextService.Resolve();
        _wizardContext = deploymentWizardContextFactory.Create(IsDebugSafeMode);
        _wizardContext.StateChanged += OnWizardContextStateChanged;
        _wizardContext.StatusMessageGenerated += OnWizardContextStatusMessageGenerated;
        Preparation = _wizardContext.Preparation;
        OperatingSystemCatalog = _wizardContext.OperatingSystemCatalog;
        DriverPackSelection = _wizardContext.DriverPackSelection;
        Session = new DeploymentSessionViewModel(
            _dispatcher,
            _logger,
            operationProgressService,
            _deploymentOrchestrator,
            processRunner,
            localizationService,
            IsDebugSafeMode);
        Session.PropertyChanged += OnSessionPropertyChanged;
        LocalizationService.LanguageChanged += OnLocalizationLanguageChanged;
        RefreshSupportedCultures();
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

        DeploymentStartupSnapshot startupSnapshot = await _deploymentStartupCoordinator.InitializeAsync(
                new DeploymentStartupRequest
                {
                    RuntimeContext = _deploymentRuntimeContext,
                    IsDebugSafeMode = IsDebugSafeMode,
                    FallbackComputerName = ResolveInitialComputerName()
                })
            .ConfigureAwait(false);

        RunOnUi(() => ApplyStartupSnapshot(startupSnapshot));

        _isInitialized = true;
    }

    [RelayCommand]
    private void SetCulture(string cultureName)
    {
        LocalizationService.SetCulture(new CultureInfo(cultureName));
    }

    private void RefreshSupportedCultures()
    {
        SupportedCultures.Clear();
        foreach (SupportedCultureOption option in SupportedCultureCatalog.CreateOptions(CurrentCulture, key => Strings[key]))
        {
            SupportedCultures.Add(option);
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

    [RelayCommand]
    private void ShowAbout()
    {
        _applicationShellService.ShowAbout();
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
            DeploymentCatalogSnapshot snapshot = await _deploymentCatalogLoadService.LoadAsync().ConfigureAwait(false);
            _logger.LogInformation(
                "Deployment catalogs refreshed successfully. OperatingSystemCount={OperatingSystemCount}, DriverPackCount={DriverPackCount}",
                snapshot.OperatingSystems.Count,
                snapshot.DriverPacks.Count);

            RunOnUi(() =>
            {
                _wizardContext.ApplyCatalogSnapshot(snapshot);
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

    [RelayCommand(CanExecute = nameof(CanBeginWizard))]
    private void BeginWizard()
    {
        Session.ShowWizard();
    }

    [RelayCommand(CanExecute = nameof(CanStartDeployment))]
    private async Task StartDeploymentAsync()
    {
        _logger.LogInformation("Start deployment requested.");
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
                IsAutopilotEnabled = Preparation.IsAutopilotEnabled,
                SelectedAutopilotProfile = Preparation.SelectedAutopilotProfile,
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
            DeploymentExecutionRunResult executionRunResult = await _deploymentExecutionService
                .ExecuteAsync(launchPreparation.Context)
                .ConfigureAwait(false);

            RunOnUi(() => Session.ApplyExecutionRunResult(executionRunResult));
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

    private void OnWizardContextStateChanged(object? sender, EventArgs e)
    {
        OnPropertyChanged(nameof(EffectiveOsArchitecture));
        OnPropertyChanged(nameof(SelectedOperatingSystem));
        OnPropertyChanged(nameof(OperatingSystemArchitectureDisplay));
        OnPropertyChanged(nameof(SummaryTargetDiskText));
        OnPropertyChanged(nameof(SummaryOperatingSystemText));
        OnPropertyChanged(nameof(SummaryFirmwareText));
        OnPropertyChanged(nameof(SummaryAutopilotEnabledText));
        OnPropertyChanged(nameof(SummaryAutopilotProfileText));
        NextWizardStepCommand.NotifyCanExecuteChanged();
        StartDeploymentCommand.NotifyCanExecuteChanged();
    }

    private void OnWizardContextStatusMessageGenerated(string message)
    {
        Session.SetStatus(message);
    }

    private void OnSessionPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(e.PropertyName) &&
            e.PropertyName is not nameof(DeploymentSessionViewModel.IsStartupInitializing) &&
            e.PropertyName is not nameof(DeploymentSessionViewModel.CurrentPage))
        {
            return;
        }

        RefreshCatalogsCommand.NotifyCanExecuteChanged();
        BeginWizardCommand.NotifyCanExecuteChanged();
    }

    private bool CanShowDebugPages()
    {
        return IsDebugSafeMode && !IsDeploymentRunning;
    }

    private bool CanRefreshCatalogs()
    {
        return Session.IsStartupReady && !IsCatalogLoading && !IsDeploymentRunning;
    }

    private bool CanBeginWizard()
    {
        return Session.IsSplashPage && Session.IsStartupReady;
    }

    private bool CanGoPrevious()
    {
        return _deploymentWizardStateService.CanGoPrevious(BuildWizardStateSnapshot());
    }

    private bool CanGoNext()
    {
        return _deploymentWizardStateService.CanGoNext(BuildWizardStateSnapshot());
    }

    private bool CanStartDeployment()
    {
        return _deploymentWizardStateService.CanStartDeployment(BuildWizardStateSnapshot());
    }

    private bool HasValidDriverPackSelection()
    {
        return DriverPackSelection.HasValidSelection();
    }

    private DeploymentWizardStateSnapshot BuildWizardStateSnapshot()
    {
        return new DeploymentWizardStateSnapshot
        {
            WizardStepIndex = WizardStepIndex,
            IsDeploymentRunning = IsDeploymentRunning,
            IsCatalogLoading = IsCatalogLoading,
            IsTargetDiskLoading = Preparation.IsTargetDiskLoading,
            IsDebugSafeMode = IsDebugSafeMode,
            IsTargetComputerNameValid = ComputerNameRules.IsValid(Preparation.TargetComputerName),
            HasSelectedOperatingSystem = OperatingSystemCatalog.SelectedOperatingSystem is not null,
            HasTargetDiskSelection = Preparation.SelectedTargetDisk is not null,
            IsSelectedTargetDiskSelectable = Preparation.SelectedTargetDisk?.IsSelectable ?? false,
            HasValidDriverPackSelection = HasValidDriverPackSelection(),
            HasValidAutopilotSelection = !Preparation.IsAutopilotEnabled || Preparation.SelectedAutopilotProfile is not null,
            IsOperatingSystemCatalogReadyForNavigation = !IsCatalogLoading && OperatingSystemCatalog.IsReadyForNavigation()
        };
    }

    private static string ResolveInitialComputerName()
    {
        string normalized = ComputerNameRules.Normalize(Environment.MachineName);
        return normalized.Length > 0
            ? normalized
            : ComputerNameRules.FallbackName;
    }

    private void ApplyStartupSnapshot(DeploymentStartupSnapshot startupSnapshot)
    {
        ArgumentNullException.ThrowIfNull(startupSnapshot);

        string startupStatusMessage = _wizardContext.ApplyStartupSnapshot(startupSnapshot);
        Session.SetComputerName(Preparation.TargetComputerName);

        Session.SetStatus(
            !string.IsNullOrWhiteSpace(startupSnapshot.TargetDiskStatusMessage)
                ? startupSnapshot.TargetDiskStatusMessage
                : startupStatusMessage);
        Session.CompleteStartupInitialization();
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

    public override void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _wizardContext.StatusMessageGenerated -= OnWizardContextStatusMessageGenerated;
        _wizardContext.StateChanged -= OnWizardContextStateChanged;
        Session.PropertyChanged -= OnSessionPropertyChanged;
        LocalizationService.LanguageChanged -= OnLocalizationLanguageChanged;
        _wizardContext.Dispose();
        Session.Dispose();
        _isDisposed = true;
        base.Dispose();
    }

    private void OnLocalizationLanguageChanged(object? sender, EventArgs e)
    {
        RunOnUiThread(() =>
        {
            RefreshSupportedCultures();
            OnPropertyChanged(nameof(CurrentCulture));
            OnPropertyChanged(nameof(WindowTitle));
            OnPropertyChanged(nameof(VersionDisplay));
            OnPropertyChanged(nameof(OperatingSystemArchitectureDisplay));
            OnPropertyChanged(nameof(SummaryTargetDiskText));
            OnPropertyChanged(nameof(SummaryOperatingSystemText));
            OnPropertyChanged(nameof(SummaryFirmwareText));
            OnPropertyChanged(nameof(SummaryAutopilotEnabledText));
            OnPropertyChanged(nameof(SummaryAutopilotProfileText));
        });
    }

    private string GetString(string key)
    {
        return Strings[key];
    }

    private string Format(string key, params object[] args)
    {
        return string.Format(LocalizationService.CurrentCulture, GetString(key), args);
    }
}
