using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Foundry.Connect.Models;
using Foundry.Connect.Models.Configuration;
using Foundry.Connect.Models.Network;
using Foundry.Connect.Services.ApplicationShell;
using Foundry.Connect.Services.ApplicationLifetime;
using Foundry.Connect.Services.Configuration;
using Foundry.Connect.Services.Localization;
using Foundry.Connect.Services.Network;
using Foundry.Connect.Services.Theme;
using Microsoft.Extensions.Logging;
using ConnectThemeMode = Foundry.Connect.Services.Theme.ThemeMode;

namespace Foundry.Connect.ViewModels;

/// <summary>
/// Coordinates the Foundry.Connect shell, localized network status, Wi-Fi actions, and auto-continue behavior.
/// </summary>
public partial class MainWindowViewModel : LocalizedViewModelBase
{
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
    private readonly FoundryConnectConfiguration _configuration;
    private readonly INetworkBootstrapService _networkBootstrapService;
    private readonly INetworkStatusService _networkStatusService;
    private readonly ILogger<MainWindowViewModel> _logger;
    private readonly Dispatcher _dispatcher;
    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private readonly CancellationTokenSource _disposeCts = new();
    private readonly bool _isAutoCloseEnabled;

    private CancellationTokenSource? _countdownCts;
    private bool _isInitialized;
    private bool _isDisposed;
    private bool _isSyncingWifiNetworks;
    private string? _lastSelectedWifiNetworkSsid;
    private DateTimeOffset? _lastConfiguredWifiConnectAttemptAt;
    private string? _connectedWifiSsid;

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
    public MainWindowViewModel(
        IThemeService themeService,
        ILocalizationService localizationService,
        IApplicationShellService applicationShellService,
        IApplicationLifetimeService applicationLifetimeService,
        IConnectConfigurationService configurationService,
        FoundryConnectConfiguration configuration,
        INetworkBootstrapService networkBootstrapService,
        INetworkStatusService networkStatusService,
        ILogger<MainWindowViewModel> logger)
        : base(localizationService)
    {
        _themeService = themeService;
        _applicationShellService = applicationShellService;
        _applicationLifetimeService = applicationLifetimeService;
        _configurationService = configurationService;
        _configuration = configuration;
        _networkBootstrapService = networkBootstrapService;
        _networkStatusService = networkStatusService;
        _logger = logger;
        _dispatcher = Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;
        _isAutoCloseEnabled = !Debugger.IsAttached;
        LayoutMode = NetworkLayoutMode.EthernetOnly;
        LocalizationService.LanguageChanged += OnLanguageChanged;
        RefreshSupportedCultures();
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
    /// Gets the current UI culture.
    /// </summary>
    public CultureInfo CurrentCulture => LocalizationService.CurrentCulture;

    /// <summary>
    /// Gets the current application theme mode.
    /// </summary>
    public ConnectThemeMode CurrentTheme => _themeService.CurrentTheme;

    public string VersionDisplay => Format("Common.VersionFormat", FoundryConnectApplicationInfo.Version);

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
    public bool CanContinueBootstrap => HasInternetAccess && !_applicationLifetimeService.IsExitRequested;

    /// <summary>
    /// Gets a value indicating whether the current Wi-Fi connection matches the provisioned profile.
    /// </summary>
    public bool IsProvisionedWifiConnected => IsProvisionedWifiConnection(_connectedWifiSsid);

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
    /// Applies provisioned network settings, refreshes the first snapshot, and starts background monitoring.
    /// </summary>
    public async Task InitializeAsync()
    {
        if (_isInitialized)
        {
            _logger.LogDebug("InitializeAsync was called after initialization had already completed.");
            return;
        }

        _isInitialized = true;
        _logger.LogInformation("Starting Foundry.Connect initialization.");

        _logger.LogInformation("Applying provisioned network settings.");
        await ApplyProvisionedSettingsAsync(_disposeCts.Token).ConfigureAwait(false);
        _logger.LogInformation("Provisioned network settings step completed.");

        _logger.LogInformation("Refreshing initial network snapshot.");
        await RefreshCoreAsync(_disposeCts.Token).ConfigureAwait(false);
        _logger.LogInformation("Initial network snapshot refresh completed.");

        _ = MonitorAsync(_disposeCts.Token);
        _logger.LogInformation("Background network monitoring started.");
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

    private void RefreshSupportedCultures()
    {
        SupportedCultures.Clear();
        foreach (SupportedCultureOption option in SupportedCultureCatalog.CreateOptions(CurrentCulture, key => Strings[key]))
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
    private Task RefreshStatusAsync()
    {
        return RefreshCoreAsync(_disposeCts.Token);
    }

    [RelayCommand(CanExecute = nameof(CanContinueBootstrap))]
    private void ContinueBootstrap()
    {
        if (_applicationLifetimeService.IsExitRequested)
        {
            return;
        }

        _applicationLifetimeService.Exit(FoundryConnectExitCode.Success);
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
            () => _networkBootstrapService.ConnectConfiguredWifiAsync(_disposeCts.Token),
            BuildProvisionedWifiConnectFeedback,
            refreshAfterAction: true).ConfigureAwait(false);
    }

    [RelayCommand(CanExecute = nameof(CanDisconnectConfiguredWifi))]
    private async Task DisconnectConfiguredWifiAsync()
    {
        await ExecuteProvisionedWifiActionAsync(
            () => _networkBootstrapService.DisconnectWifiAsync(_disposeCts.Token),
            BuildProvisionedWifiDisconnectFeedback,
            refreshAfterAction: true).ConfigureAwait(false);
    }

    [RelayCommand(CanExecute = nameof(CanConnectSelectedWifi))]
    private async Task ConnectSelectedWifiAsync()
    {
        if (SelectedWifiNetwork is null)
        {
            return;
        }

        await ExecuteSelectedWifiActionAsync(
            () => _networkBootstrapService.ConnectWifiNetworkAsync(
                SelectedWifiNetwork.Ssid,
                SelectedWifiNetwork.SsidHex,
                SelectedWifiNetwork.Authentication,
                SelectedWifiNetwork.RequiresPassphrase ? SelectedWifiPassphrase : null,
                _disposeCts.Token),
            BuildSelectedWifiConnectFeedback,
            refreshAfterAction: true).ConfigureAwait(false);
    }

    [RelayCommand(CanExecute = nameof(CanDisconnectSelectedWifi))]
    private async Task DisconnectSelectedWifiAsync()
    {
        if (SelectedWifiNetwork is not { IsConnected: true })
        {
            return;
        }

        await ExecuteSelectedWifiActionAsync(
            () => _networkBootstrapService.DisconnectWifiAsync(_disposeCts.Token),
            BuildSelectedWifiDisconnectFeedback,
            refreshAfterAction: true).ConfigureAwait(false);
    }

    /// <summary>
    /// Handles a user closing the window before Foundry.Connect has completed successfully.
    /// </summary>
    public void HandleWindowClosing()
    {
        if (!_applicationLifetimeService.IsExitRequested)
        {
            _logger.LogInformation("Window close detected before a controlled exit. Returning user-aborted exit code.");
            _applicationLifetimeService.Exit(FoundryConnectExitCode.UserAborted);
        }
    }

    private async Task MonitorAsync(CancellationToken cancellationToken)
    {
        using PeriodicTimer timer = new(TimeSpan.FromSeconds(FoundryConnectApplicationInfo.DefaultRefreshIntervalSeconds));

        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
            {
                await RefreshCoreAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // Ignore disposal-driven shutdown.
        }
    }

    private async Task RefreshCoreAsync(CancellationToken cancellationToken)
    {
        await _refreshGate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            NetworkStatusSnapshot snapshot = await _networkStatusService.GetSnapshotAsync(cancellationToken).ConfigureAwait(false);
            _logger.LogDebug(
                "Network snapshot refreshed. LayoutMode={LayoutMode}, HasInternetAccess={HasInternetAccess}, IsEthernetConnected={IsEthernetConnected}, IsWifiRuntimeAvailable={IsWifiRuntimeAvailable}, HasWirelessAdapter={HasWirelessAdapter}, WifiNetworkCount={WifiNetworkCount}.",
                snapshot.LayoutMode,
                snapshot.HasInternetAccess,
                snapshot.IsEthernetConnected,
                snapshot.IsWifiRuntimeAvailable,
                snapshot.HasWirelessAdapter,
                snapshot.WifiNetworks.Count);
            await RunOnUiAsync(() => ApplySnapshot(snapshot)).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Ignore shutdown-driven refresh cancellation.
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to refresh network status.");
            await RunOnUiAsync(() =>
            {
                IsPrimaryStatusSuccessful = false;
                PrimaryStatusGlyph = PendingStatusGlyph;
                PrimaryStatusTitle = GetString("Status.NetworkRefreshFailedTitle");
                PrimaryStatusDescription = ex.Message;
            }).ConfigureAwait(false);
        }
        finally
        {
            _refreshGate.Release();
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

        SyncWifiNetworks(snapshot.WifiNetworks, snapshot.ConnectedWifiSsid);
        ApplyPrimaryStatus(snapshot.HasInternetAccess);
        UpdateCountdown(snapshot);

        if (!snapshot.HasInternetAccess &&
            snapshot.LayoutMode == NetworkLayoutMode.EthernetWifi &&
            snapshot.IsWifiRuntimeAvailable &&
            _configuration.Wifi.IsEnabled &&
            !IsNetworkActionInProgress &&
            !IsProvisionedWifiConnected &&
            ShouldRetryConfiguredWifiConnect())
        {
            _lastConfiguredWifiConnectAttemptAt = DateTimeOffset.UtcNow;
            // Auto-retry the provisioned Wi-Fi profile only when Ethernet has not already satisfied internet access.
            _ = Task.Run(async () =>
            {
                await ExecuteProvisionedWifiActionAsync(
                    () => _networkBootstrapService.ConnectConfiguredWifiAsync(_disposeCts.Token),
                    BuildProvisionedWifiConnectFeedback,
                    refreshAfterAction: true).ConfigureAwait(false);
            });
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
            if (IsCountdownActive || _applicationLifetimeService.IsExitRequested)
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
        _ = Task.Run(() => RunCountdownAsync(_countdownCts.Token), _countdownCts.Token);
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

            if (!_applicationLifetimeService.IsExitRequested)
            {
                _logger.LogInformation("Internet validation remained stable through the countdown. Exiting successfully.");
                _applicationLifetimeService.Exit(FoundryConnectExitCode.Success);
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

    private void SyncWifiNetworks(IReadOnlyList<WifiNetworkSummary> networks, string? connectedWifiSsid)
    {
        string? selectedSsid = SelectedWifiNetwork?.Ssid;
        string preservedPassphrase = SelectedWifiNetwork?.RequiresPassphrase == true
            ? SelectedWifiPassphrase
            : string.Empty;
        Dictionary<string, WifiNetworkItemViewModel> existingNetworks = WifiNetworks.ToDictionary(
            network => network.Ssid,
            StringComparer.OrdinalIgnoreCase);
        List<WifiNetworkItemViewModel> orderedNetworks = new(networks.Count);

        _isSyncingWifiNetworks = true;

        try
        {
            foreach (WifiNetworkSummary network in networks)
            {
                if (!existingNetworks.TryGetValue(network.Ssid, out WifiNetworkItemViewModel? wifiNetwork))
                {
                    wifiNetwork = new WifiNetworkItemViewModel(network.Ssid);
                }

                wifiNetwork.Update(
                    network.Ssid == "Hidden network" ? GetString("Wifi.HiddenNetwork") : network.Ssid,
                    TranslateDiscoveredWifiSecurity(network.Authentication),
                    network.SsidHex,
                    network.Authentication,
                    network.Encryption,
                    network.SignalStrengthPercent,
                    ResolveWifiGlyph(network.SignalStrengthPercent),
                    CanDirectConnect(network.Authentication),
                    RequiresPassphrase(network.Authentication),
                    string.Equals(network.Ssid, connectedWifiSsid, StringComparison.OrdinalIgnoreCase));
                orderedNetworks.Add(wifiNetwork);
            }

            SyncWifiNetworkCollection(orderedNetworks);

            SelectedWifiNetwork = orderedNetworks.FirstOrDefault(network =>
                string.Equals(network.Ssid, selectedSsid, StringComparison.OrdinalIgnoreCase))
                ?? orderedNetworks.FirstOrDefault(network =>
                    string.Equals(network.Ssid, connectedWifiSsid, StringComparison.OrdinalIgnoreCase));
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
        _disposeCts.Cancel();
        CancelCountdown();
        _countdownCts?.Dispose();
        _disposeCts.Dispose();
        _refreshGate.Dispose();
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
            _lastSelectedWifiNetworkSsid,
            value?.Ssid,
            StringComparison.OrdinalIgnoreCase);

        if (!_isSyncingWifiNetworks &&
            (value is null || !value.RequiresPassphrase || value.IsConnected || hasChangedSelection))
        {
            SelectedWifiPassphrase = string.Empty;
        }

        SelectedWifiActionFeedbackText = string.Empty;

        _lastSelectedWifiNetworkSsid = value?.Ssid;

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
            ApplyPrimaryStatus(HasInternetAccess);
            CurrentConnectionChipText = BuildCurrentConnectionChipText(IsEthernetConnected);
            OnPropertyChanged(nameof(CurrentCulture));
            OnPropertyChanged(nameof(VersionDisplay));
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
        });

        _ = RefreshCoreAsync(_disposeCts.Token);
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

    private async Task ApplyProvisionedSettingsAsync(CancellationToken cancellationToken)
    {
        if (_disposeCts.IsCancellationRequested)
        {
            return;
        }

        try
        {
            string status = await _networkBootstrapService.ApplyProvisionedSettingsAsync(cancellationToken).ConfigureAwait(false);
            string? feedback = BuildProvisionedWifiInitializationFeedback(status);
            if (!string.IsNullOrWhiteSpace(feedback))
            {
                await RunOnUiAsync(() => ProvisionedWifiActionFeedbackText = feedback).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // Ignore shutdown-driven cancellations.
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Provisioned settings initialization failed.");
            await RunOnUiAsync(() => ProvisionedWifiActionFeedbackText = GetString("Wifi.ActionApplyProvisionedFailed")).ConfigureAwait(false);
        }
    }

    private bool ShouldRetryConfiguredWifiConnect()
    {
        if (_lastConfiguredWifiConnectAttemptAt is null)
        {
            return true;
        }

        int retryDelaySeconds = Math.Max(FoundryConnectApplicationInfo.DefaultRefreshIntervalSeconds * 2, 10);
        return DateTimeOffset.UtcNow - _lastConfiguredWifiConnectAttemptAt.Value >= TimeSpan.FromSeconds(retryDelaySeconds);
    }

    private async Task ExecuteProvisionedWifiActionAsync(
        Func<Task<string>> action,
        Func<string, string?> resolveFeedback,
        bool refreshAfterAction)
    {
        if (_disposeCts.IsCancellationRequested)
        {
            return;
        }

        await RunOnUiAsync(() =>
        {
            IsNetworkActionInProgress = true;
            IsProvisionedWifiActionInProgress = true;
            ProvisionedWifiActionFeedbackText = string.Empty;
        }).ConfigureAwait(false);

        try
        {
            string status = await action().ConfigureAwait(false);
            string? feedback = resolveFeedback(status);

            await RunOnUiAsync(() =>
            {
                ProvisionedWifiActionFeedbackText = string.IsNullOrWhiteSpace(feedback)
                    ? string.Empty
                    : feedback;
            }).ConfigureAwait(false);

            if (refreshAfterAction)
            {
                await RefreshCoreAsync(_disposeCts.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // Ignore shutdown-driven cancellations.
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Provisioned network action failed.");
            await RunOnUiAsync(() => ProvisionedWifiActionFeedbackText = GetString("Wifi.ActionTryAgain")).ConfigureAwait(false);
        }
        finally
        {
            await RunOnUiAsync(() =>
            {
                IsProvisionedWifiActionInProgress = false;
                IsNetworkActionInProgress = false;
            }).ConfigureAwait(false);
        }
    }

    private async Task ExecuteSelectedWifiActionAsync(
        Func<Task<string>> action,
        Func<string, string?> resolveFeedback,
        bool refreshAfterAction)
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
            string status = await action().ConfigureAwait(false);
            string? feedback = resolveFeedback(status);

            await RunOnUiAsync(() =>
            {
                if (string.IsNullOrWhiteSpace(feedback))
                {
                    SelectedWifiActionFeedbackText = string.Empty;
                }
                else
                {
                    SelectedWifiActionFeedbackText = feedback;
                }
            }).ConfigureAwait(false);

            if (refreshAfterAction)
            {
                await RefreshCoreAsync(_disposeCts.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // Ignore shutdown-driven cancellations.
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Selected Wi-Fi action failed.");
            await RunOnUiAsync(() => SelectedWifiActionFeedbackText = GetString("Wifi.ActionTryAgain")).ConfigureAwait(false);
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

    private string? BuildSelectedWifiConnectFeedback(string status)
    {
        if (status.StartsWith("Wi-Fi connected to ", StringComparison.Ordinal))
        {
            return null;
        }

        if (status.Contains("not supported in this build", StringComparison.OrdinalIgnoreCase))
        {
            return GetString("Wifi.ActionSelectedNotSupported");
        }

        if (status.Contains("No wireless adapter", StringComparison.OrdinalIgnoreCase))
        {
            return GetString("Wifi.NoAdapterAvailable");
        }

        return GetString("Wifi.ActionConnectFailed");
    }

    private string? BuildProvisionedWifiConnectFeedback(string status)
    {
        if (status.Contains("Wi-Fi connected to ", StringComparison.Ordinal))
        {
            return null;
        }

        if (status.Contains("No wireless adapter", StringComparison.OrdinalIgnoreCase))
        {
            return GetString("Wifi.NoAdapterAvailable");
        }

        if (status.Contains("not provisioned for this image", StringComparison.OrdinalIgnoreCase) ||
            status.Contains("No Wi-Fi profile is available", StringComparison.OrdinalIgnoreCase))
        {
            return GetString("Wifi.ActionProvisionedProfileUnavailable");
        }

        return GetString("Wifi.ActionProvisionedConnectFailed");
    }

    private string? BuildProvisionedWifiDisconnectFeedback(string status)
    {
        if (status.StartsWith("Wi-Fi disconnected from ", StringComparison.Ordinal) ||
            status.StartsWith("Wi-Fi is already disconnected.", StringComparison.Ordinal))
        {
            return null;
        }

        if (status.Contains("No wireless adapter", StringComparison.OrdinalIgnoreCase))
        {
            return GetString("Wifi.NoAdapterAvailable");
        }

        return GetString("Wifi.ActionProvisionedDisconnectFailed");
    }

    private string? BuildProvisionedWifiInitializationFeedback(string status)
    {
        if (!HasProvisionedWifiProfile ||
            string.IsNullOrWhiteSpace(status) ||
            status.Contains("imported.", StringComparison.OrdinalIgnoreCase) &&
            !status.Contains("failed", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (status.Contains("failed", StringComparison.OrdinalIgnoreCase) ||
            status.Contains("not supported", StringComparison.OrdinalIgnoreCase) ||
            status.Contains("No Wi-Fi profile", StringComparison.OrdinalIgnoreCase))
        {
            if (status.Contains("No Wi-Fi profile", StringComparison.OrdinalIgnoreCase))
            {
                return GetString("Wifi.ActionProvisionedProfileUnavailable");
            }

            return GetString("Wifi.ActionApplyProvisionedFailed");
        }

        return null;
    }

    private string? BuildSelectedWifiDisconnectFeedback(string status)
    {
        if (status.StartsWith("Wi-Fi disconnected from ", StringComparison.Ordinal) ||
            status.StartsWith("Wi-Fi is already disconnected.", StringComparison.Ordinal))
        {
            return null;
        }

        if (status.Contains("No wireless adapter", StringComparison.OrdinalIgnoreCase))
        {
            return GetString("Wifi.NoAdapterAvailable");
        }

        return GetString("Wifi.ActionDisconnectFailed");
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
            try
            {
                string? profileName = ProvisionedWifiProfileResolver.ResolveProfileName(
                    _configuration.Wifi,
                    _configurationService.ConfigurationPath);
                if (!string.IsNullOrWhiteSpace(profileName))
                {
                    return profileName;
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed to resolve the provisioned Wi-Fi profile name.");
            }

            return GetString("Wifi.EnterpriseProfile");
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
            string? profileAuthentication = ProvisionedWifiProfileResolver.TryReadProfileAuthentication(profilePath);
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

    private bool IsProvisionedWifiConnection(string? ssid)
    {
        if (!HasProvisionedWifiProfile || string.IsNullOrWhiteSpace(ssid))
        {
            return false;
        }

        string trimmedSsid = ssid.Trim();

        if (!string.IsNullOrWhiteSpace(_configuration.Wifi.Ssid) &&
            string.Equals(trimmedSsid, _configuration.Wifi.Ssid.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        string profileName = ResolveProvisionedWifiProfileName();
        return !string.IsNullOrWhiteSpace(profileName) &&
               !string.Equals(profileName, GetString("Wifi.EnterpriseProfile"), StringComparison.OrdinalIgnoreCase) &&
               string.Equals(trimmedSsid, profileName, StringComparison.OrdinalIgnoreCase);
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
