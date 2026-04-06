using System.Collections.ObjectModel;
using System.Diagnostics;
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
using Foundry.Connect.Services.Network;
using Foundry.Connect.Services.Theme;
using Microsoft.Extensions.Logging;
using ConnectThemeMode = Foundry.Connect.Services.Theme.ThemeMode;

namespace Foundry.Connect.ViewModels;

public partial class MainWindowViewModel : ObservableObject, IDisposable
{
    private const double CompactViewportWidthThreshold = 1360;
    private const double CompactViewportHeightThreshold = 900;
    private const double CompactContentMargin = 16;
    private const double WideContentMargin = 32;
    private const double WideContentMaxWidth = 1320;
    private const double CompactWifiListMinHeightValue = 120;
    private const double CompactWifiListMaxHeightValue = 180;
    private const double WideWifiListMinHeightValue = 160;
    private const double WideWifiListMaxHeightValue = 320;
    private const string EthernetOkGlyph = "\uE839";
    private const string EthernetWarningGlyph = "\uEB56";
    private const string EthernetErrorGlyph = "\uEB55";
    private const string NetworkOfflineGlyph = "\uF384";
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
    private Task? _monitoringTask;
    private bool _isInitialized;
    private bool _isDisposed;
    private bool _isSyncingWifiNetworks;
    private string? _lastSelectedWifiNetworkSsid;
    private DateTimeOffset? _lastConfiguredWifiConnectAttemptAt;

    [ObservableProperty]
    private NetworkLayoutMode layoutMode;

    [ObservableProperty]
    private ConnectViewportMode viewportMode = ConnectViewportMode.Compact;

    [ObservableProperty]
    private NetworkLayoutMode? debugLayoutOverride;

    [ObservableProperty]
    private string primaryStatusGlyph = NetworkOfflineGlyph;

    [ObservableProperty]
    private string primaryStatusTitle = "Waiting for network validation";

    [ObservableProperty]
    private string primaryStatusDescription = "Foundry.Connect is validating network reachability before the bootstrap can continue.";

    [ObservableProperty]
    private string ethernetGlyph = EthernetErrorGlyph;

    [ObservableProperty]
    private string ethernetStatusText = "No ethernet adapter detected.";

    [ObservableProperty]
    private string internetStatusText = "Internet validation has not succeeded yet.";

    [ObservableProperty]
    private string wifiStatusText = "Wi-Fi is not provisioned for this environment.";

    [ObservableProperty]
    private string adapterName = "Unavailable";

    [ObservableProperty]
    private string ipAddress = "Unavailable";

    [ObservableProperty]
    private string subnetMask = "Unavailable";

    [ObservableProperty]
    private string gatewayAddress = "Unavailable";

    [ObservableProperty]
    private string dnsServers = "Unavailable";

    [ObservableProperty]
    private string dhcpText = "Unavailable";

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
    private string networkActionStatusText = "Provisioned network settings have not been applied yet.";

    [ObservableProperty]
    private WifiNetworkItemViewModel? selectedWifiNetwork;

    [ObservableProperty]
    private string selectedWifiPassphrase = string.Empty;

    [ObservableProperty]
    private Thickness mainContentMargin = new(CompactContentMargin);

    [ObservableProperty]
    private double centerContentWidth = 960;

    [ObservableProperty]
    private double wifiNetworkListMinHeight = CompactWifiListMinHeightValue;

    [ObservableProperty]
    private double wifiNetworkListMaxHeight = CompactWifiListMaxHeightValue;

    [ObservableProperty]
    private bool isDiagnosticsExpanded;

    public MainWindowViewModel(
        IThemeService themeService,
        IApplicationShellService applicationShellService,
        IApplicationLifetimeService applicationLifetimeService,
        IConnectConfigurationService configurationService,
        FoundryConnectConfiguration configuration,
        INetworkBootstrapService networkBootstrapService,
        INetworkStatusService networkStatusService,
        ILogger<MainWindowViewModel> logger)
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
        VersionDisplay = $"Version: {FoundryConnectApplicationInfo.Version}";
        ConfigurationSourceText = _configurationService.IsLoadedFromDisk && !string.IsNullOrWhiteSpace(_configurationService.ConfigurationPath)
            ? $"Configuration: {_configurationService.ConfigurationPath}"
            : "Configuration: built-in defaults";
        RefreshIntervalText = $"Refresh: every {FoundryConnectApplicationInfo.DefaultRefreshIntervalSeconds} seconds";
    }

    public ObservableCollection<WifiNetworkItemViewModel> WifiNetworks { get; } = [];

    public ConnectThemeMode CurrentTheme => _themeService.CurrentTheme;

    public string VersionDisplay { get; }

    public string ConfigurationSourceText { get; }

    public string RefreshIntervalText { get; }

    public string WindowTitle => FoundryConnectApplicationInfo.WindowTitle;

    public string CountdownText => $"Continuing bootstrap in {CountdownSecondsRemaining}s";

    public string LastUpdatedText => LastUpdatedAt is null
        ? "Last update: pending"
        : $"Last update: {LastUpdatedAt.Value.LocalDateTime:HH:mm:ss}";

    public bool HasWifiNetworks => WifiNetworks.Count > 0;

    public bool IsCompactViewport => ViewportMode == ConnectViewportMode.Compact;

    public bool IsWideViewport => ViewportMode == ConnectViewportMode.Wide;

    public bool IsDebugMenuVisible => Debugger.IsAttached;

    public NetworkLayoutMode EffectiveLayoutMode => DebugLayoutOverride ?? LayoutMode;

    public bool CanConnectConfiguredWifi => _configuration.Capabilities.WifiProvisioned && _configuration.Wifi.IsEnabled && !IsNetworkActionInProgress;
    public string WifiDiscoveryEmptyStateText => BuildWifiDiscoveryEmptyStateText();
    public bool CanConnectSelectedWifi => SelectedWifiNetwork is { CanDirectConnect: true } network &&
                                          !network.IsConnected &&
                                          (!network.RequiresPassphrase || !string.IsNullOrWhiteSpace(SelectedWifiPassphrase)) &&
                                          !IsNetworkActionInProgress;
    public bool CanDisconnectSelectedWifi => SelectedWifiNetwork is { IsConnected: true } && !IsNetworkActionInProgress;

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

        _monitoringTask = MonitorAsync(_disposeCts.Token);
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
    private void ShowAbout()
    {
        _applicationShellService.ShowAbout();
    }

    [RelayCommand]
    private Task RefreshStatusAsync()
    {
        return RefreshCoreAsync(_disposeCts.Token);
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
        await ExecuteNetworkActionAsync(
            () => _networkBootstrapService.ConnectConfiguredWifiAsync(_disposeCts.Token),
            refreshAfterAction: true).ConfigureAwait(false);
    }

    [RelayCommand(CanExecute = nameof(CanConnectSelectedWifi))]
    private async Task ConnectSelectedWifiAsync()
    {
        if (SelectedWifiNetwork is null)
        {
            return;
        }

        await RunOnUiAsync(() => IsSelectedWifiActionInProgress = true).ConfigureAwait(false);

        try
        {
            await ExecuteNetworkActionAsync(
                () => _networkBootstrapService.ConnectWifiNetworkAsync(
                    SelectedWifiNetwork.Ssid,
                    SelectedWifiNetwork.Authentication,
                    SelectedWifiNetwork.RequiresPassphrase ? SelectedWifiPassphrase : null,
                    _disposeCts.Token),
                refreshAfterAction: true).ConfigureAwait(false);
        }
        finally
        {
            await RunOnUiAsync(() => IsSelectedWifiActionInProgress = false).ConfigureAwait(false);
        }
    }

    [RelayCommand(CanExecute = nameof(CanDisconnectSelectedWifi))]
    private async Task DisconnectSelectedWifiAsync()
    {
        if (SelectedWifiNetwork is not { IsConnected: true })
        {
            return;
        }

        await RunOnUiAsync(() => IsSelectedWifiActionInProgress = true).ConfigureAwait(false);

        try
        {
            await ExecuteNetworkActionAsync(
                () => _networkBootstrapService.DisconnectWifiAsync(_disposeCts.Token),
                refreshAfterAction: true).ConfigureAwait(false);
        }
        finally
        {
            await RunOnUiAsync(() => IsSelectedWifiActionInProgress = false).ConfigureAwait(false);
        }
    }

    public void HandleWindowClosing()
    {
        if (!_applicationLifetimeService.IsExitRequested)
        {
            _logger.LogInformation("Window close detected before a controlled exit. Returning user-aborted exit code.");
            _applicationLifetimeService.Exit(FoundryConnectExitCode.UserAborted);
        }
    }

    public void UpdateViewport(double windowWidth, double windowHeight)
    {
        if (windowWidth <= 0 || windowHeight <= 0)
        {
            return;
        }

        bool useCompactViewport = windowWidth < CompactViewportWidthThreshold || windowHeight < CompactViewportHeightThreshold;
        double horizontalMargin = useCompactViewport ? CompactContentMargin : WideContentMargin;
        double availableWidth = Math.Max(320, windowWidth - (horizontalMargin * 2));

        ViewportMode = useCompactViewport ? ConnectViewportMode.Compact : ConnectViewportMode.Wide;
        MainContentMargin = new Thickness(horizontalMargin, horizontalMargin, horizontalMargin, horizontalMargin);
        CenterContentWidth = useCompactViewport
            ? availableWidth
            : Math.Min(availableWidth, WideContentMaxWidth);
        WifiNetworkListMinHeight = useCompactViewport ? CompactWifiListMinHeightValue : WideWifiListMinHeightValue;
        WifiNetworkListMaxHeight = useCompactViewport ? CompactWifiListMaxHeightValue : WideWifiListMaxHeightValue;
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
                PrimaryStatusGlyph = NetworkOfflineGlyph;
                PrimaryStatusTitle = "Network refresh failed";
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

        EthernetGlyph = ResolveEthernetGlyph(snapshot);
        EthernetStatusText = snapshot.EthernetStatusText;
        InternetStatusText = snapshot.InternetStatusText;
        WifiStatusText = snapshot.WifiStatusText;
        AdapterName = snapshot.AdapterName;
        IpAddress = snapshot.IpAddress;
        SubnetMask = snapshot.SubnetMask;
        GatewayAddress = snapshot.GatewayAddress;
        DnsServers = snapshot.DnsServers;
        DhcpText = snapshot.DhcpText;
        LastUpdatedAt = DateTimeOffset.Now;
        OnPropertyChanged(nameof(LastUpdatedText));
        OnPropertyChanged(nameof(WifiDiscoveryEmptyStateText));

        SyncWifiNetworks(snapshot.WifiNetworks, snapshot.ConnectedWifiSsid);
        ApplyPrimaryStatus(snapshot);
        UpdateCountdown(snapshot);

        if (!snapshot.HasInternetAccess &&
            snapshot.LayoutMode == NetworkLayoutMode.EthernetWifi &&
            snapshot.IsWifiRuntimeAvailable &&
            _configuration.Wifi.IsEnabled &&
            !IsNetworkActionInProgress &&
            ShouldRetryConfiguredWifiConnect())
        {
            _lastConfiguredWifiConnectAttemptAt = DateTimeOffset.UtcNow;
            _ = Task.Run(async () =>
            {
                await ExecuteNetworkActionAsync(
                    () => _networkBootstrapService.ConnectConfiguredWifiAsync(_disposeCts.Token),
                    refreshAfterAction: true).ConfigureAwait(false);
            });
        }
    }

    private void ApplyPrimaryStatus(NetworkStatusSnapshot snapshot)
    {
        if (snapshot.HasInternetAccess)
        {
            PrimaryStatusGlyph = EthernetOkGlyph;
            PrimaryStatusTitle = "Internet validation succeeded";
            PrimaryStatusDescription = snapshot.ConnectionSummary;
            return;
        }

        PrimaryStatusGlyph = NetworkOfflineGlyph;
        PrimaryStatusTitle = "Waiting for internet reachability";
        PrimaryStatusDescription = snapshot.ConnectionSummary;
    }

    private void UpdateCountdown(NetworkStatusSnapshot snapshot)
    {
        if (!_isAutoCloseEnabled)
        {
            CancelCountdown();
            return;
        }

        if (snapshot.HasInternetAccess)
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

        CountdownSecondsRemaining = FoundryConnectApplicationInfo.DefaultAutoCloseDelaySeconds;
        IsCountdownActive = true;
        OnPropertyChanged(nameof(CountdownText));

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
                    OnPropertyChanged(nameof(CountdownText));
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
        OnPropertyChanged(nameof(CountdownText));
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
        if (snapshot.IsEthernetConnected && snapshot.HasDhcpLease)
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

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _disposeCts.Cancel();
        CancelCountdown();
        _countdownCts?.Dispose();
        _disposeCts.Dispose();
        _refreshGate.Dispose();
        _isDisposed = true;
    }

    partial void OnIsNetworkActionInProgressChanged(bool value)
    {
        ConnectConfiguredWifiCommand.NotifyCanExecuteChanged();
        ConnectSelectedWifiCommand.NotifyCanExecuteChanged();
        DisconnectSelectedWifiCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(CanConnectConfiguredWifi));
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

        _lastSelectedWifiNetworkSsid = value?.Ssid;

        ConnectSelectedWifiCommand.NotifyCanExecuteChanged();
        DisconnectSelectedWifiCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(CanConnectSelectedWifi));
        OnPropertyChanged(nameof(CanDisconnectSelectedWifi));
    }

    partial void OnSelectedWifiPassphraseChanged(string value)
    {
        ConnectSelectedWifiCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(CanConnectSelectedWifi));
    }

    partial void OnLayoutModeChanged(NetworkLayoutMode value)
    {
        OnPropertyChanged(nameof(EffectiveLayoutMode));
    }

    partial void OnViewportModeChanged(ConnectViewportMode value)
    {
        OnPropertyChanged(nameof(IsCompactViewport));
        OnPropertyChanged(nameof(IsWideViewport));
    }

    partial void OnDebugLayoutOverrideChanged(NetworkLayoutMode? value)
    {
        OnPropertyChanged(nameof(EffectiveLayoutMode));
    }

    private Task ApplyProvisionedSettingsAsync(CancellationToken cancellationToken)
    {
        return ExecuteNetworkActionAsync(
            () => _networkBootstrapService.ApplyProvisionedSettingsAsync(cancellationToken),
            refreshAfterAction: false);
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

    private async Task ExecuteNetworkActionAsync(Func<Task<string>> action, bool refreshAfterAction)
    {
        if (_disposeCts.IsCancellationRequested)
        {
            return;
        }

        await RunOnUiAsync(() => IsNetworkActionInProgress = true).ConfigureAwait(false);

        try
        {
            string status = await action().ConfigureAwait(false);
            await RunOnUiAsync(() => NetworkActionStatusText = status).ConfigureAwait(false);

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
            await RunOnUiAsync(() => NetworkActionStatusText = $"Network action failed: {ex.Message}").ConfigureAwait(false);
        }
        finally
        {
            await RunOnUiAsync(() => IsNetworkActionInProgress = false).ConfigureAwait(false);
        }
    }

    public sealed class WifiNetworkItemViewModel : ObservableObject
    {
        private string _authentication = string.Empty;
        private string _encryption = string.Empty;
        private int _signalStrengthPercent;
        private string _signalGlyph = string.Empty;
        private bool _canDirectConnect;
        private bool _requiresPassphrase;
        private bool _isConnected;

        public WifiNetworkItemViewModel(string ssid)
        {
            Ssid = ssid;
        }

        public string Ssid { get; }

        public string Authentication
        {
            get => _authentication;
            private set => SetProperty(ref _authentication, value);
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
            string authentication,
            string encryption,
            int signalStrengthPercent,
            string signalGlyph,
            bool canDirectConnect,
            bool requiresPassphrase,
            bool isConnected)
        {
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
        if (!IsWifiRuntimeAvailable)
        {
            return "Wi-Fi support is not available at runtime.";
        }

        if (!HasWirelessAdapter)
        {
            return "No wireless adapter is currently detected.";
        }

        return "No Wi-Fi networks are currently visible.";
    }

    private static bool CanDirectConnect(string authentication)
    {
        return ClassifyDiscoveredWifi(authentication) is DiscoveredWifiType.Open or DiscoveredWifiType.Personal;
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
        Personal,
        Enterprise
    }
}
