// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Models.Configuration;
using Foundry.Connect.Lifetime;
using Foundry.Connect.Network;
using Foundry.Core.Models.Runtime;
using Foundry.Wpf.Shell;
using Foundry.Connect.Configuration;
using Foundry.Wpf.Theme;
using Foundry.Connect.ViewModels;
using Foundry.Utilities.Globalization;
using Foundry.Wpf.Localization;
using System.Reflection;
using System.Resources;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Foundry.Connect.Tests;

public sealed class MainWindowViewModelTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 29, 8, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RefreshStatusCommand_WhenSelectedNetworkConnectionChanges_RefreshesWifiCommandAvailability(bool initiallyConnected)
    {
        static NetworkState State(bool connected) => NetworkState.Initial(Now) with
        {
            Wifi = WifiState.Initial with
            {
                Availability = WifiAvailability.Available,
                Networks = [new VisibleWifiNetwork(Guid.Empty, WifiSsid.FromText("Foundry"), WifiSecurityType.Open, 80)],
                Connection = connected
                    ? new WifiConnection(Guid.Empty, WifiSsid.FromText("Foundry"), "Foundry", WifiSecurityType.Open)
                    : null
            }
        };

        var coordinator = new FakeConnectionCoordinator();
        var monitor = new FakeNetworkMonitor();
        using MainWindowViewModel viewModel = CreateViewModel(coordinator, monitor);
        await viewModel.InitializeAsync();
        coordinator.RaiseStateChanged(State(initiallyConnected));
        viewModel.SelectedWifiNetwork = Assert.Single(viewModel.WifiNetworks);
        MainWindowViewModel.WifiNetworkItemViewModel selectedNetwork = viewModel.SelectedWifiNetwork;
        bool canConnect = viewModel.ConnectSelectedWifiCommand.CanExecute(null);
        bool canDisconnect = viewModel.DisconnectSelectedWifiCommand.CanExecute(null);
        Assert.Equal(!initiallyConnected, canConnect);
        Assert.Equal(initiallyConnected, canDisconnect);
        viewModel.ConnectSelectedWifiCommand.CanExecuteChanged += (_, _) =>
            canConnect = viewModel.ConnectSelectedWifiCommand.CanExecute(null);
        viewModel.DisconnectSelectedWifiCommand.CanExecuteChanged += (_, _) =>
            canDisconnect = viewModel.DisconnectSelectedWifiCommand.CanExecute(null);

        coordinator.RaiseStateChanged(State(!initiallyConnected));
        await viewModel.RefreshStatusCommand.ExecuteAsync(null);

        Assert.Same(selectedNetwork, viewModel.SelectedWifiNetwork);
        Assert.Equal(initiallyConnected, canConnect);
        Assert.Equal(!initiallyConnected, canDisconnect);
        Assert.Equal(1, monitor.RefreshRequests);
    }

    [Fact]
    public async Task InitializeAsync_AppliesProvisioningThenStartsMonitoring()
    {
        var coordinator = new FakeConnectionCoordinator();
        var monitor = new FakeNetworkMonitor();
        var successfulExit = new FakeSuccessfulExitService();
        int provisioningCallsWhenMonitoringStarted = -1;
        monitor.OnRun = () => provisioningCallsWhenMonitoringStarted = coordinator.ApplyProvisioningCalls;
        using MainWindowViewModel viewModel = CreateViewModel(coordinator, monitor, successfulExit);

        await viewModel.InitializeAsync();

        Assert.Equal(1, coordinator.ApplyProvisioningCalls);
        Assert.Equal(1, monitor.RunCalls);
        Assert.Equal(1, provisioningCallsWhenMonitoringStarted);
        Assert.Equal(0, successfulExit.Calls);
    }

    [Fact]
    public async Task InitializeAsync_WhenProvisionedWifiIsUnavailable_ShowsProvisionedFeedback()
    {
        var coordinator = new FakeConnectionCoordinator
        {
            Provisioning = new ProvisioningOutcome(
                Dot1xState.NotConfigured,
                new ProvisionedWifiState(ProvisionedWifiStatus.Unavailable, ProvisionedWifiUnavailableReason.SecretUnavailable),
                null,
                [NetworkFailureCode.WifiSecretUnavailable])
        };
        using MainWindowViewModel viewModel = CreateViewModel(coordinator);

        await viewModel.InitializeAsync();

        Assert.Equal(viewModel.Strings["Wifi.ActionProvisionedProfileUnavailable"], viewModel.ProvisionedWifiActionFeedbackText);
    }

    [Fact]
    public async Task ContinueBootstrapCommand_WhenOnline_CallsSuccessfulExit()
    {
        var coordinator = new FakeConnectionCoordinator();
        var successfulExit = new FakeSuccessfulExitService();
        using MainWindowViewModel viewModel = CreateViewModel(coordinator, successfulExit: successfulExit);
        await viewModel.InitializeAsync();
        coordinator.RaiseStateChanged(CreateOnlineState());

        await viewModel.ContinueBootstrapCommand.ExecuteAsync(null);

        Assert.Equal(1, successfulExit.Calls);
    }

    [Fact]
    public async Task ConnectSelectedWifiAsync_WhenRuntimeSupportIsMissing_ShowsNotSupportedFeedback()
    {
        var coordinator = new FakeConnectionCoordinator();
        coordinator.ConnectWifiNetworkResults.Enqueue(OperationOutcome.Failed(NetworkOperationKind.ConnectWifiNetwork, NetworkFailureCode.UnsupportedSecurity));
        using MainWindowViewModel viewModel = CreateViewModel(coordinator);
        await viewModel.InitializeAsync();
        coordinator.RaiseStateChanged(CreateDisconnectedState());
        viewModel.SelectedWifiNetwork = Assert.Single(viewModel.WifiNetworks);

        await InvokeNonPublicAsync(viewModel, "ConnectSelectedWifiAsync");

        Assert.Equal(viewModel.Strings["Wifi.ActionSelectedNotSupported"], viewModel.SelectedWifiActionFeedbackText);
    }

    [Fact]
    public async Task ConnectSelectedWifiAsync_WhenAnotherFailureIsReturned_ShowsGenericConnectFailure()
    {
        var coordinator = new FakeConnectionCoordinator();
        coordinator.ConnectWifiNetworkResults.Enqueue(OperationOutcome.Failed(NetworkOperationKind.ConnectWifiNetwork, NetworkFailureCode.ConnectTimedOut));
        using MainWindowViewModel viewModel = CreateViewModel(coordinator);
        await viewModel.InitializeAsync();
        coordinator.RaiseStateChanged(CreateDisconnectedState());
        viewModel.SelectedWifiNetwork = Assert.Single(viewModel.WifiNetworks);

        await InvokeNonPublicAsync(viewModel, "ConnectSelectedWifiAsync");

        Assert.Equal(viewModel.Strings["Wifi.ActionConnectFailed"], viewModel.SelectedWifiActionFeedbackText);
        Assert.Equal(WifiSsid.FromText("Foundry"), Assert.Single(coordinator.ConnectedSsids));
    }

    [Fact]
    public async Task ApplyState_DoesNotStartAutomaticConnections()
    {
        var coordinator = new FakeConnectionCoordinator();
        using MainWindowViewModel viewModel = CreateViewModel(coordinator);
        await viewModel.InitializeAsync();

        coordinator.RaiseStateChanged(CreateDisconnectedState() with
        {
            Internet = InternetStatus.NoInternet,
            Wifi = CreateDisconnectedState().Wifi with
            {
                Provisioned = new ProvisionedWifiState(ProvisionedWifiStatus.Ready, Ssid: WifiSsid.FromText("Foundry"), ProfileName: "Foundry")
            }
        });

        Assert.Equal(0, coordinator.ConnectProvisionedWifiCalls);
        Assert.Equal(0, coordinator.ConnectWifiNetworkCalls);
        Assert.Equal(0, coordinator.DisconnectWifiCalls);
    }

    [Theory]
    [InlineData(BootMediaUpdateReason.NewerRelease, "BootMedia.UpdateAvailable", "BootMedia.UpdateRecommendedToolTip")]
    [InlineData(BootMediaUpdateReason.UnknownAuthoringVersion, "BootMedia.UpdateRecommended", "BootMedia.UnknownAuthoringVersionToolTip")]
    public void BootMediaBanner_SelectsCaptionAndToolTipForReason(BootMediaUpdateReason reason, string textKey, string toolTipKey)
    {
        using MainWindowViewModel viewModel = CreateViewModel(bootMediaUpdateReason: reason);

        Assert.True(viewModel.IsBootMediaUpdateRecommended);
        Assert.Equal(viewModel.Strings[textKey], viewModel.BootMediaUpdateRecommendedText);
        Assert.Equal(viewModel.Strings[toolTipKey], viewModel.BootMediaUpdateRecommendedToolTip);
    }

    [Fact]
    public void OpenLogFileCommand_WhenLogFileIsUnavailable_LogsWarningWithoutThrowing()
    {
        var logger = new RecordingLogger<MainWindowViewModel>();
        using MainWindowViewModel viewModel = CreateViewModel(logger: logger);

        viewModel.OpenLogFileCommand.Execute(null);

        LogEntry entry = Assert.Single(logger.Entries, item => item.Level == LogLevel.Warning);
        Assert.True(entry.Properties.ContainsKey("LogFilePath"));
    }

    [Fact]
    public void KeyboardMenu_ListsActiveLanguageFirst()
    {
        using MainWindowViewModel viewModel = CreateViewModel(keyboardLayoutSwitcher: new FakeKeyboardLayoutSwitcher("0000040C"));

        Assert.Equal(["0000080C", "0000040C", null, null], viewModel.KeyboardMenuEntries.Select(entry => entry.LayoutId));
        Assert.True(viewModel.KeyboardMenuEntries[1].IsSelected);
        Assert.True(viewModel.KeyboardMenuEntries[2].IsSeparator);
        KeyboardMenuEntry dutch = Assert.Single(viewModel.KeyboardMenuEntries[3].Children);
        Assert.Equal(["00000413"], dutch.Children.Select(entry => entry.LayoutId));
        Assert.Contains("FR", viewModel.KeyboardMenuHeader, StringComparison.Ordinal);
    }

    [Fact]
    public void SetKeyboardLayoutCommand_AppliesAndRefreshesSelection()
    {
        var switcher = new FakeKeyboardLayoutSwitcher("0000040C");
        using MainWindowViewModel viewModel = CreateViewModel(keyboardLayoutSwitcher: switcher);

        viewModel.SetKeyboardLayoutCommand.Execute("0000080C");

        Assert.Equal(["0000080C"], switcher.AppliedLayoutIds);
        Assert.True(viewModel.KeyboardMenuEntries.Single(entry => entry.LayoutId == "0000080C").IsSelected);
        Assert.Contains("BE", viewModel.KeyboardMenuHeader, StringComparison.Ordinal);
    }

    [Fact]
    public void SetKeyboardLayoutCommand_WhenApplyFails_KeepsPreviousSelection()
    {
        var switcher = new FakeKeyboardLayoutSwitcher("0000040C") { FailApply = true };
        using MainWindowViewModel viewModel = CreateViewModel(keyboardLayoutSwitcher: switcher);

        viewModel.SetKeyboardLayoutCommand.Execute("0000080C");

        Assert.True(viewModel.KeyboardMenuEntries.Single(entry => entry.LayoutId == "0000040C").IsSelected);
        Assert.Contains("FR", viewModel.KeyboardMenuHeader, StringComparison.Ordinal);
    }

    private static MainWindowViewModel CreateViewModel(
        FakeConnectionCoordinator? coordinator = null,
        FakeNetworkMonitor? monitor = null,
        FakeSuccessfulExitService? successfulExit = null,
        ILogger<MainWindowViewModel>? logger = null,
        BootMediaUpdateReason bootMediaUpdateReason = BootMediaUpdateReason.None,
        IKeyboardLayoutSwitcher? keyboardLayoutSwitcher = null)
    {
        var configuration = new FoundryConnectConfigurationDocument
        {
            Capabilities = new ConnectNetworkCapabilitiesSettings { WifiProvisioned = true },
            Wifi = new ConnectWifiSettings
            {
                IsEnabled = true,
                SecurityType = "WPA2-Personal",
                Ssid = "Foundry"
            },
            Dot1x = new ConnectDot1xSettings { IsEnabled = true }
        };

        return new MainWindowViewModel(
            new FakeThemeService(),
            new ResourceLocalizationService(new ResourceManager("Foundry.Connect.Strings.Resources", typeof(App).Assembly)),
            new FakeApplicationShellService(),
            new RecordingApplicationLifetimeService(),
            new FakeConnectConfigurationService(configuration, bootMediaUpdateReason),
            configuration,
            coordinator ?? new FakeConnectionCoordinator(),
            monitor ?? new FakeNetworkMonitor(),
            successfulExit ?? new FakeSuccessfulExitService(),
            keyboardLayoutSwitcher ?? new FakeKeyboardLayoutSwitcher("0000040C"),
            logger ?? NullLogger<MainWindowViewModel>.Instance);
    }

    private static NetworkState CreateOnlineState()
    {
        return NetworkState.Initial(Now) with
        {
            Ethernet = new EthernetState([new EthernetInterfaceState("eth0", "Ethernet", true, IpConfigurationKind.Dhcp, "192.0.2.10", "192.0.2.1")], Dot1xState.NotConfigured),
            Wifi = WifiState.Initial with
            {
                Availability = WifiAvailability.Available,
                Networks = [new VisibleWifiNetwork(Guid.Empty, WifiSsid.FromText("Foundry"), WifiSecurityType.Wpa2Personal, 100)]
            },
            Internet = InternetStatus.Online
        };
    }

    private static NetworkState CreateDisconnectedState()
    {
        return NetworkState.Initial(Now) with
        {
            Ethernet = new EthernetState([new EthernetInterfaceState("eth0", "Ethernet", false, IpConfigurationKind.None, null, null)], Dot1xState.NotConfigured),
            Wifi = WifiState.Initial with
            {
                Availability = WifiAvailability.Available,
                Networks = [new VisibleWifiNetwork(Guid.Empty, WifiSsid.FromText("Foundry"), WifiSecurityType.Wpa2Personal, 100)]
            },
            Internet = InternetStatus.NoInternet
        };
    }

    private static async Task InvokeNonPublicAsync(object instance, string methodName)
    {
        MethodInfo method = instance.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException($"Method '{methodName}' was not found.");
        Task task = Assert.IsAssignableFrom<Task>(method.Invoke(instance, null));
        await task;
    }

    private sealed class FakeConnectionCoordinator : IConnectionCoordinator
    {
        public NetworkState State { get; set; } = NetworkState.Initial(Now);

        public ProvisioningOutcome Provisioning { get; init; } = ProvisioningOutcome.NotConfigured;

        public Queue<OperationOutcome> ConnectProvisionedWifiResults { get; } = [];

        public Queue<OperationOutcome> ConnectWifiNetworkResults { get; } = [];

        public Queue<OperationOutcome> DisconnectWifiResults { get; } = [];

        public List<WifiSsid> ConnectedSsids { get; } = [];

        public int ApplyProvisioningCalls { get; private set; }

        public int ConnectProvisionedWifiCalls { get; private set; }

        public int ConnectWifiNetworkCalls { get; private set; }

        public int DisconnectWifiCalls { get; private set; }

        public event EventHandler<NetworkState>? StateChanged;

        public void RaiseStateChanged(NetworkState state)
        {
            State = state;
            StateChanged?.Invoke(this, state);
        }

        public Task<ProvisioningOutcome> ApplyProvisioningAsync(CancellationToken cancellationToken)
        {
            ApplyProvisioningCalls++;
            return Task.FromResult(Provisioning);
        }

        public Task ApplyObservationAsync(NetworkObservation observation, CancellationToken cancellationToken) =>
            throw new NotSupportedException("The view model never applies observations.");

        public Task<OperationOutcome> ConnectProvisionedWifiAsync(CancellationToken cancellationToken)
        {
            ConnectProvisionedWifiCalls++;
            return Task.FromResult(Next(ConnectProvisionedWifiResults, NetworkOperationKind.ConnectProvisionedWifi));
        }

        public Task<OperationOutcome> ConnectWifiNetworkAsync(WifiSsid ssid, string? passphrase, CancellationToken cancellationToken)
        {
            ConnectWifiNetworkCalls++;
            ConnectedSsids.Add(ssid);
            return Task.FromResult(Next(ConnectWifiNetworkResults, NetworkOperationKind.ConnectWifiNetwork));
        }

        public Task<OperationOutcome> DisconnectWifiAsync(CancellationToken cancellationToken)
        {
            DisconnectWifiCalls++;
            return Task.FromResult(Next(DisconnectWifiResults, NetworkOperationKind.DisconnectWifi));
        }

        public void ReleaseProvisionedProfile()
        {
        }

        private static OperationOutcome Next(Queue<OperationOutcome> results, NetworkOperationKind kind) =>
            results.Count > 0 ? results.Dequeue() : OperationOutcome.Success(kind);
    }

    private sealed class FakeNetworkMonitor : INetworkMonitor
    {
        public int RunCalls { get; private set; }

        public int RefreshRequests { get; private set; }

        public Action? OnRun { get; set; }

        public Task RunAsync(CancellationToken cancellationToken)
        {
            RunCalls++;
            OnRun?.Invoke();
            return Task.CompletedTask;
        }

        public void RequestRefresh() => RefreshRequests++;
    }

    private sealed class FakeSuccessfulExitService : ISuccessfulExitService
    {
        public int Calls { get; private set; }

        public SuccessfulExitResult Result { get; init; } = SuccessfulExitResult.Exited;

        public Task<SuccessfulExitResult> TryCompleteAsync(CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(Result);
        }
    }

    private sealed class RecordingApplicationLifetimeService : IApplicationLifetimeService
    {
        private readonly CancellationTokenSource exitRequested = new();

        public ChildRuntimeExitCode? RequestedExitCode { get; private set; }

        public CancellationToken ExitRequested => exitRequested.Token;

        public bool TryExit(ChildRuntimeExitCode exitCode)
        {
            if (RequestedExitCode is not null)
            {
                return false;
            }

            RequestedExitCode = exitCode;
            exitRequested.Cancel();
            return true;
        }
    }

    private sealed class FakeKeyboardLayoutSwitcher(string activeLayoutId) : IKeyboardLayoutSwitcher
    {
        private static readonly IReadOnlyList<KeyboardLayout> Layouts = KeyboardLayoutCatalog.FromEntries(
        [
            new KeyboardLayoutRegistryEntry("0000040C", "KBDFR.DLL", "French (Legacy, AZERTY)", null),
            new KeyboardLayoutRegistryEntry("0000080C", "KBDBE.DLL", "Belgian French", null),
            new KeyboardLayoutRegistryEntry("00000413", "KBDNE.DLL", "Dutch", null)
        ]);

        private string activeLayoutId = activeLayoutId;

        public bool FailApply { get; init; }

        public List<string> AppliedLayoutIds { get; } = [];

        public IReadOnlyList<KeyboardLayout> GetLayouts() => Layouts;

        public string GetActiveLayoutId() => activeLayoutId;

        public KeyboardLayoutSwitchResult Apply(KeyboardLayout layout)
        {
            if (FailApply)
            {
                return new KeyboardLayoutSwitchResult(false, layout.Id, "Simulated failure.");
            }

            AppliedLayoutIds.Add(layout.Id);
            activeLayoutId = layout.Id;
            return new KeyboardLayoutSwitchResult(true, layout.Id, null);
        }

        public KeyboardLayoutSwitchResult? RestoreHandoff() => null;
    }

    private sealed class FakeThemeService : IThemeService
    {
        public ThemeMode CurrentTheme => ThemeMode.System;

        public void SetTheme(ThemeMode theme)
        {
        }
    }

    private sealed class FakeApplicationShellService : IApplicationShellService
    {
        public bool ConfirmWarning(string title, string message) => true;

        public void Shutdown()
        {
        }

        public void ShowAbout()
        {
        }
    }

    private sealed class FakeConnectConfigurationService(
        FoundryConnectConfigurationDocument configuration,
        BootMediaUpdateReason bootMediaUpdateReason = BootMediaUpdateReason.None) : IConnectConfigurationService
    {
        public string? ConfigurationPath => null;

        public bool IsLoadedFromDisk => false;

        public bool IsBootMediaUpdateRecommended => bootMediaUpdateReason != BootMediaUpdateReason.None;
        public BootMediaUpdateReason BootMediaUpdateReason => bootMediaUpdateReason;

        public ConnectConfigurationLoadResult Load()
        {
            return ConnectConfigurationLoadResult.Success(configuration, ConnectRuntimeSecrets.None(), bootMediaUpdateReason);
        }
    }

    private sealed class RecordingLogger<T> : ILogger<T>
    {
        public List<LogEntry> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            var properties = state is IEnumerable<KeyValuePair<string, object?>> values
                ? values.ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal)
                : new Dictionary<string, object?>(StringComparer.Ordinal);
            Entries.Add(new LogEntry(logLevel, properties));
        }
    }

    private sealed record LogEntry(LogLevel Level, IReadOnlyDictionary<string, object?> Properties);
}
