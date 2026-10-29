// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Collections.ObjectModel;
using BootMediaUpdateReason = Foundry.Core.Models.Configuration.BootMediaUpdateReason;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Foundry.Core.Services.DomainJoin;
using Foundry.Deploy;
using Foundry.Deploy.Models;
using Foundry.Deploy.Models.Configuration;
using Foundry.Deploy.Services.Cache;
using Foundry.Deploy.Services.Deployment;
using Foundry.Deploy.Services.DomainJoin;
using Foundry.Deploy.Services.Operations;
using Foundry.Deploy.Services.Runtime;
using Foundry.Deploy.Services.Security;
using Foundry.Deploy.Services.Startup;
using Foundry.Deploy.Services.ApplicationShell;
using Foundry.Deploy.Services.Localization;
using Foundry.Deploy.Services.System;
using Foundry.Utilities.Globalization;
using Foundry.Utilities.Networking;
using Foundry.Deploy.Services.Theme;
using Foundry.Deploy.Services.Wizard;
using Foundry.Localization;
using Foundry.Telemetry;
using ComputerNameRules = Foundry.Core.Services.Configuration.ComputerNameRules;
using Microsoft.Extensions.Logging;
using DeployThemeMode = Foundry.Deploy.Services.Theme.ThemeMode;

namespace Foundry.Deploy.ViewModels;

public partial class MainWindowViewModel : LocalizedViewModelBase
{
    private static readonly TimeSpan BlockedStartTelemetryTimeout = TimeSpan.FromSeconds(5);

    private readonly IThemeService _themeService;
    private readonly IDeploymentStartupCoordinator _deploymentStartupCoordinator;
    private readonly IDeploymentAccessGate _deploymentAccessGate;
    private readonly IDeploymentLaunchPreparationService _deploymentLaunchPreparationService;
    private readonly IDeploymentExecutionService _deploymentExecutionService;
    private readonly IDeploymentWizardStateService _deploymentWizardStateService;
    private readonly IDeploymentOrchestrator _deploymentOrchestrator;
    private readonly IApplicationShellService _applicationShellService;
    private readonly IDomainCredentialVerifier _domainCredentialVerifier;
    private readonly IZeroTouchMediaCredentialCheck _zeroTouchMediaCredentialCheck;
    private readonly IZeroTouchDomainCredentialCheck _zeroTouchDomainCredentialCheck;
    private readonly INetworkCacheStartupDialogService _networkCacheStartupDialog;
    private readonly ITelemetryService _telemetryService;
    private readonly ILogger<MainWindowViewModel> _logger;
    private readonly IKeyboardLayoutSwitcher _keyboardLayoutSwitcher;
    private readonly Dispatcher _dispatcher;
    private readonly DeploymentRuntimeContext _deploymentRuntimeContext;
    private readonly DeploymentWizardContext _wizardContext;
    private readonly DeploymentWizardNavigationState _wizardNavigationState;
    private DebugAutopilotMode _debugAutopilotMode = DebugAutopilotMode.None;
    private DebugDomainJoinMode _debugDomainJoinMode = DebugDomainJoinMode.None;
    private DebugDomainCredentialCheckScenario _debugCredentialCheckScenario = DebugDomainCredentialCheckScenario.None;
    private DebugDomainCredentialVerifier? _debugCredentialVerifier;
    private bool _isCheckingLaunchCredentials;
    private bool _wasOnDomainJoinStep;
    private bool _isDebugCredentialPreviewPending;
    private BootMediaUpdateReason _bootMediaUpdateReason;
    private BootMediaUpdateReason? _debugBootMediaUpdateReasonOverride;
    private bool _isInitialized;
    private bool _isDisposed;
    private Task? _initializationTask;
    private readonly CancellationTokenSource _startupCancellation = new();
    private CancellationTokenSource? _deploymentCancellation;
    private string? _launchFailureMessage;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PreviousWizardStepCommand))]
    [NotifyCanExecuteChangedFor(nameof(NextWizardStepCommand))]
    [NotifyCanExecuteChangedFor(nameof(StartDeploymentCommand))]
    private int wizardStepIndex;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(NextWizardStepCommand))]
    private bool isCatalogLoading;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PreviousWizardStepCommand))]
    [NotifyCanExecuteChangedFor(nameof(NextWizardStepCommand))]
    [NotifyCanExecuteChangedFor(nameof(StartDeploymentCommand))]
    [NotifyCanExecuteChangedFor(nameof(ShowDebugProgressPageCommand))]
    [NotifyCanExecuteChangedFor(nameof(ShowDebugSuccessPageCommand))]
    [NotifyCanExecuteChangedFor(nameof(ShowDebugErrorPageCommand))]
    [NotifyCanExecuteChangedFor(nameof(ShowDebugCancelledPageCommand))]
    [NotifyCanExecuteChangedFor(nameof(SetDebugAutopilotModeCommand))]
    [NotifyCanExecuteChangedFor(nameof(SetDebugDomainJoinModeCommand))]
    [NotifyCanExecuteChangedFor(nameof(ToggleDebugBootMediaUpdateReasonCommand))]
    [NotifyCanExecuteChangedFor(nameof(SetDebugCustomImageScenarioCommand))]
    [NotifyCanExecuteChangedFor(nameof(CancelDeploymentCommand))]
    private bool isDeploymentRunning;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CancelDeploymentCommand))]
    private bool isCancellationPending;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CancelDeploymentCommand))]
    private bool isCompletionStarting;

    public DeploymentPreparationViewModel Preparation { get; }
    public DeploymentSessionViewModel Session { get; }
    public OperatingSystemCatalogViewModel OperatingSystemCatalog { get; }
    public DriverPackSelectionViewModel DriverPackSelection { get; }
    public ObservableCollection<SupportedCultureOption> SupportedCultures { get; } = [];
    public ObservableCollection<DeploymentWizardStepViewModel> WizardSteps { get; } = [];
    public ObservableCollection<DeploymentSummaryCategoryViewModel> SummaryCategories { get; } = [];

    public CultureInfo CurrentCulture => LocalizationService.CurrentCulture;
    public DeployThemeMode CurrentTheme => _themeService.CurrentTheme;
    public bool IsDebugSafeMode => DebugSafetyMode.IsEnabled;
    public string EffectiveOsArchitecture => OperatingSystemCatalog.EffectiveOsArchitecture;
    public CustomImageSelectionViewModel CustomImages => _wizardContext.CustomImages;
    public DomainJoinStepViewModel DomainJoinStep => _wizardContext.DomainJoinStep;
    public OperatingSystemMetadata? SelectedOperatingSystem => _wizardContext.SelectedOperatingSystem;
    public string WindowTitle => GetString("App.WindowTitle");
    public string VersionDisplay => Format("Common.VersionFormat", FoundryDeployApplicationInfo.Version);

    /// <summary>Indicates whether the startup reason or active Debug preview displays the boot media banner.</summary>
    public bool IsBootMediaUpdateRecommended => EffectiveBootMediaUpdateReason != BootMediaUpdateReason.None;

    /// <summary>Gets the localized caption for the displayed boot media reason.</summary>
    public string BootMediaUpdateRecommendedText => GetString(
        EffectiveBootMediaUpdateReason == BootMediaUpdateReason.NewerRelease
            ? "BootMedia.UpdateAvailable"
            : "BootMedia.UpdateRecommended");

    /// <summary>Gets the localized explanation for the displayed boot media reason.</summary>
    public string BootMediaUpdateRecommendedToolTip => GetString(
        EffectiveBootMediaUpdateReason == BootMediaUpdateReason.UnknownAuthoringVersion
            ? "BootMedia.UnknownAuthoringVersionToolTip"
            : "BootMedia.UpdateRecommendedToolTip");

    /// <summary>Indicates whether the newer release banner choice is active in Debug safe mode.</summary>
    public bool IsDebugBootMediaUpdateAvailable => IsDebugSafeMode && EffectiveBootMediaUpdateReason == BootMediaUpdateReason.NewerRelease;

    /// <summary>Indicates whether the unknown authoring version banner choice is active in Debug safe mode.</summary>
    public bool IsDebugBootMediaUpdateRecommended => IsDebugSafeMode && EffectiveBootMediaUpdateReason == BootMediaUpdateReason.UnknownAuthoringVersion;

    private BootMediaUpdateReason EffectiveBootMediaUpdateReason => IsDebugSafeMode
        ? _debugBootMediaUpdateReasonOverride ?? _bootMediaUpdateReason
        : _bootMediaUpdateReason;

    public string OperatingSystemArchitectureDisplay => Format("Catalog.ArchitectureFormat", OperatingSystemCatalog.EffectiveOsArchitecture);
    public string SummaryTargetDiskText => Preparation.SelectedTargetDisk?.DisplayLabel ?? GetString("Summary.NoDiskSelected");
    public string SummaryOperatingSystemText => SelectedOperatingSystem is null
        ? GetString("Summary.NoSelection")
        : new Converters.OperatingSystemSummaryConverter().Convert(SelectedOperatingSystem, typeof(string), string.Empty, LocalizationService.CurrentCulture)?.ToString() ?? GetString("Summary.NoSelection");
    public string SummaryFirmwareText => Preparation.ApplyFirmwareUpdates ? GetString("Common.Enabled") : GetString("Common.Disabled");
    public string SummaryAutopilotProfileText => Preparation.SelectedAutopilotProfile?.DisplayName ?? GetString("Common.None");
    public string SummaryAutopilotGroupTagText => Preparation.EffectiveHardwareHashGroupTagText;
    public DeploymentWizardStepId CurrentWizardStepId => WizardSteps[WizardStepIndex].Id;
    public bool IsOperatingSystemStep => CurrentWizardStepId == DeploymentWizardStepId.OperatingSystem;
    public bool IsDriversStep => CurrentWizardStepId == DeploymentWizardStepId.Drivers;
    public bool IsAutopilotStep => CurrentWizardStepId == DeploymentWizardStepId.Autopilot;
    public bool IsDomainJoinStep => CurrentWizardStepId == DeploymentWizardStepId.DomainJoin;
    public bool IsSummaryStep => CurrentWizardStepId == DeploymentWizardStepId.Summary;
    public bool IsReturningToSummary => _wizardNavigationState.IsReturningToSummary;
    public bool IsDebugAutopilotNoneMode => IsDebugAutopilotMode(DebugAutopilotMode.None);
    public bool IsDebugAutopilotJsonProfileMode => IsDebugAutopilotMode(DebugAutopilotMode.JsonProfile);
    public bool IsDebugAutopilotHardwareHashUploadValidCertificateMode => IsDebugAutopilotMode(DebugAutopilotMode.HardwareHashUploadValidCertificate);
    public bool IsDebugAutopilotHardwareHashUploadExpiredCertificateMode => IsDebugAutopilotMode(DebugAutopilotMode.HardwareHashUploadExpiredCertificate);
    public bool IsDebugAutopilotHardwareHashUploadMissingCertificateMetadataMode => IsDebugAutopilotMode(DebugAutopilotMode.HardwareHashUploadMissingCertificateMetadata);
    public bool IsDebugAutopilotHardwareHashUploadNoDefaultGroupTagMode => IsDebugAutopilotMode(DebugAutopilotMode.HardwareHashUploadNoDefaultGroupTag);
    public bool IsDebugDomainJoinNoneMode => IsDebugDomainJoinMode(DebugDomainJoinMode.None);
    public bool IsDebugDomainJoinInteractiveMode => IsDebugDomainJoinMode(DebugDomainJoinMode.Interactive);
    public bool IsDebugDomainJoinZeroTouchMode => IsDebugDomainJoinMode(DebugDomainJoinMode.ZeroTouch);
    public bool IsDebugCredentialCheckNoneScenario => IsDebugCredentialCheckScenario(DebugDomainCredentialCheckScenario.None);
    public bool IsDebugCredentialCheckCheckingScenario => IsDebugCredentialCheckScenario(DebugDomainCredentialCheckScenario.Checking);
    public bool IsDebugCredentialCheckValidScenario => IsDebugCredentialCheckScenario(DebugDomainCredentialCheckScenario.Valid);
    public bool IsDebugCredentialCheckRejectedScenario => IsDebugCredentialCheckScenario(DebugDomainCredentialCheckScenario.Rejected);
    public bool IsDebugCredentialCheckAccountLockedScenario => IsDebugCredentialCheckScenario(DebugDomainCredentialCheckScenario.AccountLocked);
    public bool IsDebugCredentialCheckPasswordExpiredScenario => IsDebugCredentialCheckScenario(DebugDomainCredentialCheckScenario.PasswordExpired);
    public bool IsDebugCredentialCheckAccountDisabledScenario => IsDebugCredentialCheckScenario(DebugDomainCredentialCheckScenario.AccountDisabled);
    public bool IsDebugCredentialCheckDomainUnreachableScenario => IsDebugCredentialCheckScenario(DebugDomainCredentialCheckScenario.DomainUnreachable);
    public bool IsDebugCredentialCheckInconclusiveScenario => IsDebugCredentialCheckScenario(DebugDomainCredentialCheckScenario.Inconclusive);

    /// <summary>
    /// Gets the verifier of the credential check: the domain itself, or in debug safe mode the scripted debug answer, and none
    /// while no debug answer is chosen, so debug safe mode never contacts a domain.
    /// </summary>
    private IDomainCredentialVerifier? CredentialVerifier => IsDebugSafeMode ? _debugCredentialVerifier : _domainCredentialVerifier;

    private IZeroTouchMediaCredentialCheck? ZeroTouchMediaCredentialCheck => IsDebugSafeMode ? _debugCredentialVerifier : _zeroTouchMediaCredentialCheck;

    public MainWindowViewModel(
        ILocalizationService localizationService,
        IThemeService themeService,
        IOperationProgressService operationProgressService,
        IDeploymentAccessGate deploymentAccessGate,
        IDeploymentStartupCoordinator deploymentStartupCoordinator,
        IDeploymentRuntimeContextService deploymentRuntimeContextService,
        IDeploymentLaunchPreparationService deploymentLaunchPreparationService,
        IDeploymentExecutionService deploymentExecutionService,
        IDeploymentWizardStateService deploymentWizardStateService,
        IDeploymentOrchestrator deploymentOrchestrator,
        IApplicationShellService applicationShellService,
        IDeploymentWizardContextFactory deploymentWizardContextFactory,
        IProcessRunner processRunner,
        IKeyboardLayoutSwitcher keyboardLayoutSwitcher,
        IDomainCredentialVerifier domainCredentialVerifier,
        IZeroTouchMediaCredentialCheck zeroTouchMediaCredentialCheck,
        IZeroTouchDomainCredentialCheck zeroTouchDomainCredentialCheck,
        INetworkCacheStartupDialogService networkCacheStartupDialog,
        ITelemetryService telemetryService,
        ILogger<MainWindowViewModel> logger,
        INetworkAdapterSnapshotProvider? networkAdapterSnapshotProvider = null)
        : base(localizationService)
    {
        _keyboardLayoutSwitcher = keyboardLayoutSwitcher;
        _themeService = themeService;
        _deploymentAccessGate = deploymentAccessGate;
        _deploymentStartupCoordinator = deploymentStartupCoordinator;
        _deploymentLaunchPreparationService = deploymentLaunchPreparationService;
        _deploymentExecutionService = deploymentExecutionService;
        _deploymentWizardStateService = deploymentWizardStateService;
        _deploymentOrchestrator = deploymentOrchestrator;
        _applicationShellService = applicationShellService;
        _domainCredentialVerifier = domainCredentialVerifier;
        _zeroTouchMediaCredentialCheck = zeroTouchMediaCredentialCheck;
        _zeroTouchDomainCredentialCheck = zeroTouchDomainCredentialCheck;
        _networkCacheStartupDialog = networkCacheStartupDialog;
        _telemetryService = telemetryService;
        _logger = logger;
        _dispatcher = Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;
        _deploymentRuntimeContext = deploymentRuntimeContextService.Resolve();
        _wizardContext = deploymentWizardContextFactory.Create(IsDebugSafeMode);
        _wizardContext.StateChanged += OnWizardContextStateChanged;
        Preparation = _wizardContext.Preparation;
        OperatingSystemCatalog = _wizardContext.OperatingSystemCatalog;
        DriverPackSelection = _wizardContext.DriverPackSelection;
        _wizardNavigationState = new DeploymentWizardNavigationState(
            DeploymentWizardStepDefinition.CreateSequence(HasAutopilotConfigurationStep(), HasDomainJoinStep()));
        RefreshWizardSteps();
        RefreshSummaryCategories();
        Session = new DeploymentSessionViewModel(
            _dispatcher,
            _logger,
            operationProgressService,
            _deploymentOrchestrator,
            processRunner,
            localizationService,
            IsDebugSafeMode,
            networkAdapterSnapshotProvider);
        Session.PropertyChanged += OnSessionPropertyChanged;
        _deploymentOrchestrator.CompletionStarting += OnDeploymentCompletionStarting;
        LocalizationService.LanguageChanged += OnLocalizationLanguageChanged;
        RefreshSupportedCultures();
        RefreshKeyboardMenu();
    }

    /// <summary>
    /// Gets the keyboard menu entries: the active language's layouts, then the other languages.
    /// </summary>
    public ObservableCollection<KeyboardMenuEntry> KeyboardMenuEntries { get; } = [];

    /// <summary>
    /// Gets the keyboard menu header, including the active layout's short name when it is known.
    /// </summary>
    public string KeyboardMenuHeader { get; private set; } = string.Empty;

    public Task InitializeAsync()
    {
        return _initializationTask ??= InitializeCoreAsync();
    }

    private async Task InitializeCoreAsync()
    {
        CancellationToken cancellationToken = _startupCancellation.Token;
        cancellationToken.ThrowIfCancellationRequested();
        if (_isInitialized)
        {
            return;
        }

        if (!await _deploymentAccessGate.AuthorizeAsync())
        {
            _applicationShellService.Shutdown();
            return;
        }

        cancellationToken.ThrowIfCancellationRequested();

        DeploymentStartupSnapshot startupSnapshot = await RunStartupCoordinatorAsync(cancellationToken).ConfigureAwait(false);

        // The wizard never opens while the network cache blocks startup: the snapshot then still carries the boot image
        // sections, which an active network cache no longer uses.
        while (startupSnapshot.NetworkCacheFailure is { } failure)
        {
            bool retry = false;
            RunOnUi(() => retry = _networkCacheStartupDialog.ShowFailure(failure));
            if (!retry)
            {
                _logger.LogWarning("Deployment closed before the wizard because the network cache cannot be used. FailureKind={FailureKind}", failure.Kind);
                // The blocked start ends the session: report it as one. The network is often what failed, so Close waits for the
                // send only briefly.
                try
                {
                    await _telemetryService.TrackAsync(TelemetryEvents.DeploySessionFinished, NetworkCacheTelemetry.DescribeBlockedStart(failure),
                        CancellationToken.None).WaitAsync(BlockedStartTelemetryTimeout).ConfigureAwait(false);
                }
                catch (TimeoutException)
                {
                    _logger.LogDebug("The blocked start telemetry was not sent before Foundry Deploy closed.");
                }

                RunOnUi(_applicationShellService.Shutdown);
                return;
            }

            cancellationToken.ThrowIfCancellationRequested();
            _logger.LogInformation("Network cache startup check retried after FailureKind={FailureKind}.", failure.Kind);
            startupSnapshot = await RunStartupCoordinatorAsync(cancellationToken).ConfigureAwait(false);
        }

        RunOnUi(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            ApplyStartupSnapshot(startupSnapshot);
            _isInitialized = true;
        });
    }

    private Task<DeploymentStartupSnapshot> RunStartupCoordinatorAsync(CancellationToken cancellationToken) =>
        _deploymentStartupCoordinator.InitializeAsync(
            new DeploymentStartupRequest
            {
                RuntimeContext = _deploymentRuntimeContext,
                IsDebugSafeMode = IsDebugSafeMode,
                FallbackComputerName = ResolveInitialComputerName()
            }, cancellationToken);

    [RelayCommand]
    private void SetCulture(string cultureName)
    {
        LocalizationService.SetCulture(new CultureInfo(cultureName));
    }

    [RelayCommand]
    private void SetKeyboardLayout(string? layoutId)
    {
        KeyboardLayout? layout = KeyboardLayoutSelection.Find(_keyboardLayoutSwitcher.GetLayouts(), layoutId);
        if (layout is null)
        {
            return;
        }

        KeyboardLayoutSwitchResult result = _keyboardLayoutSwitcher.Apply(layout);
        if (!result.Succeeded)
        {
            _logger.LogWarning("Keyboard layout change failed. LayoutId={LayoutId}, Error={Error}", layout.Id, result.Error);
        }
        else if (result.Error is not null)
        {
            _logger.LogWarning("Keyboard layout changed with a warning. LayoutId={LayoutId}, Warning={Warning}", layout.Id, result.Error);
        }
        else
        {
            _logger.LogInformation("Keyboard layout changed. LayoutId={LayoutId}", layout.Id);
        }

        RefreshKeyboardMenu();
    }

    /// <summary>
    /// Re-applies the keyboard layout chosen in Foundry Connect, which runs first.
    /// Call it once the window is active: Windows ignores layout activation for a process that does not own the focused window.
    /// </summary>
    public void RestoreKeyboardLayoutHandoff()
    {
        KeyboardLayoutSwitchResult? result = _keyboardLayoutSwitcher.RestoreHandoff();
        if (result is null)
        {
            return;
        }

        if (!result.Succeeded)
        {
            _logger.LogWarning("Keyboard layout hand-off was not applied. LayoutId={LayoutId}, Error={Error}", result.LayoutId, result.Error);
        }
        else if (result.Error is not null)
        {
            _logger.LogWarning("Keyboard layout restored from Foundry Connect with a warning. LayoutId={LayoutId}, Warning={Warning}", result.LayoutId, result.Error);
        }
        else
        {
            _logger.LogInformation("Keyboard layout restored from Foundry Connect. LayoutId={LayoutId}", result.LayoutId);
        }

        RefreshKeyboardMenu();
    }

    private void RefreshKeyboardMenu()
    {
        IReadOnlyList<KeyboardLayout> layouts = _keyboardLayoutSwitcher.GetLayouts();
        string activeLayoutId = _keyboardLayoutSwitcher.GetActiveLayoutId();
        KeyboardMenuEntries.Clear();
        foreach (KeyboardMenuEntry entry in KeyboardLayoutSelection.BuildMenuEntries(layouts, activeLayoutId, GetString("Keyboard.OtherLanguages")))
        {
            KeyboardMenuEntries.Add(entry);
        }

        KeyboardLayout? activeLayout = KeyboardLayoutSelection.Find(layouts, activeLayoutId);
        KeyboardMenuHeader = activeLayout is null
            ? GetString("Menu.Keyboard")
            : Format("Menu.KeyboardFormat", activeLayout.ShortName);
        OnPropertyChanged(nameof(KeyboardMenuHeader));
    }

    private void RefreshSupportedCultures()
    {
        SupportedCultures.Clear();
        foreach (SupportedCultureOption option in LocalizationService.CreateSupportedCultureOptions())
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

    [RelayCommand(CanExecute = nameof(CanUseCustomImageDebugTools))]
    private async Task SetDebugCustomImageScenarioAsync(DebugCustomImageScenario scenario)
    {
        if (!CanUseCustomImageDebugTools()) return;
        await CustomImages.SetDebugScenarioAsync(scenario);
    }

    private bool CanUseCustomImageDebugTools() => CanUseDebugTools() && Session.IsStartupReady;

    [RelayCommand(CanExecute = nameof(CanUseDebugTools))]
    private void ToggleDebugBootMediaUpdateReason(BootMediaUpdateReason reason)
    {
        if (!CanUseDebugTools() || reason is not (BootMediaUpdateReason.NewerRelease or BootMediaUpdateReason.UnknownAuthoringVersion))
        {
            return;
        }

        _debugBootMediaUpdateReasonOverride = EffectiveBootMediaUpdateReason == reason
            ? BootMediaUpdateReason.None
            : reason;
        RaiseBootMediaUpdatePropertiesChanged();
    }

    [RelayCommand(CanExecute = nameof(CanUseDebugTools))]
    private void SetDebugAutopilotMode(DebugAutopilotMode mode)
    {
        if (!IsDebugSafeMode)
        {
            return;
        }

        // Autopilot and Domain Join are exclusive, so a debug Autopilot scenario replaces a debug Domain Join one.
        if (mode != DebugAutopilotMode.None && _debugDomainJoinMode != DebugDomainJoinMode.None)
        {
            ApplyDebugDomainJoinMode(DebugDomainJoinMode.None);
        }

        _debugAutopilotMode = mode;
        Preparation.ApplyDebugAutopilotMode(mode);
        RaiseDebugAutopilotModePropertiesChanged();
        OnPropertyChanged(nameof(SummaryAutopilotProfileText));
        OnPropertyChanged(nameof(SummaryAutopilotGroupTagText));
        NextWizardStepCommand.NotifyCanExecuteChanged();
        StartDeploymentCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(CanGoPrevious))]
    private void PreviousWizardStep()
    {
        if (_wizardNavigationState.MovePrevious())
        {
            SynchronizeWizardNavigation();
        }
    }

    [RelayCommand(CanExecute = nameof(CanGoNext))]
    private async Task NextWizardStepAsync()
    {
        if (CurrentWizardStepId == DeploymentWizardStepId.DomainJoin && CredentialVerifier is { } verifier &&
            (!await RunDomainCredentialCheckAsync(verifier) || CurrentWizardStepId != DeploymentWizardStepId.DomainJoin))
        {
            return;
        }

        if (_wizardNavigationState.Advance())
        {
            SynchronizeWizardNavigation();
        }
    }

    /// <summary>
    /// Confirms the Interactive Domain Join credentials before the wizard leaves the step or starts a deployment. Logs only the outcome and
    /// numeric codes, never the domain, account, password or server text.
    /// </summary>
    private async Task<bool> RunDomainCredentialCheckAsync(IDomainCredentialVerifier verifier)
    {
        bool contactsDomain = DomainJoinStep.RequiresCredentials && DomainJoinStep.CredentialCheckState == DomainCredentialCheckState.NotChecked;
        if (contactsDomain)
        {
            _logger.LogInformation("Domain credential check started.");
        }

        bool confirmed = await DomainJoinStep.ConfirmCredentialsAsync(verifier, CancellationToken.None);
        if (!contactsDomain)
        {
            return confirmed;
        }

        DomainCredentialCheckResult? result = DomainJoinStep.LastCredentialCheck;
        if (result is null)
        {
            _logger.LogInformation("Domain credential check discarded because the inputs changed.");
            return confirmed;
        }

        _logger.Log(
            result.Outcome == DomainCredentialCheckOutcome.Valid ? LogLevel.Information : LogLevel.Warning,
            "Domain credential check completed with {Outcome} (LDAP {LdapErrorCode}, data {DirectoryDataCode:x}, native {NativeErrorCode}).",
            result.Outcome,
            result.LdapErrorCode,
            result.DirectoryDataCode,
            result.NativeErrorCode);
        return confirmed;
    }

    [RelayCommand]
    private void NavigateToWizardStep(DeploymentWizardStepViewModel? step)
    {
        if (step is not null && CanNavigateWizard() && _wizardNavigationState.TryNavigateTo(step.Id))
        {
            SynchronizeWizardNavigation();
        }
    }

    [RelayCommand]
    private void EditSummaryStep(DeploymentWizardStepId stepId)
    {
        if (CanNavigateWizard() && _wizardNavigationState.BeginSummaryEdit(stepId))
        {
            SynchronizeWizardNavigation();
        }
    }

    [RelayCommand(CanExecute = nameof(CanBeginWizard))]
    private void BeginWizard()
    {
        Session.ShowWizard();
    }

    /// <summary>
    /// Prepares the launch from the current wizard state. The prepared input takes its own copy of the domain password,
    /// so the technician's submission is disposed as soon as preparation returns instead of living for the whole deployment.
    /// </summary>
    private DeploymentLaunchPreparationResult PrepareLaunch()
    {
        DriverPackSelectionKind effectiveDriverPackKind = DriverPackSelection.EffectiveSelectionKind;
        DriverPackCatalogItem? effectiveDriverPack = DriverPackSelection.ResolveEffectiveSelection();
        using DomainJoinSubmission? domainJoinSubmission = DomainJoinStep.CreateSubmission();
        return _deploymentLaunchPreparationService.Prepare(
            new DeploymentLaunchRequest
            {
                Mode = _deploymentRuntimeContext.Mode,
                CacheRootPath = Preparation.CacheRootPath,
                Unattend = Preparation.SelectedUnattend,
                TargetComputerName = Preparation.TargetComputerName,
                UploadComputerNameToAutopilot = Preparation.UploadComputerNameToAutopilot,
                SelectedTargetDisk = Preparation.SelectedTargetDisk,
                SelectedOperatingSystem = SelectedOperatingSystem,
                DriverPackSelectionKind = effectiveDriverPackKind,
                SelectedDriverPack = effectiveDriverPack,
                SelectedCustomDriverSet = DriverPackSelection.ResolveEffectiveCustomDriverSet(),
                CustomDrivers = _wizardContext.CustomDrivers,
                ApplyFirmwareUpdates = Preparation.ApplyFirmwareUpdates,
                IsAutopilotEnabled = Preparation.IsAutopilotEnabled,
                AutopilotProvisioningMode = Preparation.AutopilotProvisioningMode,
                SelectedAutopilotProfile = Preparation.SelectedAutopilotProfile,
                AutopilotHardwareHashUpload = Preparation.CreateAutopilotHardwareHashUploadForLaunch(),
                Network = _wizardContext.Network,
                Oobe = _wizardContext.Oobe,
                PreOobe = _wizardContext.PreOobe,
                AppxRemoval = _wizardContext.AppxRemoval,
                AiComponentRemoval = _wizardContext.AiComponentRemoval,
                WindowsOptionalFeatures = _wizardContext.WindowsOptionalFeatures,
                Completion = _wizardContext.Completion,
                IsDryRun = IsDebugSafeMode
            }, _wizardContext.DomainJoin, domainJoinSubmission);
    }

    [RelayCommand(CanExecute = nameof(CanStartDeployment))]
    private async Task StartDeploymentAsync()
    {
        _logger.LogInformation("Start deployment requested.");
        ReportLaunchFailure(null);

        // Inputs edited from the summary or the step list have not been checked yet.
        if (CredentialVerifier is { } verifier && HasDomainJoinStep() && DomainJoinStep.NeedsCredentialCheck && !await RunDomainCredentialCheckAsync(verifier))
        {
            if (_wizardNavigationState.TryNavigateTo(DeploymentWizardStepId.DomainJoin))
            {
                SynchronizeWizardNavigation();
            }

            return;
        }

        // The check awaited the network; start only from the reviewed summary with inputs that are still valid.
        if (!CanStartDeployment())
        {
            return;
        }

        using DeploymentLaunchPreparationResult launchPreparation = PrepareLaunch();

        if (!Preparation.UsesCustomUnattend &&
            !string.Equals(Preparation.TargetComputerName, launchPreparation.NormalizedComputerName, StringComparison.Ordinal))
        {
            Preparation.TargetComputerName = launchPreparation.NormalizedComputerName;
        }

        if (!launchPreparation.IsReadyToStart || launchPreparation.Context is null)
        {
            if (launchPreparation.IsUnattendFailure)
                Preparation.ReportUnattendFailure(launchPreparation.FailureMessage!);
            else
                ReportLaunchFailure(launchPreparation.FailureMessage);
            return;
        }

        if (!await CheckZeroTouchCredentialsBeforeStartAsync(launchPreparation))
        {
            return;
        }

        // The prepared input now owns its own copy; the wizard keeps no password once a deployment starts.
        RunOnUi(DomainJoinStep.ClearPassword);

        RunOnUi(() =>
        {
            _deploymentCancellation = new CancellationTokenSource();
            IsCancellationPending = false;
            IsCompletionStarting = false;
            IsDeploymentRunning = true;
            Session.BeginDeployment(Preparation.EffectiveComputerName, DeploymentPlan.Build(launchPreparation.Context));
        });

        try
        {
            DeploymentExecutionRunResult executionRunResult = await _deploymentExecutionService
                .ExecuteAsync(launchPreparation.Context, launchPreparation.TakeDomainJoinInput(), _deploymentCancellation!.Token)
                .ConfigureAwait(false);

            RunOnUi(() => Session.ApplyExecutionRunResult(executionRunResult));
        }
        finally
        {
            RunOnUi(() =>
            {
                IsDeploymentRunning = false;
                IsCancellationPending = false;
                _deploymentCancellation?.Dispose();
                _deploymentCancellation = null;
            });
        }
    }

    /// <summary>
    /// Checks the Zero-touch account stored on the media when the Domain Join step holds no current result, before anything
    /// touches the disk. A refusal reports a launch failure and the deployment does not start. Navigation stays blocked while the
    /// check waits on the network, and the deployment starts only from the reviewed summary.
    /// </summary>
    private async Task<bool> CheckZeroTouchCredentialsBeforeStartAsync(DeploymentLaunchPreparationResult launchPreparation)
    {
        if (DomainJoinStep.CredentialCheckState is DomainCredentialCheckState.Valid or DomainCredentialCheckState.WarningAcknowledged)
        {
            return true;
        }

        ZeroTouchDomainCredentialCheckDecision decision;
        SetCheckingLaunchCredentials(true);
        try
        {
            decision = await _zeroTouchDomainCredentialCheck.CheckAsync(launchPreparation.Context!, launchPreparation.DomainJoinInput, CancellationToken.None);
        }
        finally
        {
            SetCheckingLaunchCredentials(false);
        }

        if (decision.FailureResourceKey is { } failure)
        {
            ReportLaunchFailure(GetString(failure));
            return false;
        }

        return decision.CanStart && CanStartDeployment();
    }

    private void SetCheckingLaunchCredentials(bool value)
    {
        _isCheckingLaunchCredentials = value;
        UpdateWizardStepStates();
        PreviousWizardStepCommand.NotifyCanExecuteChanged();
    }

    /// <summary>
    /// Runs the Zero-touch check of the media account while the Domain Join step is shown, never in debug safe mode without a
    /// debug answer. In debug safe mode, Interactive media also shows the chosen debug answer as soon as the step is shown, so the
    /// card can be reviewed without typing an account.
    /// </summary>
    private void UpdateStepCredentialCheck()
    {
        bool isOnStep = CurrentWizardStepId == DeploymentWizardStepId.DomainJoin;
        bool isArrival = isOnStep && (!_wasOnDomainJoinStep || _isDebugCredentialPreviewPending);
        _wasOnDomainJoinStep = isOnStep;
        if (isOnStep && CredentialVerifier is { } verifier && ZeroTouchMediaCredentialCheck is { } mediaCheck)
        {
            DomainJoinStep.BeginZeroTouchCheck(mediaCheck, verifier, _logger);
        }
        else
        {
            DomainJoinStep.EndZeroTouchCheck();
        }

        if (isArrival && IsDebugSafeMode && _debugCredentialVerifier is { } debugVerifier && DomainJoinStep.RequiresCredentials &&
            DomainJoinStep.CredentialCheckState == DomainCredentialCheckState.NotChecked)
        {
            _isDebugCredentialPreviewPending = false;
            _ = RunDomainCredentialCheckAsync(debugVerifier);
        }
    }

    /// <summary>Shows a launch failure in the review summary without invalidating any wizard selection, so Start stays available.</summary>
    private void ReportLaunchFailure(string? message)
    {
        if (string.Equals(_launchFailureMessage, message, StringComparison.Ordinal))
        {
            return;
        }

        _launchFailureMessage = message;
        RefreshSummaryCategories();
    }

    [RelayCommand(CanExecute = nameof(CanCancelDeployment))]
    private void CancelDeployment()
    {
        if (!CanCancelDeployment())
        {
            return;
        }

        IsCancellationPending = true;
        _deploymentCancellation!.Cancel();
    }

    private bool CanCancelDeployment() => IsDeploymentRunning && !IsCancellationPending && !IsCompletionStarting && _deploymentCancellation is not null;

    private void OnDeploymentCompletionStarting(object? sender, EventArgs e)
    {
        // A synchronous dispatcher handoff closes cancellation before the orchestrator selects
        // its terminal outcome, including the later telemetry and result-dispatch interval.
        RunOnUi(() => IsCompletionStarting = true);
    }

    [RelayCommand(CanExecute = nameof(CanShowDebugPages))]
    private void ShowDebugProgressPage()
    {
        Session.ShowDebugProgress(CreateDebugPreviewContext());
    }

    [RelayCommand(CanExecute = nameof(CanShowDebugPages))]
    private void ShowDebugSuccessPage()
    {
        Session.ShowDebugSuccess(CreateDebugPreviewContext());
    }

    [RelayCommand(CanExecute = nameof(CanShowDebugPages))]
    private void ShowDebugErrorPage()
    {
        Session.ShowDebugError(
            CreateDebugPreviewContext(),
            failedStepErrorMessage:
            "Debug preview: DISM apply failed because the target partition is read-only.\n\n" +
            "ErrorCode=0x80070005\n" +
            "Details: Access denied while mounting image to target path.\n" +
            "Action: Verify disk attributes and retry deployment.");
    }

    [RelayCommand(CanExecute = nameof(CanShowDebugPages))]
    private void ShowDebugCancelledPage()
    {
        Session.ShowDebugCancelled(CreateDebugPreviewContext());
    }

    // Preview requests reflect wizard choices without requiring complete inputs or performing launch validation.
    private DeploymentContext CreateDebugPreviewContext() => new()
    {
        Mode = _deploymentRuntimeContext.Mode,
        CacheRootPath = Preparation.CacheRootPath,
        TargetDiskNumber = Preparation.SelectedTargetDisk?.DiskNumber ?? 0,
        TargetComputerName = Preparation.EffectiveComputerName,
        OperatingSystem = SelectedOperatingSystem ?? new OperatingSystemCatalogItem(),
        Unattend = Preparation.SelectedUnattend,
        DriverPackSelectionKind = DriverPackSelection.EffectiveSelectionKind,
        DriverPack = DriverPackSelection.ResolveEffectiveSelection(),
        CustomDriverSet = DriverPackSelection.ResolveEffectiveCustomDriverSet(),
        CustomDrivers = _wizardContext.CustomDrivers,
        ApplyFirmwareUpdates = Preparation.ApplyFirmwareUpdates,
        IsAutopilotEnabled = Preparation.IsAutopilotEnabled,
        AutopilotProvisioningMode = Preparation.AutopilotProvisioningMode,
        Network = _wizardContext.Network,
        Oobe = _wizardContext.Oobe,
        PreOobe = _wizardContext.PreOobe,
        AppxRemoval = _wizardContext.AppxRemoval,
        AiComponentRemoval = _wizardContext.AiComponentRemoval,
        WindowsOptionalFeatures = _wizardContext.WindowsOptionalFeatures,
        IsDryRun = true
    };

    private void OnWizardContextStateChanged(object? sender, EventArgs e)
    {
        _launchFailureMessage = null;
        RefreshWizardSteps();
        RefreshSummaryCategories();
        OnPropertyChanged(nameof(EffectiveOsArchitecture));
        OnPropertyChanged(nameof(SelectedOperatingSystem));
        OnPropertyChanged(nameof(OperatingSystemArchitectureDisplay));
        OnPropertyChanged(nameof(SummaryTargetDiskText));
        OnPropertyChanged(nameof(SummaryOperatingSystemText));
        OnPropertyChanged(nameof(SummaryFirmwareText));
        OnPropertyChanged(nameof(SummaryAutopilotProfileText));
        OnPropertyChanged(nameof(SummaryAutopilotGroupTagText));
        NextWizardStepCommand.NotifyCanExecuteChanged();
        StartDeploymentCommand.NotifyCanExecuteChanged();
    }

    private void OnSessionPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(e.PropertyName) &&
            e.PropertyName is not nameof(DeploymentSessionViewModel.IsStartupInitializing) &&
            e.PropertyName is not nameof(DeploymentSessionViewModel.CurrentPage))
        {
            return;
        }

        BeginWizardCommand.NotifyCanExecuteChanged();
        SetDebugCustomImageScenarioCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(CanUseDebugTools))]
    private void SetDebugDomainJoinMode(DebugDomainJoinMode mode)
    {
        if (!IsDebugSafeMode)
        {
            return;
        }

        if (mode != DebugDomainJoinMode.None && _debugAutopilotMode != DebugAutopilotMode.None)
        {
            SetDebugAutopilotMode(DebugAutopilotMode.None);
        }

        ApplyDebugDomainJoinMode(mode);
    }

    private void ApplyDebugDomainJoinMode(DebugDomainJoinMode mode)
    {
        _debugDomainJoinMode = mode;
        ReplaceDebugCredentialVerifier();
        _wizardContext.ApplyDebugDomainJoin(DebugDomainJoinScenarios.Create(mode));
        // Set once the step is configured: configuring raises refreshes that would consume an earlier flag.
        _isDebugCredentialPreviewPending = true;
        UpdateStepCredentialCheck();
        OnPropertyChanged(nameof(IsDebugDomainJoinNoneMode));
        OnPropertyChanged(nameof(IsDebugDomainJoinInteractiveMode));
        OnPropertyChanged(nameof(IsDebugDomainJoinZeroTouchMode));
    }

    [RelayCommand(CanExecute = nameof(CanUseDebugTools))]
    private void SetDebugCredentialCheckScenario(DebugDomainCredentialCheckScenario scenario)
    {
        if (!IsDebugSafeMode)
        {
            return;
        }

        _debugCredentialCheckScenario = scenario;
        ReplaceDebugCredentialVerifier();
        DomainJoinStep.EndZeroTouchCheck();
        // Configuring the step again resets its inputs and discards the previous answer; the media configuration stays the same.
        DomainJoinStep.Configure(_wizardContext.DomainJoin);
        _isDebugCredentialPreviewPending = true;
        UpdateStepCredentialCheck();
        foreach (string name in new[]
                 {
                     nameof(IsDebugCredentialCheckNoneScenario), nameof(IsDebugCredentialCheckCheckingScenario), nameof(IsDebugCredentialCheckValidScenario),
                     nameof(IsDebugCredentialCheckRejectedScenario), nameof(IsDebugCredentialCheckAccountLockedScenario),
                     nameof(IsDebugCredentialCheckPasswordExpiredScenario), nameof(IsDebugCredentialCheckAccountDisabledScenario),
                     nameof(IsDebugCredentialCheckDomainUnreachableScenario), nameof(IsDebugCredentialCheckInconclusiveScenario)
                 })
        {
            OnPropertyChanged(name);
        }
    }

    private bool IsDebugCredentialCheckScenario(DebugDomainCredentialCheckScenario scenario) => IsDebugSafeMode && _debugCredentialCheckScenario == scenario;

    /// <summary>Releases a pending debug answer, which ends a waiting wizard command, and serves the chosen scenario afresh.</summary>
    private void ReplaceDebugCredentialVerifier()
    {
        _debugCredentialVerifier?.Dispose();
        _debugCredentialVerifier = _debugCredentialCheckScenario == DebugDomainCredentialCheckScenario.None
            ? null
            : new DebugDomainCredentialVerifier(_debugCredentialCheckScenario);
    }

    private bool CanShowDebugPages()
    {
        return IsDebugSafeMode && !IsDeploymentRunning;
    }

    private bool CanUseDebugTools()
    {
        return IsDebugSafeMode && !IsDeploymentRunning;
    }

    private void RaiseBootMediaUpdatePropertiesChanged()
    {
        OnPropertyChanged(nameof(IsBootMediaUpdateRecommended));
        OnPropertyChanged(nameof(BootMediaUpdateRecommendedText));
        OnPropertyChanged(nameof(BootMediaUpdateRecommendedToolTip));
        OnPropertyChanged(nameof(IsDebugBootMediaUpdateAvailable));
        OnPropertyChanged(nameof(IsDebugBootMediaUpdateRecommended));
    }

    private bool IsDebugAutopilotMode(DebugAutopilotMode mode)
    {
        return IsDebugSafeMode && _debugAutopilotMode == mode;
    }

    private bool IsDebugDomainJoinMode(DebugDomainJoinMode mode)
    {
        return IsDebugSafeMode && _debugDomainJoinMode == mode;
    }

    private void RaiseDebugAutopilotModePropertiesChanged()
    {
        OnPropertyChanged(nameof(IsDebugAutopilotNoneMode));
        OnPropertyChanged(nameof(IsDebugAutopilotJsonProfileMode));
        OnPropertyChanged(nameof(IsDebugAutopilotHardwareHashUploadValidCertificateMode));
        OnPropertyChanged(nameof(IsDebugAutopilotHardwareHashUploadExpiredCertificateMode));
        OnPropertyChanged(nameof(IsDebugAutopilotHardwareHashUploadMissingCertificateMetadataMode));
        OnPropertyChanged(nameof(IsDebugAutopilotHardwareHashUploadNoDefaultGroupTagMode));
    }

    private bool CanBeginWizard()
    {
        return Session.IsSplashPage && Session.IsStartupReady;
    }

    private bool CanGoPrevious()
    {
        return _deploymentWizardStateService.CanGoPrevious(BuildWizardStateSnapshot());
    }

    private bool CanNavigateWizard()
    {
        return _deploymentWizardStateService.CanNavigate(BuildWizardStateSnapshot());
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
        IReadOnlyList<DeploymentWizardStepDefinition> availableSteps = WizardSteps
            .Select(step => step.Definition)
            .ToArray();

        return new DeploymentWizardStateSnapshot
        {
            CurrentStepId = CurrentWizardStepId,
            AvailableSteps = availableSteps,
            IsDeploymentRunning = IsDeploymentRunning,
            IsCatalogLoading = IsCatalogLoading,
            IsTargetDiskLoading = Preparation.IsTargetDiskLoading,
            IsDebugSafeMode = IsDebugSafeMode,
            IsUnattendSelectionValid = Preparation.IsUnattendSelectionValid,
            IsTargetComputerNameValid = Preparation.IsTargetComputerNameValid,
            HasSelectedOperatingSystem = SelectedOperatingSystem is not null,
            HasTargetDiskSelection = Preparation.SelectedTargetDisk is not null,
            IsSelectedTargetDiskSelectable = Preparation.SelectedTargetDisk?.IsSelectable ?? false,
            HasValidDriverPackSelection = HasValidDriverPackSelection(),
            HasValidAutopilotSelection =
                !Preparation.IsAutopilotEnabled ||
                Preparation.AutopilotProvisioningMode == AutopilotProvisioningMode.HardwareHashUpload ||
                Preparation.AutopilotProvisioningMode == AutopilotProvisioningMode.InteractiveHardwareHashUpload ||
                Preparation.SelectedAutopilotProfile is not null,
            HasValidDomainJoinInput = !HasDomainJoinStep() || DomainJoinStep.IsValid,
            IsCheckingDomainCredentials = DomainJoinStep.IsCheckingCredentials || _isCheckingLaunchCredentials,
            IsCustomImageMode = CustomImages.IsCustom,
            IsOperatingSystemCatalogReadyForNavigation = !IsCatalogLoading && (CustomImages.IsEnabled || OperatingSystemCatalog.IsReadyForNavigation())
        };
    }

    partial void OnWizardStepIndexChanged(int value)
    {
        UpdateWizardStepStates();
        OnPropertyChanged(nameof(CurrentWizardStepId));
        OnPropertyChanged(nameof(IsOperatingSystemStep));
        OnPropertyChanged(nameof(IsDriversStep));
        OnPropertyChanged(nameof(IsAutopilotStep));
        OnPropertyChanged(nameof(IsDomainJoinStep));
        OnPropertyChanged(nameof(IsSummaryStep));
        OnPropertyChanged(nameof(IsReturningToSummary));
    }

    private void RefreshWizardSteps()
    {
        IReadOnlyList<DeploymentWizardStepDefinition> definitions =
            DeploymentWizardStepDefinition.CreateSequence(HasAutopilotConfigurationStep(), HasDomainJoinStep());
        bool sequenceChanged = WizardSteps.Count != definitions.Count ||
                               WizardSteps.Select(step => step.Id).Where((id, index) => id != definitions[index].Id).Any();
        if (sequenceChanged)
        {
            _wizardNavigationState.ReplaceSteps(definitions);
        }

        WizardSteps.Clear();
        for (int index = 0; index < definitions.Count; index++)
        {
            DeploymentWizardStepDefinition definition = definitions[index];
            WizardSteps.Add(new DeploymentWizardStepViewModel(
                definition,
                GetString(definition.ResourceKey),
                index + 1,
                index == definitions.Count - 1));
        }

        SynchronizeWizardNavigation();
    }

    private void SynchronizeWizardNavigation()
    {
        WizardStepIndex = WizardSteps
            .Select((step, index) => (step, index))
            .First(item => item.step.Id == _wizardNavigationState.CurrentStepId)
            .index;
        UpdateWizardStepStates();
        UpdateStepCredentialCheck();
        OnPropertyChanged(nameof(IsReturningToSummary));
        PreviousWizardStepCommand.NotifyCanExecuteChanged();
        NextWizardStepCommand.NotifyCanExecuteChanged();
        StartDeploymentCommand.NotifyCanExecuteChanged();
    }

    private void UpdateWizardStepStates()
    {
        for (int index = 0; index < WizardSteps.Count; index++)
        {
            DeploymentWizardStepViewModel step = WizardSteps[index];
            step.IsCurrent = index == WizardStepIndex;
            step.IsCompleted = index < WizardStepIndex || _wizardNavigationState.CanNavigateTo(step.Id) && !step.IsCurrent;
            step.IsEnabled = _wizardNavigationState.CanNavigateTo(step.Id) && !step.IsCurrent && !DomainJoinStep.IsCheckingCredentials &&
                             !_isCheckingLaunchCredentials;
            step.IsConnectorCompleted = _wizardNavigationState.HasAdvancedPast(step.Id);
        }
    }

    private void RefreshSummaryCategories()
    {
        bool hasCustomization = Preparation.UsesCustomUnattend || _wizardContext.Oobe.IsEnabled ||
                                _wizardContext.AppxRemoval.IsEnabled ||
                                _wizardContext.AiComponentRemoval.IsEnabled ||
                                _wizardContext.WindowsOptionalFeatures.IsEnabled;
        bool hasNetwork = _wizardContext.Network.ProfileRoaming.IsAnyEnabled;
        OperatingSystemMetadata? operatingSystem = SelectedOperatingSystem;
        var source = new DeploymentSummarySource
        {
            TargetSummary = Preparation.EffectiveComputerName,
            IsTargetConfigured = Preparation.IsUnattendSelectionValid && Preparation.IsTargetComputerNameValid &&
                                 (IsDebugSafeMode || Preparation.SelectedTargetDisk?.IsSelectable == true),
            HasTargetWarning = Preparation.HasUnattendWarning || Preparation.HasUnattendValidationError || _launchFailureMessage is not null ||
                               Preparation.SelectedTargetDisk is { IsSelectable: false },
            TargetRows = BuildTargetSummaryRows(),
            OperatingSystemSummary = SummaryOperatingSystemText,
            IsOperatingSystemConfigured = operatingSystem is not null,
            OperatingSystemRows = operatingSystem is null
                ? [new(GetString("Summary.SelectedOperatingSystem"), GetString("Summary.NoSelection"))]
                : operatingSystem is CustomImageSelection custom
                ?
                [
                    new(GetString("CustomImages.Image"), custom.Asset.DisplayLabel),
                    new(GetString("CustomImages.Index"), custom.Index.Index.ToString(CultureInfo.InvariantCulture)),
                    new(GetString("Summary.Edition"), custom.Edition),
                    new(GetString("Summary.Architecture"), custom.Architecture),
                    new(GetString("Summary.Language"), custom.LanguageCode),
                    new(GetString("Summary.Build"), custom.Build)
                ]
                :
                [
                    new(GetString("Summary.Release"), $"Windows {operatingSystem.WindowsRelease} {operatingSystem.ReleaseId}"),
                    new(GetString("Summary.Edition"), operatingSystem.Edition),
                    new(GetString("Summary.Architecture"), operatingSystem.Architecture),
                    new(GetString("Summary.Language"), operatingSystem.LanguageCode),
                    new(GetString("Summary.LicenseChannel"), operatingSystem.LicenseChannel),
                    new(GetString("Summary.Build"), operatingSystem.Build)
                ],
            DriversSummary = DriverPackSelection.SelectedDriverPackSelectionDisplay,
            DriverRows = BuildDriverSummaryRows(),
            IsDriversConfigured = DriverPackSelection.EffectiveSelectionKind != DriverPackSelectionKind.None,
            AutopilotSummary = Preparation.AutopilotModeText,
            AutopilotRows = BuildAutopilotSummaryRows(),
            IsAutopilotConfigured = Preparation.IsAutopilotEnabled,
            HasAutopilotStep = HasAutopilotConfigurationStep(),
            DomainJoinSummary = !IsDomainJoinApplicable() ? GetString("Summary.Status.NotConfigured") :
                DomainJoinStep.IsJoinDeclined ? GetString("DomainJoin.DoNotJoin") : DomainJoinStep.DomainName,
            DomainJoinRows = BuildDomainJoinSummaryRows(),
            IsDomainJoinConfigured = _wizardContext.DomainJoin.IsEnabled,
            HasDomainJoinWarning = !IsDomainJoinApplicable(),
            IsDomainJoinDeclined = IsDomainJoinApplicable() && DomainJoinStep.IsJoinDeclined,
            HasDomainJoinStep = HasDomainJoinStep(),
            WindowsCustomizationSummary = hasCustomization
                ? GetString("Summary.Status.Configured")
                : GetString("Summary.Status.NoChanges"),
            WindowsCustomizationRows = BuildWindowsCustomizationSummaryRows(),
            IsWindowsCustomizationConfigured = hasCustomization,
            NetworkSummary = hasNetwork
                ? GetString("Summary.Status.Configured")
                : GetString("Summary.Status.NotConfigured"),
            NetworkRows = BuildNetworkSummaryRows(),
            IsNetworkConfigured = hasNetwork,
            CompletionSummary = _wizardContext.Completion.AutomaticRebootEnabled
                ? GetString("Summary.AutomaticRestart")
                : GetString("Summary.ManualRestart"),
            CompletionRows = BuildCompletionSummaryRows()
        };

        var builder = new DeploymentSummaryBuilder(GetString);
        SummaryCategories.Clear();
        foreach (DeploymentSummaryCategoryViewModel category in builder.Build(source))
        {
            SummaryCategories.Add(category);
        }
    }

    private IReadOnlyList<DeploymentSummaryRowViewModel> BuildTargetSummaryRows()
    {
        List<DeploymentSummaryRowViewModel> rows =
        [
            DeploymentSummaryRowViewModel.Section(GetString("TargetDevice.DeploymentSettings")),
            new(GetString("Unattend.Title"), Preparation.UnattendSummary),
            new(GetString("Preparation.ComputerName"), Preparation.EffectiveComputerName),
            new(GetString("Summary.TargetDisk"), SummaryTargetDiskText),
            new(GetString("TargetDevice.Firmware"), SummaryFirmwareText),
            DeploymentSummaryRowViewModel.Separator(),
            DeploymentSummaryRowViewModel.Section(GetString("TargetDevice.DeviceInventory")),
            new(GetString("TargetDevice.Manufacturer"), Preparation.HardwareManufacturerText),
            new(GetString("TargetDevice.Model"), Preparation.HardwareModelText),
            new(GetString("TargetDevice.Product"), Preparation.HardwareProductText),
            new(GetString("TargetDevice.Architecture"), Preparation.HardwareArchitectureText),
            new(GetString("TargetDevice.Tpm"), Preparation.HardwareTpmText),
            new(GetString("TargetDevice.PowerSource"), Preparation.HardwarePowerText),
            new(GetString("TargetDevice.FirmwareStatus"), Preparation.HardwareFirmwareText)
        ];
        if (Preparation.HasUnattendWarning)
            rows.Add(new(GetString("Summary.WarningDetails"), Preparation.UnattendWarning));
        if (Preparation.HasUnattendValidationError)
            rows.Add(new(GetString("Summary.Status"), Preparation.UnattendValidationMessage));
        else if (_launchFailureMessage is not null)
            rows.Add(new(GetString("Summary.Status"), _launchFailureMessage));
        return rows;
    }

    private IReadOnlyList<DeploymentSummaryRowViewModel> BuildDomainJoinSummaryRows()
    {
        if (!_wizardContext.DomainJoin.IsEnabled)
        {
            return [];
        }

        if (!IsDomainJoinApplicable())
        {
            return [new(GetString("Summary.Status"), GetString("DomainJoin.UnsupportedEdition"))];
        }

        if (DomainJoinStep.IsJoinDeclined)
        {
            return [];
        }

        return
        [
            new(GetString("DomainJoin.Domain"), DomainJoinStep.DomainName),
            new(GetString("DomainJoin.Destination"), DomainJoinStep.EffectiveOuDistinguishedName ?? GetString("DomainJoin.DefaultDestination"))
        ];
    }

    /// <summary>Gets whether the join applies to the selected image; an unsupported edition skips it.</summary>
    private bool IsDomainJoinApplicable()
    {
        return DomainJoinPreparationService.IsRequiredFor(_wizardContext.DomainJoin, SelectedOperatingSystem);
    }

    /// <summary>Gets whether the wizard shows the Domain Join step, which needs something for the technician to enter or choose.</summary>
    private bool HasDomainJoinStep()
    {
        return DomainJoinStep.HasInput && IsDomainJoinApplicable();
    }

    private IReadOnlyList<DeploymentSummaryRowViewModel> BuildDriverSummaryRows()
    {
        if (DriverPackSelection.EffectiveSelectionKind == DriverPackSelectionKind.None)
        {
            return [new(GetString("DriverPack.Source"), GetString("Common.None"))];
        }

        if (DriverPackSelection.EffectiveSelectionKind == DriverPackSelectionKind.MicrosoftUpdateCatalog)
        {
            return [new(GetString("DriverPack.Source"), GetString("DriverPack.MicrosoftUpdateCatalog"))];
        }

        if (DriverPackSelection.EffectiveSelectionKind == DriverPackSelectionKind.CustomDriverSet)
        {
            return
            [
                new(GetString("DriverPack.Source"), GetString("DriverPack.CustomDriverSet")),
                new(GetString("DriverPack.CustomDriverSetLabel"), DriverPackSelection.SelectedCustomDriverSet?.DisplayName ?? string.Empty)
            ];
        }

        string manufacturer = DriverPackSelection.SelectedDriverPackOption?.DisplayName ?? GetString("DriverPack.Oem");
        DriverPackCatalogItem? selectedPack = DriverPackSelection.ResolveEffectiveDriverPackSelection();
        if (selectedPack is null)
        {
            return
            [
                new(GetString("TargetDevice.Manufacturer"), manufacturer),
                new(GetString("Summary.Status"), Format("DriverPack.NoMatchingModelVersionFormat", manufacturer))
            ];
        }

        return
        [
            new(GetString("TargetDevice.Manufacturer"), selectedPack.Manufacturer),
            new(GetString("DriverPack.Model"), DriverPackSelection.SelectedDriverPackModel),
            new(GetString("DriverPack.Version"), DriverPackSelection.SelectedDriverPackVersion)
        ];
    }

    private bool HasAutopilotConfigurationStep()
    {
        return Preparation.IsAutopilotEnabled &&
               Preparation.AutopilotProvisioningMode != AutopilotProvisioningMode.InteractiveHardwareHashUpload;
    }

    private IReadOnlyList<DeploymentSummaryRowViewModel> BuildAutopilotSummaryRows()
    {
        if (!Preparation.IsAutopilotEnabled)
        {
            return [];
        }

        var rows = new List<DeploymentSummaryRowViewModel>
        {
            new(GetString("Summary.ProvisioningMethod"), Preparation.AutopilotModeText)
        };
        if (Preparation.IsJsonProfileMode)
        {
            rows.Add(new(GetString("Summary.AutopilotProfile"), SummaryAutopilotProfileText));
        }
        else if (Preparation.IsHardwareHashUploadMode)
        {
            rows.Add(new(
                GetString("Preparation.AutopilotHardwareHashUploadStatus"),
                Preparation.AutopilotHardwareHashUploadStatusText));
            rows.Add(new(GetString("Summary.AutopilotGroupTag"), SummaryAutopilotGroupTagText));
            rows.Add(DeploymentSummaryRowViewModel.Separator());
            rows.Add(DeploymentSummaryRowViewModel.Section(GetString("Autopilot.ConfigurationDetails")));
            rows.Add(new(
                GetString("Preparation.AutopilotHardwareHashTenantId"),
                Preparation.AutopilotHardwareHashTenantIdText));
            rows.Add(new(
                GetString("Preparation.AutopilotHardwareHashCertificateExpiration"),
                Preparation.AutopilotHardwareHashCertificateExpirationText));
            if (Preparation.IsHardwareHashUploadMessageVisible)
            {
                rows.Add(new(GetString("Summary.WarningDetails"), Preparation.AutopilotHardwareHashUploadMessage));
            }
        }

        return rows;
    }

    private IReadOnlyList<DeploymentSummaryRowViewModel> BuildWindowsCustomizationSummaryRows()
    {
        return WindowsCustomizationSummaryBuilder.Build(
            _wizardContext.Oobe,
            _wizardContext.AppxRemoval,
            _wizardContext.AiComponentRemoval,
            _wizardContext.WindowsOptionalFeatures,
            GetString,
            CurrentCulture,
            Preparation.UsesCustomUnattend);
    }

    private IReadOnlyList<DeploymentSummaryRowViewModel> BuildNetworkSummaryRows()
    {
        var roaming = _wizardContext.Network.ProfileRoaming;
        if (!roaming.IsAnyEnabled)
        {
            return [new(GetString("Summary.NetworkProfileRoaming"), GetString("Common.Disabled"))];
        }

        var rows = new List<DeploymentSummaryRowViewModel>();
        AddNetworkTransportRows(rows, "Summary.WiredDot1x", roaming.WiredDot1x.IsEnabled, roaming.WiredDot1x.IncludePrivateKeyMaterial);
        rows.Add(DeploymentSummaryRowViewModel.Separator());
        AddNetworkTransportRows(rows, "Summary.Wifi", roaming.Wifi.IsEnabled, roaming.Wifi.IncludePrivateKeyMaterial);
        return rows;
    }

    private void AddNetworkTransportRows(
        ICollection<DeploymentSummaryRowViewModel> rows,
        string sectionKey,
        bool isEnabled,
        bool includePrivateKeyMaterial)
    {
        rows.Add(DeploymentSummaryRowViewModel.Section(GetString(sectionKey)));
        rows.Add(new(GetString("Summary.NetworkProfileRoaming"), GetEnabledText(isEnabled)));
        if (isEnabled)
        {
            rows.Add(new(
                GetString("Summary.PrivateKeyMaterial"),
                GetString(includePrivateKeyMaterial ? "Summary.Included" : "Summary.NotIncluded")));
        }
    }

    private IReadOnlyList<DeploymentSummaryRowViewModel> BuildCompletionSummaryRows()
    {
        var rows = new List<DeploymentSummaryRowViewModel>
        {
            new(
                GetString("Summary.RestartBehavior"),
                GetString(_wizardContext.Completion.AutomaticRebootEnabled ? "Summary.Automatic" : "Summary.Manual"))
        };
        if (_wizardContext.Completion.AutomaticRebootEnabled)
        {
            rows.Add(new(
                GetString("Summary.RestartDelay"),
                Format("Summary.SecondsFormat", _wizardContext.Completion.AutomaticRebootDelaySeconds)));
        }

        return rows;
    }

    private string GetEnabledText(bool enabled)
    {
        return enabled ? GetString("Common.Enabled") : GetString("Common.Disabled");
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

        _wizardContext.ApplyStartupSnapshot(startupSnapshot);
        _bootMediaUpdateReason = startupSnapshot.BootMediaUpdateReason;
        RaiseBootMediaUpdatePropertiesChanged();
        Session.ConfigureRebootPolicy(DeploymentRebootPolicy.Create(_wizardContext.Completion));
        Session.SetComputerName(Preparation.EffectiveComputerName);
        Session.CompleteStartupInitialization();
        if (CustomImages.IsEnabled) _ = CustomImages.RefreshAsync();
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

        _startupCancellation.Cancel();
        _startupCancellation.Dispose();
        _wizardContext.StateChanged -= OnWizardContextStateChanged;
        Session.PropertyChanged -= OnSessionPropertyChanged;
        _deploymentOrchestrator.CompletionStarting -= OnDeploymentCompletionStarting;
        LocalizationService.LanguageChanged -= OnLocalizationLanguageChanged;
        _wizardContext.Dispose();
        Session.Dispose();
        _debugCredentialVerifier?.Dispose();
        _isDisposed = true;
        base.Dispose();
    }

    private void OnLocalizationLanguageChanged(object? sender, EventArgs e)
    {
        RunOnUiThread(() =>
        {
            RefreshSupportedCultures();
            RefreshKeyboardMenu();
            OnPropertyChanged(nameof(CurrentCulture));
            OnPropertyChanged(nameof(WindowTitle));
            OnPropertyChanged(nameof(VersionDisplay));
            OnPropertyChanged(nameof(BootMediaUpdateRecommendedText));
            OnPropertyChanged(nameof(BootMediaUpdateRecommendedToolTip));
            OperatingSystemCatalog.RefreshLocalizedMediaOptions();
            CustomImages.RefreshLocalization();
            DomainJoinStep.RefreshLocalizedText();
            OnPropertyChanged(nameof(OperatingSystemArchitectureDisplay));
            OnPropertyChanged(nameof(SummaryTargetDiskText));
            OnPropertyChanged(nameof(SummaryOperatingSystemText));
            OnPropertyChanged(nameof(SummaryFirmwareText));
            OnPropertyChanged(nameof(SummaryAutopilotProfileText));
            OnPropertyChanged(nameof(SummaryAutopilotGroupTagText));
            RefreshWizardSteps();
            RefreshSummaryCategories();
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
