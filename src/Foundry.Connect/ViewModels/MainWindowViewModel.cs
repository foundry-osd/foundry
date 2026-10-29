// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Foundry.Connect.Models;
using Foundry.Core.Models.Configuration;
using Foundry.Connect.Models.Network;
using Foundry.Core.Models.Runtime;
using Foundry.Wpf.Shell;
using Foundry.Connect.Lifetime;
using Foundry.Connect.Configuration;
using Foundry.Connect.Network;
using Foundry.Connect.Services.Logging;
using Foundry.Wpf.Theme;
using Foundry.Wpf.Dialogs;
using Foundry.Localization;
using Foundry.Utilities.Networking;
using Foundry.Utilities.Diagnostics;
using Foundry.Utilities.Globalization;
using Foundry.Utilities.Runtime;
using Foundry.Utilities.Storage;
using Foundry.Wpf.Localization;
using Foundry.Wpf.ViewModels;
using Microsoft.Win32;
using Microsoft.Extensions.Logging;
using ConnectThemeMode = Foundry.Wpf.Theme.ThemeMode;

namespace Foundry.Connect.ViewModels;

/// <summary>
/// Coordinates the Foundry.Connect shell, localized network status, Wi-Fi actions, and auto-continue behavior.
/// </summary>
public partial class MainWindowViewModel : LocalizedViewModelBase
{
    private const int ShellFolderPickerUnavailableHResult = unchecked((int)0x80040111);
    private const string PendingStatusGlyph = "\uE709";
    private const string ReadyStatusGlyph = "\uE73E";
    private const string EthernetOkGlyph = "\uE839";
    private const string EthernetWarningGlyph = "\uEB56";
    private const string EthernetErrorGlyph = "\uEB55";
    private const string WifiLowGlyph = "\uE872";
    private const string WifiMediumGlyph = "\uE873";
    private const string WifiHighGlyph = "\uE874";
    private const string WifiFullGlyph = "\uE701";

    private readonly IThemeService _themeService;
    private readonly IApplicationShellService _applicationShellService;
    private readonly IApplicationLifetimeService _applicationLifetimeService;
    private readonly IConnectConfigurationService _configurationService;
    private readonly FoundryConnectConfigurationDocument _configuration;
    private readonly IConnectionCoordinator _coordinator;
    private readonly INetworkMonitor _monitor;
    private readonly ISuccessfulExitService _successfulExit;
    private readonly LegacyNetworkStateAdapter _adapter;
    private readonly IKeyboardLayoutSwitcher _keyboardLayoutSwitcher;
    private readonly ILogger<MainWindowViewModel> _logger;
    private readonly Dispatcher _dispatcher;
    private readonly CancellationTokenSource _disposeCts = new();
    private readonly bool _isAutoCloseEnabled;

    private CancellationTokenSource? _countdownCts;
    private bool _isInitialized;
    private bool _isDisposed;
    private bool _isSyncingWifiNetworks;
    private bool _isProvisionedWifiCommandRunning;
    private NetworkStatusSnapshot? _lastNetworkReadySnapshot;
    private OperationOutcome? _lastAppliedOutcome;
    private string? _lastSelectedWifiNetworkSsidHex;
    private string? _connectedWifiSsid;
    private bool _isConnectedWifiProvisioned;

    [ObservableProperty]
    private NetworkLayoutMode layoutMode;

    [ObservableProperty]
    private NetworkLayoutMode? debugLayoutOverride;

    [ObservableProperty]
    private string primaryStatusGlyph = PendingStatusGlyph;

    [ObservableProperty]
    private string primaryStatusTitle = string.Empty;

    [ObservableProperty]
    private string primaryStatusDescription = string.Empty;

    [ObservableProperty]
    private bool isPrimaryStatusSuccessful;

    [ObservableProperty]
    private string currentConnectionChipText = string.Empty;

    [ObservableProperty]
    private string ethernetGlyph = EthernetErrorGlyph;

    [ObservableProperty]
    private string ethernetStatusText = string.Empty;

    [ObservableProperty]
    private string ethernetSecondaryStatusText = string.Empty;

    [ObservableProperty]
    private string ethernetAdapterName = string.Empty;

    [ObservableProperty]
    private string ethernetIpAddress = string.Empty;

    [ObservableProperty]
    private string ethernetGateway = string.Empty;

    [ObservableProperty]
    private bool hasInternetAccess;

    [ObservableProperty]
    private bool isEthernetConnected;

    [ObservableProperty]
    private bool isWifiRuntimeAvailable;

    [ObservableProperty]
    private bool hasWirelessAdapter;

    [ObservableProperty]
    private bool isCountdownActive;

    [ObservableProperty]
    private int countdownSecondsRemaining;

    [ObservableProperty]
    private DateTimeOffset? lastUpdatedAt;

    [ObservableProperty]
    private bool isNetworkActionInProgress;

    [ObservableProperty]
    private bool isSelectedWifiActionInProgress;

    [ObservableProperty]
    private string selectedWifiActionFeedbackText = string.Empty;

    [ObservableProperty]
    private bool isProvisionedWifiActionInProgress;

    [ObservableProperty]
    private string provisionedWifiActionFeedbackText = string.Empty;

    [ObservableProperty]
    private WifiNetworkItemViewModel? selectedWifiNetwork;

    [ObservableProperty]
    private string selectedWifiPassphrase = string.Empty;

    /// <summary>
    /// Initializes the main window view model and wires runtime services.
    /// </summary>
    /// <remarks>
    /// The view model only displays the coordinator's published state and forwards the technician's actions to it: it
    /// starts no automatic connection, logs no network operation and sends no telemetry (UI-03, NET-01, NET-07).
    /// </remarks>
    public MainWindowViewModel(
        IThemeService themeService,
        ILocalizationService localizationService,
        IApplicationShellService applicationShellService,
        IApplicationLifetimeService applicationLifetimeService,
        IConnectConfigurationService configurationService,
        FoundryConnectConfigurationDocument configuration,
        IConnectionCoordinator coordinator,
        INetworkMonitor monitor,
        ISuccessfulExitService successfulExit,
        IKeyboardLayoutSwitcher keyboardLayoutSwitcher,
        ILogger<MainWindowViewModel> logger)
        : base(localizationService)
    {
        _keyboardLayoutSwitcher = keyboardLayoutSwitcher;
        _themeService = themeService;
        _applicationShellService = applicationShellService;
        _applicationLifetimeService = applicationLifetimeService;
        _configurationService = configurationService;
        _configuration = configuration;
        _coordinator = coordinator ?? throw new ArgumentNullException(nameof(coordinator));
        _monitor = monitor ?? throw new ArgumentNullException(nameof(monitor));
        _successfulExit = successfulExit ?? throw new ArgumentNullException(nameof(successfulExit));
        _adapter = new LegacyNetworkStateAdapter(localizationService);
        _logger = logger;
        _dispatcher = Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;
        _isAutoCloseEnabled = !Debugger.IsAttached;
        LayoutMode = NetworkLayoutMode.EthernetOnly;
        LocalizationService.LanguageChanged += OnLanguageChanged;
        RefreshSupportedCultures();
        RefreshKeyboardMenu();
        ApplyLocalizedDefaults();
    }

    /// <summary>
    /// Gets the discovered Wi-Fi networks shown in the UI.
    /// </summary>
    public ObservableCollection<WifiNetworkItemViewModel> WifiNetworks { get; } = [];

    /// <summary>
    /// Gets the supported UI cultures.
    /// </summary>
    public ObservableCollection<SupportedCultureOption> SupportedCultures { get; } = [];

    /// <summary>
    /// Gets the keyboard menu entries: the active language's layouts, then the other languages.
    /// </summary>
    public ObservableCollection<KeyboardMenuEntry> KeyboardMenuEntries { get; } = [];

    /// <summary>
    /// Gets the keyboard menu header, including the active layout's short name when it is known.
    /// </summary>
    public string KeyboardMenuHeader { get; private set; } = string.Empty;

    /// <summary>
    /// Gets the current UI culture.
    /// </summary>
    public CultureInfo CurrentCulture => LocalizationService.CurrentCulture;

    /// <summary>
    /// Gets the current application theme mode.
    /// </summary>
    public ConnectThemeMode CurrentTheme => _themeService.CurrentTheme;

    public string VersionDisplay => Format("Common.VersionFormat", FoundryConnectApplicationInfo.Version);
    public bool IsBootMediaUpdateRecommended => _configurationService.IsBootMediaUpdateRecommended;
    public string BootMediaUpdateRecommendedText => GetString(
        _configurationService.BootMediaUpdateReason == BootMediaUpdateReason.NewerRelease
            ? "BootMedia.UpdateAvailable"
            : "BootMedia.UpdateRecommended");
    public string BootMediaUpdateRecommendedToolTip => GetString(
        _configurationService.BootMediaUpdateReason == BootMediaUpdateReason.UnknownAuthoringVersion
            ? "BootMedia.UnknownAuthoringVersionToolTip"
            : "BootMedia.UpdateRecommendedToolTip");

    /// <summary>
    /// Gets the configuration source text shown in the footer.
    /// </summary>
    public string ConfigurationSourceText => _configurationService.IsLoadedFromDisk && !string.IsNullOrWhiteSpace(_configurationService.ConfigurationPath)
        ? Format("Common.ConfigurationFromPathFormat", _configurationService.ConfigurationPath!)
        : GetString("Common.ConfigurationBuiltInDefaults");

    /// <summary>
    /// Gets the localized automatic refresh interval text.
    /// </summary>
    public string RefreshIntervalText => Format("Common.RefreshIntervalFormat", FoundryConnectApplicationInfo.DefaultRefreshIntervalSeconds);

    /// <summary>
    /// Gets the localized window title.
    /// </summary>
    public string WindowTitle => GetString("App.WindowTitle");

    /// <summary>
    /// Gets the auto-continue countdown text.
    /// </summary>
    public string AutoContinueText => Format("Status.AutoContinueFormat", CountdownSecondsRemaining);

    /// <summary>
    /// Gets the localized timestamp for the last completed network refresh.
    /// </summary>
    public string LastUpdatedText => LastUpdatedAt is null
        ? GetString("Common.LastUpdatePending")
        : Format("Common.LastUpdateFormat", LastUpdatedAt.Value.LocalDateTime);

    public bool HasWifiNetworks => WifiNetworks.Count > 0;

    /// <summary>
    /// Gets a value indicating whether debug-only menu actions are visible.
    /// </summary>
    public bool IsDebugMenuVisible => Debugger.IsAttached;

    /// <summary>
    /// Gets the layout mode after applying an optional debug override.
    /// </summary>
    public NetworkLayoutMode EffectiveLayoutMode => DebugLayoutOverride ?? LayoutMode;

    /// <summary>
    /// Gets a value indicating whether the configuration contains a provisioned Wi-Fi profile.
    /// </summary>
    public bool HasProvisionedWifiProfile => _configuration.Capabilities.WifiProvisioned && _configuration.Wifi.IsEnabled;

    public bool HasCurrentConnectionChip => !string.IsNullOrWhiteSpace(CurrentConnectionChipText);

    public bool HasEthernetSecondaryStatus => !string.IsNullOrWhiteSpace(EthernetSecondaryStatusText);

    /// <summary>
    /// Gets a value indicating whether automatic bootstrap can continue to deployment.
    /// </summary>
    public bool CanContinueBootstrap => HasInternetAccess && _applicationLifetimeService.RequestedExitCode is null;

    /// <summary>
    /// Gets a value indicating whether the current Wi-Fi connection matches the provisioned profile.
    /// </summary>
    public bool IsProvisionedWifiConnected => _isConnectedWifiProvisioned;

    /// <summary>
    /// Gets a value indicating whether the provisioned Wi-Fi profile can be connected now.
    /// </summary>
    public bool CanConnectConfiguredWifi => HasProvisionedWifiProfile &&
                                           IsWifiRuntimeAvailable &&
                                           HasWirelessAdapter &&
                                           !IsProvisionedWifiConnected &&
                                           !IsNetworkActionInProgress;

    /// <summary>
    /// Gets a value indicating whether the provisioned Wi-Fi profile can be disconnected now.
    /// </summary>
    public bool CanDisconnectConfiguredWifi => HasProvisionedWifiProfile &&
                                              IsProvisionedWifiConnected &&
                                              !IsNetworkActionInProgress;

    /// <summary>
    /// Gets the resolved display name of the provisioned Wi-Fi profile.
    /// </summary>
    public string ProvisionedWifiProfileName => ResolveProvisionedWifiProfileName();

    /// <summary>
    /// Gets the authentication label for the provisioned Wi-Fi profile.
    /// </summary>
    public string ProvisionedWifiAuthenticationText => BuildProvisionedWifiAuthenticationText();

    /// <summary>
    /// Gets the source hint for the provisioned Wi-Fi profile.
    /// </summary>
    public string ProvisionedWifiSourceHintText => BuildProvisionedWifiSourceHintText();

    /// <summary>
    /// Gets the current connection status for the provisioned Wi-Fi profile.
    /// </summary>
    public string ProvisionedWifiStatusText => BuildProvisionedWifiStatusText();

    /// <summary>
    /// Gets a value indicating whether provisioned Wi-Fi controls should be shown.
    /// </summary>
    public bool ShowProvisionedWifiContent => HasProvisionedWifiProfile &&
                                              string.IsNullOrWhiteSpace(BuildWifiUnavailableText());

    /// <summary>
    /// Gets the placeholder text shown when provisioned Wi-Fi cannot be displayed.
    /// </summary>
    public string ProvisionedWifiPlaceholderText => BuildProvisionedWifiPlaceholderText();

    public bool HasProvisionedWifiActionFeedback => !string.IsNullOrWhiteSpace(ProvisionedWifiActionFeedbackText);

    /// <summary>
    /// Gets the empty-state text for Wi-Fi discovery.
    /// </summary>
    public string WifiDiscoveryEmptyStateText => BuildWifiDiscoveryEmptyStateText();

    /// <summary>
    /// Gets a value indicating whether selected Wi-Fi action feedback should be shown.
    /// </summary>
    public bool HasSelectedWifiActionFeedback => !string.IsNullOrWhiteSpace(SelectedWifiActionFeedbackText);

    public bool CanConnectSelectedWifi => SelectedWifiNetwork is { CanDirectConnect: true } network &&
                                          !network.IsConnected &&
                                          (!network.RequiresPassphrase || !string.IsNullOrWhiteSpace(SelectedWifiPassphrase)) &&
                                          !IsNetworkActionInProgress;

    public bool CanDisconnectSelectedWifi => SelectedWifiNetwork is { IsConnected: true } && !IsNetworkActionInProgress;

    /// <summary>
    /// Subscribes to the published network state, applies the provisioning once, then starts the network monitor.
    /// </summary>
    /// <remarks>
    /// The coordinator and the monitor log their own operations and run their blocking work off the caller's thread.
    /// </remarks>
    public async Task InitializeAsync()
    {
        if (_isInitialized)
        {
            _logger.LogDebug("InitializeAsync was called after initialization had already completed.");
            return;
        }

        _isInitialized = true;
        _logger.LogInformation("Starting Foundry.Connect initialization.");
        _coordinator.StateChanged += OnCoordinatorStateChanged;

        try
        {
            ProvisioningOutcome outcome = await _coordinator.ApplyProvisioningAsync(_disposeCts.Token).ConfigureAwait(false);
            if (LegacyNetworkStateAdapter.ResolveProvisioningFeedbackKey(outcome) is string feedbackKey)
            {
                await RunOnUiAsync(() => ProvisionedWifiActionFeedbackText = GetString(feedbackKey)).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (_disposeCts.IsCancellationRequested)
        {
            // The window closed during provisioning.
            return;
        }

        _ = _monitor.RunAsync(_disposeCts.Token);
    }

    [RelayCommand]
    private void SetSystemTheme()
    {
        _themeService.SetTheme(ConnectThemeMode.System);
        OnPropertyChanged(nameof(CurrentTheme));
    }

    [RelayCommand]
    private void SetLightTheme()
    {
        _themeService.SetTheme(ConnectThemeMode.Light);
        OnPropertyChanged(nameof(CurrentTheme));
    }

    [RelayCommand]
    private void SetDarkTheme()
    {
        _themeService.SetTheme(ConnectThemeMode.Dark);
        OnPropertyChanged(nameof(CurrentTheme));
    }

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
    private void ShowAbout()
    {
        _applicationShellService.ShowAbout();
    }

    [RelayCommand]
    private void OpenLogFile()
    {
        string logFilePath = FoundryConnectLogging.CurrentLogFilePath;
        if (!File.Exists(logFilePath))
        {
            _logger.LogWarning("The Foundry Connect log file is unavailable. LogFilePath={LogFilePath}", logFilePath);
            return;
        }

        try
        {
            ProcessStartInfo startInfo = new("notepad.exe")
            {
                UseShellExecute = false
            };
            startInfo.ArgumentList.Add(logFilePath);
            _ = Process.Start(startInfo);
            _logger.LogInformation("Opened log file at {LogFilePath}.", logFilePath);
        }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException)
        {
            _logger.LogError(exception, "Failed to open log file. LogFilePath={LogFilePath}", logFilePath);
        }
    }

    [RelayCommand]
    private Task ExportDiagnosticsAsync()
    {
        return ExportDiagnosticsAsync(SupportBundlePrivacyMode.Sanitized);
    }

    [RelayCommand]
    private async Task ExportRawDiagnosticsAsync()
    {
        var confirmationDialog = new LocalizedMessageDialog(
            GetString("Diagnostics.ExportRawTitle"),
            GetString("Diagnostics.ExportRawWarning"),
            GetString("Diagnostics.ExportRawConfirm"),
            GetString("Common.Cancel"));
        if (confirmationDialog.ShowDialog() != true)
        {
            return;
        }

        await ExportDiagnosticsAsync(SupportBundlePrivacyMode.Raw);
    }

    private async Task ExportDiagnosticsAsync(SupportBundlePrivacyMode privacyMode)
    {
        try
        {
            string? destinationDirectoryPath = SelectExportDestination();
            if (destinationDirectoryPath is null)
            {
                return;
            }

            string? logDirectoryPath = Path.GetDirectoryName(FoundryConnectLogging.CurrentLogFilePath);
            string[] logFilePaths = string.IsNullOrWhiteSpace(logDirectoryPath) || !Directory.Exists(logDirectoryPath)
                ? []
                : Directory.GetFiles(logDirectoryPath, "Foundry*.log", SearchOption.TopDirectoryOnly);

            _logger.LogInformation(
                "Support bundle export started. PrivacyMode={PrivacyMode}, LogFileCount={LogFileCount}",
                privacyMode,
                logFilePaths.Length);

            SupportBundleResult result = await new SupportBundleExporter().ExportAsync(
                new SupportBundleRequest
                {
                    ApplicationName = "Foundry.Connect",
                    ApplicationVersion = FoundryConnectApplicationInfo.Version,
                    SessionId = DiagnosticSessionContext.CurrentSessionId,
                    DestinationDirectoryPath = destinationDirectoryPath,
                    LogFilePaths = logFilePaths,
                    PrivacyMode = privacyMode,
                    Summary = new Dictionary<string, string>
                    {
                        ["Architecture"] = RuntimeInformation.ProcessArchitecture.ToString(),
                        ["DeploymentMode"] = Environment.GetEnvironmentVariable("FOUNDRY_DEPLOYMENT_MODE") ?? "Unknown",
                        ["OperatingSystem"] = Environment.OSVersion.VersionString
                    }
                },
                _disposeCts.Token);

            _logger.LogInformation(
                "Support bundle export completed. PrivacyMode={PrivacyMode}, IncludedFileCount={IncludedFileCount}, OmittedFileCount={OmittedFileCount}",
                privacyMode,
                result.IncludedFiles.Count,
                result.OmittedFiles.Count);
            _ = new LocalizedMessageDialog(
                GetString("Diagnostics.ExportSucceededTitle"),
                Format("Diagnostics.ExportSucceededMessageFormat", result.ArchivePath),
                GetString("Common.Close")).ShowDialog();
        }
        catch (OperationCanceledException) when (_disposeCts.IsCancellationRequested)
        {
            _logger.LogInformation("Support bundle export canceled because the application is shutting down.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Support bundle export failed. PrivacyMode={PrivacyMode}", privacyMode);
            _ = new LocalizedMessageDialog(
                GetString("Diagnostics.ExportFailedTitle"),
                GetString("Diagnostics.ExportFailedMessage"),
                GetString("Common.Close")).ShowDialog();
        }
    }

    private string? SelectExportDestination()
    {
        if (WinPeRuntimeDetector.IsWinPeRuntime())
        {
            _logger.LogInformation("The shell folder picker was skipped because the application is running in WinPE.");
            return ResolveRequiredExternalExportDirectory();
        }

        try
        {
            string? externalExportDirectory = ResolveExternalExportDirectory();
            var picker = new OpenFolderDialog
            {
                Title = GetString("Diagnostics.ExportDestinationTitle"),
                InitialDirectory = ResolveSuggestedExportDirectory(externalExportDirectory)
            };
            return picker.ShowDialog() == true ? picker.FolderName : null;
        }
        catch (COMException ex) when (ex.HResult == ShellFolderPickerUnavailableHResult)
        {
            string? fallbackDirectoryPath = ResolveExternalExportDirectory();
            _logger.LogWarning(
                ex,
                "The shell folder picker is unavailable. Falling back to an external volume. HasFallbackDestination={HasFallbackDestination}",
                fallbackDirectoryPath is not null);
            return fallbackDirectoryPath
                   ?? throw new IOException("No ready external volume is available for the diagnostic export.");
        }
    }

    private string ResolveRequiredExternalExportDirectory()
    {
        return ResolveExternalExportDirectory()
               ?? throw new IOException("No ready external volume is available for the diagnostic export.");
    }

    private static string ResolveSuggestedExportDirectory(string? externalExportDirectory)
    {
        try
        {
            return externalExportDirectory
                   ?? Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        }
        catch
        {
            return Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        }
    }

    private string? ResolveExternalExportDirectory()
    {
        return SupportBundleDestinationPolicy.SelectExternalDestination(
            new WindowsVolumeDiscovery().GetVolumes());
    }

    [RelayCommand]
    private Task RefreshStatusAsync()
    {
        _monitor.RequestRefresh();
        return Task.CompletedTask;
    }

    [RelayCommand(CanExecute = nameof(CanContinueBootstrap))]
    private async Task ContinueBootstrapAsync()
    {
        await ContinueBootstrapSuccessfullyAsync(_disposeCts.Token).ConfigureAwait(false);
    }

    [RelayCommand(CanExecute = nameof(IsDebugMenuVisible))]
    private void ShowEthernetOnlyDebugLayout()
    {
        DebugLayoutOverride = NetworkLayoutMode.EthernetOnly;
    }

    [RelayCommand(CanExecute = nameof(IsDebugMenuVisible))]
    private void ShowEthernetWifiDebugLayout()
    {
        DebugLayoutOverride = NetworkLayoutMode.EthernetWifi;
    }

    [RelayCommand(CanExecute = nameof(IsDebugMenuVisible))]
    private void UseRuntimeDebugLayout()
    {
        DebugLayoutOverride = null;
    }

    [RelayCommand(CanExecute = nameof(CanConnectConfiguredWifi))]
    private async Task ConnectConfiguredWifiAsync()
    {
        await ExecuteProvisionedWifiActionAsync(
            () => _coordinator.ConnectProvisionedWifiAsync(_disposeCts.Token)).ConfigureAwait(false);
    }

    [RelayCommand(CanExecute = nameof(CanDisconnectConfiguredWifi))]
    private async Task DisconnectConfiguredWifiAsync()
    {
        await ExecuteProvisionedWifiActionAsync(
            () => _coordinator.DisconnectWifiAsync(_disposeCts.Token)).ConfigureAwait(false);
    }

    [RelayCommand(CanExecute = nameof(CanConnectSelectedWifi))]
    private async Task ConnectSelectedWifiAsync()
    {
        if (SelectedWifiNetwork is not { SsidHex: string ssidHex } network)
        {
            return;
        }

        string? passphrase = network.RequiresPassphrase ? SelectedWifiPassphrase : null;
        await ExecuteSelectedWifiActionAsync(
            () => _coordinator.ConnectWifiNetworkAsync(WifiSsid.FromHex(ssidHex), passphrase, _disposeCts.Token)).ConfigureAwait(false);
    }

    [RelayCommand(CanExecute = nameof(CanDisconnectSelectedWifi))]
    private async Task DisconnectSelectedWifiAsync()
    {
        if (SelectedWifiNetwork is not { IsConnected: true })
        {
            return;
        }

        await ExecuteSelectedWifiActionAsync(
            () => _coordinator.DisconnectWifiAsync(_disposeCts.Token)).ConfigureAwait(false);
    }

    /// <summary>
    /// Handles a user closing the window before Foundry.Connect has completed successfully.
    /// </summary>
    public void HandleWindowClosing()
    {
        // TryExit decides atomically, so a controlled exit that won the race keeps its exit code.
        if (_applicationLifetimeService.TryExit(ChildRuntimeExitCode.UserAborted))
        {
            _logger.LogInformation("Window close detected before a controlled exit. Returning user-aborted exit code.");
        }
    }

    // Raised on arbitrary threads, possibly with a stale argument: the current state is read again on the UI thread.
    private void OnCoordinatorStateChanged(object? sender, NetworkState state)
    {
        _ = RunOnUiAsync(() =>
        {
            if (!_isDisposed)
            {
                ApplyState(_coordinator.State);
            }
        });
    }

    private void ApplyState(NetworkState state)
    {
        ApplySnapshot(_adapter.ToSnapshot(state));
        IsProvisionedWifiActionInProgress = _isProvisionedWifiCommandRunning ||
                                            state.ActiveOperation == NetworkOperationKind.ConnectProvisionedWifi;
        OnPropertyChanged(nameof(ProvisionedWifiProfileName));

        // Automatic provisioned attempts keep showing their feedback in the provisioned panel, as before lot 2.
        if (!ReferenceEquals(state.LastOutcome, _lastAppliedOutcome))
        {
            _lastAppliedOutcome = state.LastOutcome;
            if (state.LastOutcome is { Operation: NetworkOperationKind.ConnectProvisionedWifi } outcome)
            {
                ProvisionedWifiActionFeedbackText = ResolveFeedbackText(outcome, fromProvisionedPanel: true);
            }
        }
    }

    private void ApplySnapshot(NetworkStatusSnapshot snapshot)
    {
        LayoutMode = snapshot.LayoutMode;
        HasInternetAccess = snapshot.HasInternetAccess;
        IsEthernetConnected = snapshot.IsEthernetConnected;
        IsWifiRuntimeAvailable = snapshot.IsWifiRuntimeAvailable;
        HasWirelessAdapter = snapshot.HasWirelessAdapter;
        _connectedWifiSsid = snapshot.ConnectedWifiSsid;
        _isConnectedWifiProvisioned = snapshot.IsConnectedWifiProvisioned;
        _lastNetworkReadySnapshot = snapshot.HasInternetAccess ? snapshot : null;

        EthernetGlyph = ResolveEthernetGlyph(snapshot);
        EthernetStatusText = snapshot.EthernetStatusText;
        EthernetSecondaryStatusText = snapshot.EthernetSecondaryStatusText;
        EthernetAdapterName = snapshot.EthernetAdapterName;
        EthernetIpAddress = snapshot.EthernetIpAddress;
        EthernetGateway = snapshot.EthernetGateway;
        LastUpdatedAt = DateTimeOffset.Now;
        OnPropertyChanged(nameof(LastUpdatedText));
        OnPropertyChanged(nameof(WifiDiscoveryEmptyStateText));
        OnPropertyChanged(nameof(ShowProvisionedWifiContent));
        OnPropertyChanged(nameof(ProvisionedWifiPlaceholderText));
        RefreshDerivedConnectionState(snapshot);

        SyncWifiNetworks(snapshot.WifiNetworks, snapshot.ConnectedWifiSsidHex);
        ApplyPrimaryStatus(snapshot.HasInternetAccess);
        UpdateCountdown(snapshot);
    }

    // The exit service confirms internet with a final probe; when it is not online the next state update cancels the
    // countdown.
    private async Task ContinueBootstrapSuccessfullyAsync(CancellationToken cancellationToken)
    {
        if (_lastNetworkReadySnapshot is null || _applicationLifetimeService.RequestedExitCode is not null)
        {
            return;
        }

        try
        {
            _ = await _successfulExit.TryCompleteAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The countdown was cancelled or the window closed.
        }
    }

    private void ApplyPrimaryStatus(bool hasInternetAccess)
    {
        if (hasInternetAccess)
        {
            IsPrimaryStatusSuccessful = true;
            PrimaryStatusGlyph = ReadyStatusGlyph;
            PrimaryStatusTitle = GetString("Status.NetworkReadyTitle");
            PrimaryStatusDescription = GetString("Status.NetworkReadyDescription");
            return;
        }

        IsPrimaryStatusSuccessful = false;
        PrimaryStatusGlyph = PendingStatusGlyph;
        PrimaryStatusTitle = GetString("Status.WaitingForNetworkTitle");
        PrimaryStatusDescription = GetString("Status.WaitingForNetworkDescription");
    }

    private void UpdateCountdown(NetworkStatusSnapshot snapshot)
    {
        if (snapshot.HasInternetAccess && _isAutoCloseEnabled)
        {
            if (IsCountdownActive || _applicationLifetimeService.RequestedExitCode is not null)
            {
                return;
            }

            StartCountdown();
            return;
        }

        CancelCountdown();
    }

    private void StartCountdown()
    {
        CancelCountdown();

        CountdownSecondsRemaining = FoundryConnectApplicationInfo.DefaultAutoContinueDelaySeconds;
        IsCountdownActive = true;
        OnPropertyChanged(nameof(AutoContinueText));

        _countdownCts = CancellationTokenSource.CreateLinkedTokenSource(_disposeCts.Token);
        // The first await is the one-second delay, so the loop leaves the UI thread at once (no Task.Run, spec 5.4).
        _ = RunCountdownAsync(_countdownCts.Token);
    }

    private async Task RunCountdownAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (CountdownSecondsRemaining > 0)
            {
                await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken).ConfigureAwait(false);
                await RunOnUiAsync(() =>
                {
                    CountdownSecondsRemaining = Math.Max(0, CountdownSecondsRemaining - 1);
                    OnPropertyChanged(nameof(AutoContinueText));
                }).ConfigureAwait(false);
            }

            if (_applicationLifetimeService.RequestedExitCode is null)
            {
                _logger.LogInformation("Internet validation remained stable through the countdown. Exiting successfully.");
                await ContinueBootstrapSuccessfullyAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // Ignore countdown cancellation.
        }
    }

    private void CancelCountdown()
    {
        _countdownCts?.Cancel();
        _countdownCts?.Dispose();
        _countdownCts = null;

        if (!IsCountdownActive && CountdownSecondsRemaining == 0)
        {
            return;
        }

        IsCountdownActive = false;
        CountdownSecondsRemaining = 0;
        OnPropertyChanged(nameof(AutoContinueText));
    }

    private void RefreshDerivedConnectionState(NetworkStatusSnapshot snapshot)
    {
        CurrentConnectionChipText = ResolveCurrentConnectionChipText(snapshot);
        OnPropertyChanged(nameof(IsProvisionedWifiConnected));
        OnPropertyChanged(nameof(CanConnectConfiguredWifi));
        OnPropertyChanged(nameof(CanDisconnectConfiguredWifi));
        OnPropertyChanged(nameof(ProvisionedWifiStatusText));
        ConnectConfiguredWifiCommand.NotifyCanExecuteChanged();
        DisconnectConfiguredWifiCommand.NotifyCanExecuteChanged();
        ContinueBootstrapCommand.NotifyCanExecuteChanged();
    }

    // Rows are keyed on the SSID bytes (hex, ordinal): two networks that differ only by case are two rows (D6).
    private void SyncWifiNetworks(IReadOnlyList<WifiNetworkSummary> networks, string? connectedWifiSsidHex)
    {
        string? selectedSsidHex = SelectedWifiNetwork?.SsidHex;
        string preservedPassphrase = SelectedWifiNetwork?.RequiresPassphrase == true
            ? SelectedWifiPassphrase
            : string.Empty;
        Dictionary<string, WifiNetworkItemViewModel> existingNetworks = new(StringComparer.Ordinal);
        foreach (WifiNetworkItemViewModel existing in WifiNetworks)
        {
            if (existing.SsidHex is string existingHex)
            {
                existingNetworks.TryAdd(existingHex, existing);
            }
        }

        List<WifiNetworkItemViewModel> orderedNetworks = new(networks.Count);

        _isSyncingWifiNetworks = true;

        try
        {
            foreach (WifiNetworkSummary network in networks)
            {
                string ssidHex = network.SsidHex ?? string.Empty;
                if (!existingNetworks.TryGetValue(ssidHex, out WifiNetworkItemViewModel? wifiNetwork))
                {
                    wifiNetwork = new WifiNetworkItemViewModel(network.Ssid);
                }

                wifiNetwork.Update(
                    network.Ssid,
                    TranslateDiscoveredWifiSecurity(network.Authentication),
                    network.SsidHex,
                    network.Authentication,
                    network.Encryption,
                    network.SignalStrengthPercent,
                    ResolveWifiGlyph(network.SignalStrengthPercent),
                    CanDirectConnect(network.Authentication),
                    RequiresPassphrase(network.Authentication),
                    string.Equals(network.SsidHex, connectedWifiSsidHex, StringComparison.Ordinal));
                orderedNetworks.Add(wifiNetwork);
            }

            SyncWifiNetworkCollection(orderedNetworks);

            SelectedWifiNetwork = orderedNetworks.FirstOrDefault(network =>
                string.Equals(network.SsidHex, selectedSsidHex, StringComparison.Ordinal))
                ?? orderedNetworks.FirstOrDefault(network =>
                    string.Equals(network.SsidHex, connectedWifiSsidHex, StringComparison.Ordinal));
        }
        finally
        {
            _isSyncingWifiNetworks = false;
        }

        if (SelectedWifiNetwork is { RequiresPassphrase: true, IsConnected: false })
        {
            SelectedWifiPassphrase = preservedPassphrase;
        }
        else
        {
            SelectedWifiPassphrase = string.Empty;
        }

        ConnectSelectedWifiCommand.NotifyCanExecuteChanged();
        DisconnectSelectedWifiCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(HasWifiNetworks));
        OnPropertyChanged(nameof(WifiDiscoveryEmptyStateText));
        OnPropertyChanged(nameof(CanConnectSelectedWifi));
        OnPropertyChanged(nameof(CanDisconnectSelectedWifi));
    }

    private void SyncWifiNetworkCollection(IReadOnlyList<WifiNetworkItemViewModel> orderedNetworks)
    {
        HashSet<WifiNetworkItemViewModel> orderedNetworkSet = [.. orderedNetworks];

        for (int index = WifiNetworks.Count - 1; index >= 0; index--)
        {
            if (!orderedNetworkSet.Contains(WifiNetworks[index]))
            {
                WifiNetworks.RemoveAt(index);
            }
        }

        for (int index = 0; index < orderedNetworks.Count; index++)
        {
            WifiNetworkItemViewModel network = orderedNetworks[index];

            if (index < WifiNetworks.Count && ReferenceEquals(WifiNetworks[index], network))
            {
                continue;
            }

            int existingIndex = WifiNetworks.IndexOf(network);
            if (existingIndex >= 0)
            {
                WifiNetworks.Move(existingIndex, index);
                continue;
            }

            WifiNetworks.Insert(index, network);
        }
    }

    private static string ResolveEthernetGlyph(NetworkStatusSnapshot snapshot)
    {
        if (snapshot.IsEthernetConnected && snapshot.HasEthernetIpv4)
        {
            return EthernetOkGlyph;
        }

        if (snapshot.HasEthernetAdapter)
        {
            return EthernetWarningGlyph;
        }

        return EthernetErrorGlyph;
    }

    private static string ResolveWifiGlyph(int signalStrengthPercent)
    {
        return signalStrengthPercent switch
        {
            <= 25 => WifiLowGlyph,
            <= 50 => WifiMediumGlyph,
            <= 75 => WifiHighGlyph,
            _ => WifiFullGlyph
        };
    }

    private Task RunOnUiAsync(Action action)
    {
        if (_dispatcher.CheckAccess())
        {
            action();
            return Task.CompletedTask;
        }

        return _dispatcher.InvokeAsync(action).Task;
    }

    public override void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        LocalizationService.LanguageChanged -= OnLanguageChanged;
        _coordinator.StateChanged -= OnCoordinatorStateChanged;

        // Cancelling stops the network monitor and any command still waiting for the coordinator.
        _disposeCts.Cancel();
        CancelCountdown();
        _countdownCts?.Dispose();
        _disposeCts.Dispose();
        base.Dispose();
        _isDisposed = true;
    }

    partial void OnIsNetworkActionInProgressChanged(bool value)
    {
        ConnectConfiguredWifiCommand.NotifyCanExecuteChanged();
        DisconnectConfiguredWifiCommand.NotifyCanExecuteChanged();
        ConnectSelectedWifiCommand.NotifyCanExecuteChanged();
        DisconnectSelectedWifiCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(CanConnectConfiguredWifi));
        OnPropertyChanged(nameof(CanDisconnectConfiguredWifi));
        OnPropertyChanged(nameof(CanConnectSelectedWifi));
        OnPropertyChanged(nameof(CanDisconnectSelectedWifi));
    }

    partial void OnSelectedWifiNetworkChanged(WifiNetworkItemViewModel? value)
    {
        bool hasChangedSelection = !string.Equals(
            _lastSelectedWifiNetworkSsidHex,
            value?.SsidHex,
            StringComparison.Ordinal);

        if (!_isSyncingWifiNetworks &&
            (value is null || !value.RequiresPassphrase || value.IsConnected || hasChangedSelection))
        {
            SelectedWifiPassphrase = string.Empty;
        }

        SelectedWifiActionFeedbackText = string.Empty;

        _lastSelectedWifiNetworkSsidHex = value?.SsidHex;

        ConnectSelectedWifiCommand.NotifyCanExecuteChanged();
        DisconnectSelectedWifiCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(CanConnectSelectedWifi));
        OnPropertyChanged(nameof(CanDisconnectSelectedWifi));
    }

    partial void OnSelectedWifiPassphraseChanged(string value)
    {
        SelectedWifiActionFeedbackText = string.Empty;
        ConnectSelectedWifiCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(CanConnectSelectedWifi));
    }

    partial void OnSelectedWifiActionFeedbackTextChanged(string value)
    {
        OnPropertyChanged(nameof(HasSelectedWifiActionFeedback));
    }

    partial void OnProvisionedWifiActionFeedbackTextChanged(string value)
    {
        OnPropertyChanged(nameof(HasProvisionedWifiActionFeedback));
    }

    partial void OnCurrentConnectionChipTextChanged(string value)
    {
        OnPropertyChanged(nameof(HasCurrentConnectionChip));
    }

    partial void OnEthernetSecondaryStatusTextChanged(string value)
    {
        OnPropertyChanged(nameof(HasEthernetSecondaryStatus));
    }

    partial void OnHasInternetAccessChanged(bool value)
    {
        ContinueBootstrapCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(CanContinueBootstrap));
    }

    partial void OnLayoutModeChanged(NetworkLayoutMode value)
    {
        OnPropertyChanged(nameof(EffectiveLayoutMode));
    }

    partial void OnDebugLayoutOverrideChanged(NetworkLayoutMode? value)
    {
        OnPropertyChanged(nameof(EffectiveLayoutMode));
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        RunOnUiThread(() =>
        {
            RefreshSupportedCultures();
            RefreshKeyboardMenu();
            ApplyPrimaryStatus(HasInternetAccess);
            CurrentConnectionChipText = BuildCurrentConnectionChipText(IsEthernetConnected);
            OnPropertyChanged(nameof(CurrentCulture));
            OnPropertyChanged(nameof(VersionDisplay));
            OnPropertyChanged(nameof(BootMediaUpdateRecommendedText));
            OnPropertyChanged(nameof(BootMediaUpdateRecommendedToolTip));
            OnPropertyChanged(nameof(ConfigurationSourceText));
            OnPropertyChanged(nameof(RefreshIntervalText));
            OnPropertyChanged(nameof(WindowTitle));
            OnPropertyChanged(nameof(AutoContinueText));
            OnPropertyChanged(nameof(LastUpdatedText));
            OnPropertyChanged(nameof(ProvisionedWifiProfileName));
            OnPropertyChanged(nameof(ProvisionedWifiAuthenticationText));
            OnPropertyChanged(nameof(ProvisionedWifiSourceHintText));
            OnPropertyChanged(nameof(ProvisionedWifiStatusText));
            OnPropertyChanged(nameof(ProvisionedWifiPlaceholderText));
            OnPropertyChanged(nameof(WifiDiscoveryEmptyStateText));

            // Texts are rebuilt from the typed state; no network refresh is needed (UI-05).
            if (_isInitialized && !_isDisposed)
            {
                ApplyState(_coordinator.State);
            }
        });
    }

    private void ApplyLocalizedDefaults()
    {
        PrimaryStatusTitle = GetString("Status.WaitingForNetworkTitle");
        PrimaryStatusDescription = GetString("Status.WaitingForNetworkDescription");
        EthernetStatusText = GetString("Ethernet.NoAdapterDetected");
        EthernetAdapterName = GetString("Common.Unavailable");
        EthernetIpAddress = GetString("Common.Unavailable");
        EthernetGateway = GetString("Common.Unavailable");
    }

    // The coordinator logs the operation and requests a refresh; the view model only tracks the panel's progress and
    // feedback.
    private async Task ExecuteProvisionedWifiActionAsync(Func<Task<OperationOutcome>> action)
    {
        if (_disposeCts.IsCancellationRequested)
        {
            return;
        }

        await RunOnUiAsync(() =>
        {
            _isProvisionedWifiCommandRunning = true;
            IsNetworkActionInProgress = true;
            IsProvisionedWifiActionInProgress = true;
            ProvisionedWifiActionFeedbackText = string.Empty;
        }).ConfigureAwait(false);

        try
        {
            OperationOutcome outcome = await action().ConfigureAwait(false);
            await RunOnUiAsync(() => ProvisionedWifiActionFeedbackText = ResolveFeedbackText(outcome, fromProvisionedPanel: true)).ConfigureAwait(false);
        }
        finally
        {
            await RunOnUiAsync(() =>
            {
                _isProvisionedWifiCommandRunning = false;
                IsProvisionedWifiActionInProgress = _coordinator.State.ActiveOperation == NetworkOperationKind.ConnectProvisionedWifi;
                IsNetworkActionInProgress = false;
            }).ConfigureAwait(false);
        }
    }

    private async Task ExecuteSelectedWifiActionAsync(Func<Task<OperationOutcome>> action)
    {
        if (_disposeCts.IsCancellationRequested)
        {
            return;
        }

        await RunOnUiAsync(() =>
        {
            IsNetworkActionInProgress = true;
            IsSelectedWifiActionInProgress = true;
            SelectedWifiActionFeedbackText = string.Empty;
        }).ConfigureAwait(false);

        try
        {
            OperationOutcome outcome = await action().ConfigureAwait(false);
            await RunOnUiAsync(() => SelectedWifiActionFeedbackText = ResolveFeedbackText(outcome, fromProvisionedPanel: false)).ConfigureAwait(false);
        }
        finally
        {
            await RunOnUiAsync(() =>
            {
                IsSelectedWifiActionInProgress = false;
                IsNetworkActionInProgress = false;
            }).ConfigureAwait(false);
        }
    }

    private string ResolveFeedbackText(OperationOutcome outcome, bool fromProvisionedPanel)
    {
        return LegacyNetworkStateAdapter.ResolveFeedbackKey(outcome, fromProvisionedPanel) is string key
            ? GetString(key)
            : string.Empty;
    }


    /// <summary>
    /// Represents one Wi-Fi network row in the discovery list.
    /// </summary>
    public sealed class WifiNetworkItemViewModel : ObservableObject
    {
        private string _displaySsid = string.Empty;
        private string _authentication = string.Empty;
        private string _displayAuthentication = string.Empty;
        private string? _ssidHex;
        private string _encryption = string.Empty;
        private int _signalStrengthPercent;
        private string _signalGlyph = string.Empty;
        private bool _canDirectConnect;
        private bool _requiresPassphrase;
        private bool _isConnected;

        /// <summary>
        /// Initializes a Wi-Fi network row with its stable SSID key.
        /// </summary>
        /// <param name="ssid">The SSID used to identify the row.</param>
        public WifiNetworkItemViewModel(string ssid)
        {
            Ssid = ssid;
        }

        public string Ssid { get; }

        public string DisplaySsid
        {
            get => _displaySsid;
            private set => SetProperty(ref _displaySsid, value);
        }

        public string? SsidHex
        {
            get => _ssidHex;
            private set => SetProperty(ref _ssidHex, value);
        }

        public string Authentication
        {
            get => _authentication;
            private set => SetProperty(ref _authentication, value);
        }

        public string DisplayAuthentication
        {
            get => _displayAuthentication;
            private set => SetProperty(ref _displayAuthentication, value);
        }

        public string Encryption
        {
            get => _encryption;
            private set => SetProperty(ref _encryption, value);
        }

        public int SignalStrengthPercent
        {
            get => _signalStrengthPercent;
            private set => SetProperty(ref _signalStrengthPercent, value);
        }

        public string SignalGlyph
        {
            get => _signalGlyph;
            private set => SetProperty(ref _signalGlyph, value);
        }

        public bool CanDirectConnect
        {
            get => _canDirectConnect;
            private set => SetProperty(ref _canDirectConnect, value);
        }

        public bool RequiresPassphrase
        {
            get => _requiresPassphrase;
            private set => SetProperty(ref _requiresPassphrase, value);
        }

        public bool IsConnected
        {
            get => _isConnected;
            private set => SetProperty(ref _isConnected, value);
        }

        public void Update(
            string displaySsid,
            string displayAuthentication,
            string? ssidHex,
            string authentication,
            string encryption,
            int signalStrengthPercent,
            string signalGlyph,
            bool canDirectConnect,
            bool requiresPassphrase,
            bool isConnected)
        {
            DisplaySsid = displaySsid;
            DisplayAuthentication = displayAuthentication;
            SsidHex = ssidHex;
            Authentication = authentication;
            Encryption = encryption;
            SignalStrengthPercent = signalStrengthPercent;
            SignalGlyph = signalGlyph;
            CanDirectConnect = canDirectConnect;
            RequiresPassphrase = requiresPassphrase;
            IsConnected = isConnected;
        }
    }

    private string BuildWifiDiscoveryEmptyStateText()
    {
        return BuildWifiUnavailableText() ?? GetString("Wifi.NoNetworksVisible");
    }

    private string BuildProvisionedWifiPlaceholderText()
    {
        if (!HasProvisionedWifiProfile)
        {
            return GetString("Wifi.NoProvisionedProfile");
        }

        return BuildWifiUnavailableText() ?? string.Empty;
    }

    private string? BuildWifiUnavailableText()
    {
        if (!IsWifiRuntimeAvailable)
        {
            return GetString("Wifi.UnavailableAtRuntime");
        }

        if (!HasWirelessAdapter)
        {
            return GetString("Wifi.NoAdapterDetected");
        }

        return null;
    }

    private string ResolveProvisionedWifiProfileName()
    {
        if (!HasProvisionedWifiProfile)
        {
            return GetString("Common.Unavailable");
        }

        if (_configuration.Wifi.HasEnterpriseProfile)
        {
            // The provisioning step read the template once; the name is known after it ran.
            string? profileName = _coordinator.State.Wifi.Provisioned.ProfileName;
            return string.IsNullOrWhiteSpace(profileName)
                ? GetString("Wifi.EnterpriseProfile")
                : profileName;
        }

        return string.IsNullOrWhiteSpace(_configuration.Wifi.Ssid)
            ? GetString("Wifi.UnnamedProvisionedProfile")
            : _configuration.Wifi.Ssid.Trim();
    }

    private string BuildProvisionedWifiAuthenticationText()
    {
        if (_configuration.Wifi.HasEnterpriseProfile)
        {
            string? profilePath = ProvisionedWifiProfileResolver.ResolveAssetPath(
                _configuration.Wifi.EnterpriseProfileTemplatePath,
                _configurationService.ConfigurationPath);
            string? profileAuthentication = WlanProfileReader.TryReadAuthentication(profilePath);
            return ResolveProvisionedEnterpriseSecurityDisplayText(
                profileAuthentication ?? _configuration.Wifi.SecurityType,
                _configuration.Wifi.EnterpriseAuthenticationMode);
        }

        return ResolveProvisionedPersonalSecurityDisplayText(_configuration.Wifi.SecurityType);
    }

    private string BuildProvisionedWifiSourceHintText()
    {
        string sourceText = _configuration.Wifi.HasEnterpriseProfile
            ? GetString("Wifi.EnterpriseProfile")
            : GetString("Wifi.BootImageSettings");

        if (_configuration.Wifi.RequiresCertificate || !string.IsNullOrWhiteSpace(_configuration.Wifi.CertificatePath))
        {
            return Format("Wifi.SourceWithCertificateFormat", sourceText);
        }

        return sourceText;
    }

    private string TranslateDiscoveredWifiSecurity(string authentication)
    {
        return authentication switch
        {
            "Open" => GetString("Wifi.SecurityOpen"),
            "WEP" => GetString("Wifi.SecurityWep"),
            "WPA-Personal" => GetString("Wifi.SecurityWpaPersonal"),
            "WPA-None" => GetString("Wifi.SecurityWpaNone"),
            "WPA2-Personal" => GetString("Wifi.SecurityWpa2Personal"),
            "WPA3-Personal" => GetString("Wifi.SecurityWpa3Personal"),
            "WPA-Enterprise" => GetString("Wifi.SecurityWpaEnterprise"),
            "WPA2-Enterprise" => GetString("Wifi.SecurityWpa2Enterprise"),
            "WPA3-Enterprise" => GetString("Wifi.SecurityWpa3Enterprise"),
            "Vendor-specific" => GetString("Wifi.SecurityVendorSpecific"),
            "OWE" => "OWE",
            _ => authentication
        };
    }

    private string ResolveProvisionedPersonalSecurityDisplayText(string? securityType)
    {
        if (string.IsNullOrWhiteSpace(securityType))
        {
            return GetString("Wifi.ConfiguredProfile");
        }

        return securityType.Trim() switch
        {
            "OWE" => "OWE",
            "WPA2/WPA3-Personal" => GetString("Wifi.SecurityWpa2Wpa3Personal"),
            "WPA2-Personal" => GetString("Wifi.SecurityWpa2Personal"),
            "WPA3-Personal" => GetString("Wifi.SecurityWpa3Personal"),
            "Personal" => GetString("Wifi.SecurityPersonal"),
            _ => securityType.Trim()
        };
    }

    private string ResolveProvisionedEnterpriseSecurityDisplayText(
        string? securityType,
        NetworkAuthenticationMode authenticationMode)
    {
        string baseSecurityText = securityType?.Trim() switch
        {
            "WPA3ENT" => GetString("Wifi.SecurityWpa3Enterprise"),
            "WPA3ENT192" or "WPA3" => GetString("Wifi.SecurityWpa3Enterprise192"),
            "WPA2/WPA3-Enterprise" => GetString("Wifi.SecurityWpa2Wpa3Enterprise"),
            "WPA2-Enterprise" => GetString("Wifi.SecurityWpa2Enterprise"),
            "WPA3-Enterprise" => GetString("Wifi.SecurityWpa3Enterprise"),
            "Enterprise" => GetString("Wifi.SecurityEnterprise"),
            _ => GetString("Wifi.SecurityWpa2Wpa3Enterprise")
        };

        return authenticationMode switch
        {
            NetworkAuthenticationMode.MachineOnly => Format("Wifi.SecurityMachineFormat", baseSecurityText),
            NetworkAuthenticationMode.MachineOrUser => Format("Wifi.SecurityMachineOrUserFormat", baseSecurityText),
            _ => baseSecurityText
        };
    }

    private string BuildProvisionedWifiStatusText()
    {
        if (!HasProvisionedWifiProfile)
        {
            return string.Empty;
        }

        if (IsProvisionedWifiConnected)
        {
            return GetString("Common.Connected");
        }

        if (!IsWifiRuntimeAvailable)
        {
            return GetString("Wifi.StatusUnavailable");
        }

        if (!HasWirelessAdapter)
        {
            return GetString("Wifi.StatusNoAdapter");
        }

        if (!string.IsNullOrWhiteSpace(_connectedWifiSsid))
        {
            return GetString("Wifi.StatusAnotherNetworkActive");
        }

        return GetString("Wifi.StatusReadyToConnect");
    }

    private string ResolveCurrentConnectionChipText(NetworkStatusSnapshot snapshot)
    {
        return BuildCurrentConnectionChipText(snapshot.IsEthernetConnected);
    }

    private string BuildCurrentConnectionChipText(bool isEthernetConnected)
    {
        if (isEthernetConnected)
        {
            return GetString("Wifi.ConnectionChipEthernet");
        }

        if (string.IsNullOrWhiteSpace(_connectedWifiSsid))
        {
            return string.Empty;
        }

        return IsProvisionedWifiConnected
            ? Format("Wifi.ConnectionChipProvisionedFormat", _connectedWifiSsid)
            : Format("Wifi.ConnectionChipFormat", _connectedWifiSsid);
    }

    private static bool CanDirectConnect(string authentication)
    {
        return ClassifyDiscoveredWifi(authentication) is DiscoveredWifiType.Open or DiscoveredWifiType.Owe or DiscoveredWifiType.Personal;
    }

    private static bool RequiresPassphrase(string authentication)
    {
        return ClassifyDiscoveredWifi(authentication) == DiscoveredWifiType.Personal;
    }

    private static DiscoveredWifiType ClassifyDiscoveredWifi(string authentication)
    {
        if (authentication.Contains("open", StringComparison.OrdinalIgnoreCase))
        {
            return DiscoveredWifiType.Open;
        }

        if (authentication.Contains("owe", StringComparison.OrdinalIgnoreCase))
        {
            return DiscoveredWifiType.Owe;
        }

        if (authentication.Contains("personal", StringComparison.OrdinalIgnoreCase) ||
            authentication.Contains("psk", StringComparison.OrdinalIgnoreCase))
        {
            return DiscoveredWifiType.Personal;
        }

        return DiscoveredWifiType.Enterprise;
    }

    private enum DiscoveredWifiType
    {
        Open,
        Owe,
        Personal,
        Enterprise
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
