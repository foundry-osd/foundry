// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Connect.Models;
using Foundry.Connect.Models.Configuration;
using Foundry.Connect.Models.Network;
using Foundry.Connect.Services.ApplicationLifetime;
using Foundry.Connect.Services.ApplicationShell;
using Foundry.Connect.Services.Configuration;
using Foundry.Connect.Services.Localization;
using Foundry.Connect.Services.Network;
using Foundry.Connect.Services.Theme;
using Foundry.Connect.ViewModels;
using Foundry.Telemetry;
using System.Reflection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Foundry.Connect.Tests;

public sealed class MainWindowViewModelTelemetryTests
{
    [Fact]
    public async Task InitializeAsync_WhenNetworkIsReady_DoesNotTrackSessionReadyImmediately()
    {
        var telemetry = new RecordingTelemetryService();
        MainWindowViewModel viewModel = CreateViewModel(
            telemetry,
            new QueueNetworkStatusService(CreateReadySnapshot()));

        await viewModel.InitializeAsync();
        viewModel.Dispose();

        Assert.Empty(telemetry.Events);
    }

    [Fact]
    public async Task ContinueBootstrapCommand_WhenNetworkIsReady_TracksSessionReadyBeforeSuccessfulExit()
    {
        var telemetry = new RecordingTelemetryService();
        var lifetime = new RecordingApplicationLifetimeService(telemetry);
        MainWindowViewModel viewModel = CreateViewModel(
            telemetry,
            new QueueNetworkStatusService(CreateReadySnapshot()),
            lifetime);

        await viewModel.InitializeAsync();

        viewModel.ContinueBootstrapCommand.Execute(null);
        viewModel.Dispose();

        TelemetryEvent telemetryEvent = Assert.Single(telemetry.Events);
        Assert.Equal(TelemetryEvents.ConnectSessionReady, telemetryEvent.Name);
        Assert.Equal("ethernet", telemetryEvent.Properties["connect_network_connection_type"]);
        Assert.Equal("ethernet_wifi", telemetryEvent.Properties["connect_network_layout_mode"]);
        Assert.True((bool)telemetryEvent.Properties["connect_ethernet_available"]!);
        Assert.True((bool)telemetryEvent.Properties["connect_wifi_available"]!);
        Assert.Equal("none", telemetryEvent.Properties["connect_wifi_security_type"]);
        Assert.Equal("none", telemetryEvent.Properties["connect_wifi_source"]);
        Assert.True((bool)telemetryEvent.Properties["connect_wifi_provisioned"]!);
        Assert.True((bool)telemetryEvent.Properties["connect_wired_dot1x_enabled"]!);
        Assert.False(telemetryEvent.Properties.ContainsKey("success"));
        Assert.Equal(FoundryConnectExitCode.Success, lifetime.ExitCode);
        Assert.True(telemetry.CallsCompletedBeforeExit);
    }

    [Fact]
    public async Task ContinueBootstrapCommand_WhenExecutedTwice_TracksSessionReadyOnce()
    {
        var telemetry = new RecordingTelemetryService();
        var lifetime = new RecordingApplicationLifetimeService(telemetry);
        MainWindowViewModel viewModel = CreateViewModel(
            telemetry,
            new QueueNetworkStatusService(CreateReadySnapshot()),
            lifetime);

        await viewModel.InitializeAsync();

        viewModel.ContinueBootstrapCommand.Execute(null);
        viewModel.ContinueBootstrapCommand.Execute(null);
        viewModel.Dispose();

        Assert.Single(telemetry.Events);
    }

    [Fact]
    public async Task InitializeAsync_WhenProvisionedSettingsFail_LogsStructuredNetworkFailure()
    {
        var telemetry = new RecordingTelemetryService();
        var logger = new RecordingLogger<MainWindowViewModel>();
        MainWindowViewModel viewModel = CreateViewModel(
            telemetry,
            new QueueNetworkStatusService(CreateReadySnapshot()),
            networkBootstrapService: new ThrowingNetworkBootstrapService(),
            logger: logger);

        await viewModel.InitializeAsync();
        viewModel.Dispose();

        LogEntry entry = Assert.Single(logger.Entries, item => item.Level == LogLevel.Error);
        Assert.Equal("network.apply_provisioned_settings", entry.Properties["NetworkOperation"]);
        Assert.Equal("network", entry.Properties["FailureKind"]);
        Assert.Equal("http_status", entry.Properties["FailureReason"]);
        Assert.Equal("502", entry.Properties["FailureCode"]);
        Assert.Equal(true, entry.Properties["RemoteDiagnostic"]);
    }

    [Fact]
    public async Task InitializeAsync_WhenProvisionedSettingsReturnHandledFailure_LogsStructuredRemoteDiagnosticOnce()
    {
        var telemetry = new RecordingTelemetryService();
        var logger = new RecordingLogger<MainWindowViewModel>();
        MainWindowViewModel viewModel = CreateViewModel(
            telemetry,
            new QueueNetworkStatusService(CreateReadySnapshot()),
            networkBootstrapService: new ResultNetworkBootstrapService(NetworkBootstrapResult.Failed(
                "Wi-Fi profile import failed: simulated error",
                new NetworkBootstrapHandledFailure("network", "profile_import_failed", "wifi_profile_import_failed"))),
            logger: logger);

        await viewModel.InitializeAsync();
        viewModel.Dispose();

        LogEntry entry = Assert.Single(logger.Entries, item =>
            item.Level == LogLevel.Warning &&
            Equals(item.Properties["NetworkOperation"], "network.apply_provisioned_settings"));
        Assert.Equal("connect", entry.Properties["Workflow"]);
        Assert.False(string.IsNullOrWhiteSpace(Assert.IsType<string>(entry.Properties["OperationId"])));
        Assert.Equal("network", entry.Properties["FailureKind"]);
        Assert.Equal("profile_import_failed", entry.Properties["FailureReason"]);
        Assert.Equal("wifi_profile_import_failed", entry.Properties["FailureCode"]);
        Assert.Equal(true, entry.Properties["RemoteDiagnostic"]);
        Assert.Single(logger.Entries, item => item.Properties.ContainsKey("RemoteDiagnostic"));
    }

    [Fact]
    public async Task ConnectConfiguredWifiAsync_WhenHandledFailureIsReturned_LogsStructuredRemoteDiagnosticOnce()
    {
        var telemetry = new RecordingTelemetryService();
        var logger = new RecordingLogger<MainWindowViewModel>();
        MainWindowViewModel viewModel = CreateViewModel(
            telemetry,
            new QueueNetworkStatusService(CreateDisconnectedSnapshot(), CreateDisconnectedSnapshot()),
            networkBootstrapService: new ConnectConfiguredWifiResultNetworkBootstrapService(NetworkBootstrapResult.Failed(
                "Wi-Fi connection request failed: simulated error",
                new NetworkBootstrapHandledFailure("network", "connect_request_failed", "wifi_connect_request_failed"))),
            logger: logger);

        await viewModel.InitializeAsync();

        await InvokeNonPublicAsync(viewModel, "ConnectConfiguredWifiAsync");
        viewModel.Dispose();

        LogEntry entry = Assert.Single(logger.Entries, item =>
            item.Level == LogLevel.Warning &&
            Equals(item.Properties["NetworkOperation"], "wifi.provisioned_action"));
        Assert.Equal("connect", entry.Properties["Workflow"]);
        Assert.False(string.IsNullOrWhiteSpace(Assert.IsType<string>(entry.Properties["OperationId"])));
        Assert.Equal("network", entry.Properties["FailureKind"]);
        Assert.Equal("connect_request_failed", entry.Properties["FailureReason"]);
        Assert.Equal("wifi_connect_request_failed", entry.Properties["FailureCode"]);
        Assert.Equal(true, entry.Properties["RemoteDiagnostic"]);
        Assert.Single(logger.Entries, item => item.Properties.ContainsKey("RemoteDiagnostic"));
    }

    [Fact]
    public async Task InitializeAsync_WhenProvisionedSettingsReturnWiredHandledFailure_LogsStructuredWiredRemoteDiagnostic()
    {
        var telemetry = new RecordingTelemetryService();
        var logger = new RecordingLogger<MainWindowViewModel>();
        MainWindowViewModel viewModel = CreateViewModel(
            telemetry,
            new QueueNetworkStatusService(CreateReadySnapshot()),
            networkBootstrapService: new ResultNetworkBootstrapService(NetworkBootstrapResult.Failed(
                "Wired 802.1X is enabled, but no wired profile template was found.",
                new NetworkBootstrapHandledFailure("network", "profile_unavailable", "wired_profile_template_missing"))),
            logger: logger);

        await viewModel.InitializeAsync();
        viewModel.Dispose();

        LogEntry entry = Assert.Single(logger.Entries, item => item.Properties.ContainsKey("RemoteDiagnostic"));
        Assert.Equal("network.apply_provisioned_settings", entry.Properties["NetworkOperation"]);
        Assert.Equal("profile_unavailable", entry.Properties["FailureReason"]);
        Assert.Equal("wired_profile_template_missing", entry.Properties["FailureCode"]);
    }

    [Fact]
    public async Task InitializeAsync_WhenProvisionedSettingsReturnMixedHandledFailures_LogsOneRemoteDiagnosticPerFailure()
    {
        var telemetry = new RecordingTelemetryService();
        var logger = new RecordingLogger<MainWindowViewModel>();
        MainWindowViewModel viewModel = CreateViewModel(
            telemetry,
            new QueueNetworkStatusService(CreateReadySnapshot()),
            networkBootstrapService: new ResultNetworkBootstrapService(new NetworkBootstrapResult(
                "Wired and Wi-Fi bootstrap finished with handled failures.",
                [
                    new NetworkBootstrapHandledFailure("network", "profile_unavailable", "wired_profile_template_missing"),
                    new NetworkBootstrapHandledFailure("network", "missing_adapter", "no_wireless_adapter")
                ])),
            logger: logger);

        await viewModel.InitializeAsync();
        viewModel.Dispose();

        LogEntry[] entries = logger.Entries
            .Where(item => item.Properties.ContainsKey("RemoteDiagnostic"))
            .ToArray();
        Assert.Equal(2, entries.Length);
        Assert.All(entries, entry => Assert.Equal("network.apply_provisioned_settings", entry.Properties["NetworkOperation"]));
        string operationId = Assert.IsType<string>(entries[0].Properties["OperationId"]);
        Assert.All(entries, entry => Assert.Equal(operationId, entry.Properties["OperationId"]));
        Assert.Contains(entries, entry => Equals(entry.Properties["FailureCode"], "wired_profile_template_missing"));
        Assert.Contains(entries, entry => Equals(entry.Properties["FailureCode"], "no_wireless_adapter"));
    }

    [Fact]
    public async Task InitializeAsync_WhenProvisionedSettingsAreCancelled_DoesNotLogRemoteDiagnosticFailure()
    {
        var telemetry = new RecordingTelemetryService();
        var logger = new RecordingLogger<MainWindowViewModel>();
        MainWindowViewModel viewModel = CreateViewModel(
            telemetry,
            new QueueNetworkStatusService(CreateReadySnapshot()),
            networkBootstrapService: new CancellingNetworkBootstrapService(),
            logger: logger);

        await viewModel.InitializeAsync();
        viewModel.Dispose();

        Assert.DoesNotContain(logger.Entries, item => item.Properties.ContainsKey("RemoteDiagnostic"));
    }

    private static MainWindowViewModel CreateViewModel(
        RecordingTelemetryService telemetryService,
        INetworkStatusService networkStatusService,
        RecordingApplicationLifetimeService? lifetimeService = null,
        INetworkBootstrapService? networkBootstrapService = null,
        ILogger<MainWindowViewModel>? logger = null)
    {
        lifetimeService ??= new RecordingApplicationLifetimeService(telemetryService);
        var configuration = new FoundryConnectConfiguration
        {
            Capabilities = new NetworkCapabilitiesOptions { WifiProvisioned = true },
            Wifi = new WifiSettings
            {
                IsEnabled = true,
                SecurityType = "WPA2-Personal",
                Ssid = "Foundry"
            },
            Dot1x = new Dot1xSettings { IsEnabled = true }
        };

        return new MainWindowViewModel(
            new FakeThemeService(),
            new LocalizationService(),
            new FakeApplicationShellService(),
            lifetimeService,
            new FakeConnectConfigurationService(configuration),
            configuration,
            networkBootstrapService ?? new FakeNetworkBootstrapService(),
            networkStatusService,
            telemetryService,
            logger ?? NullLogger<MainWindowViewModel>.Instance);
    }

    private static NetworkStatusSnapshot CreateReadySnapshot()
    {
        return new NetworkStatusSnapshot
        {
            LayoutMode = NetworkLayoutMode.EthernetWifi,
            HasInternetAccess = true,
            HasEthernetAdapter = true,
            IsEthernetConnected = true,
            HasEthernetIpv4 = true,
            HasDhcpLease = true,
            IsWifiRuntimeAvailable = true,
            HasWirelessAdapter = true,
            EthernetStatusText = "Connected",
            WifiNetworks =
            [
                new WifiNetworkSummary
                {
                    Ssid = "Foundry",
                    Authentication = "WPA2-Personal",
                    Encryption = "AES",
                    SignalStrengthPercent = 100
                }
            ]
        };
    }

    private static NetworkStatusSnapshot CreateDisconnectedSnapshot()
    {
        return new NetworkStatusSnapshot
        {
            LayoutMode = NetworkLayoutMode.EthernetWifi,
            HasInternetAccess = false,
            HasEthernetAdapter = true,
            IsEthernetConnected = false,
            HasEthernetIpv4 = false,
            HasDhcpLease = false,
            IsWifiRuntimeAvailable = true,
            HasWirelessAdapter = true,
            EthernetStatusText = "Disconnected",
            WifiNetworks =
            [
                new WifiNetworkSummary
                {
                    Ssid = "Foundry",
                    Authentication = "WPA2-Personal",
                    Encryption = "AES",
                    SignalStrengthPercent = 100
                }
            ]
        };
    }

    private static async Task InvokeNonPublicAsync(object instance, string methodName)
    {
        MethodInfo method = instance.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException($"Method '{methodName}' was not found.");
        Task task = Assert.IsAssignableFrom<Task>(method.Invoke(instance, null));
        await task;
    }

    private sealed class RecordingTelemetryService : ITelemetryService
    {
        public List<TelemetryEvent> Events { get; } = [];

        public bool CallsCompletedBeforeExit { get; private set; }

        public bool HasExitHappened { get; set; }

        public Task TrackAsync(string eventName, IReadOnlyDictionary<string, object?> properties, CancellationToken cancellationToken = default)
        {
            Events.Add(new TelemetryEvent(eventName, new Dictionary<string, object?>(properties)));
            CallsCompletedBeforeExit = !HasExitHappened;
            return Task.CompletedTask;
        }

        public Task FlushAsync(CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingApplicationLifetimeService : IApplicationLifetimeService
    {
        private readonly RecordingTelemetryService? telemetryService;

        public RecordingApplicationLifetimeService()
        {
        }

        public RecordingApplicationLifetimeService(RecordingTelemetryService telemetryService)
        {
            this.telemetryService = telemetryService;
        }

        public bool IsExitRequested { get; private set; }

        public FoundryConnectExitCode ExitCode { get; private set; }

        public void Exit(FoundryConnectExitCode exitCode)
        {
            telemetryService?.HasExitHappened = true;
            ExitCode = exitCode;
            IsExitRequested = true;
        }
    }

    private sealed class QueueNetworkStatusService(params NetworkStatusSnapshot[] snapshots) : INetworkStatusService
    {
        private readonly Queue<NetworkStatusSnapshot> snapshots = new(snapshots);

        public Task<NetworkStatusSnapshot> GetSnapshotAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(snapshots.Count > 1 ? snapshots.Dequeue() : snapshots.Peek());
        }
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
        public void ShowAbout()
        {
        }
    }

    private sealed class FakeConnectConfigurationService(FoundryConnectConfiguration configuration) : IConnectConfigurationService
    {
        public string? ConfigurationPath => null;

        public bool IsLoadedFromDisk => false;

        public bool IsBootMediaUpdateRecommended => false;

        public FoundryConnectConfiguration Load()
        {
            return configuration;
        }
    }

    private sealed class FakeNetworkBootstrapService : INetworkBootstrapService
    {
        public Task<NetworkBootstrapResult> ApplyProvisionedSettingsAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(NetworkBootstrapResult.Success(string.Empty));
        }

        public Task<NetworkBootstrapResult> ConnectConfiguredWifiAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(NetworkBootstrapResult.Success(string.Empty));
        }

        public Task<NetworkBootstrapResult> ConnectWifiNetworkAsync(
            string ssid,
            string? ssidHex,
            string authentication,
            string? passphrase,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(NetworkBootstrapResult.Success(string.Empty));
        }

        public Task<NetworkBootstrapResult> DisconnectWifiAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(NetworkBootstrapResult.Success(string.Empty));
        }
    }

    private sealed class ThrowingNetworkBootstrapService : INetworkBootstrapService
    {
        public Task<NetworkBootstrapResult> ApplyProvisionedSettingsAsync(CancellationToken cancellationToken) =>
            Task.FromException<NetworkBootstrapResult>(new HttpRequestException("Simulated failure.", null, System.Net.HttpStatusCode.BadGateway));

        public Task<NetworkBootstrapResult> ConnectConfiguredWifiAsync(CancellationToken cancellationToken) => Task.FromResult(NetworkBootstrapResult.Success(string.Empty));

        public Task<NetworkBootstrapResult> ConnectWifiNetworkAsync(
            string ssid,
            string? ssidHex,
            string authentication,
            string? passphrase,
            CancellationToken cancellationToken) => Task.FromResult(NetworkBootstrapResult.Success(string.Empty));

        public Task<NetworkBootstrapResult> DisconnectWifiAsync(CancellationToken cancellationToken) => Task.FromResult(NetworkBootstrapResult.Success(string.Empty));
    }

    private sealed class ResultNetworkBootstrapService(NetworkBootstrapResult applyProvisionedSettingsResult) : INetworkBootstrapService
    {
        public Task<NetworkBootstrapResult> ApplyProvisionedSettingsAsync(CancellationToken cancellationToken) =>
            Task.FromResult(applyProvisionedSettingsResult);

        public Task<NetworkBootstrapResult> ConnectConfiguredWifiAsync(CancellationToken cancellationToken) => Task.FromResult(NetworkBootstrapResult.Success(string.Empty));

        public Task<NetworkBootstrapResult> ConnectWifiNetworkAsync(
            string ssid,
            string? ssidHex,
            string authentication,
            string? passphrase,
            CancellationToken cancellationToken) => Task.FromResult(NetworkBootstrapResult.Success(string.Empty));

        public Task<NetworkBootstrapResult> DisconnectWifiAsync(CancellationToken cancellationToken) => Task.FromResult(NetworkBootstrapResult.Success(string.Empty));
    }

    private sealed class ConnectConfiguredWifiResultNetworkBootstrapService(NetworkBootstrapResult connectConfiguredWifiResult) : INetworkBootstrapService
    {
        public Task<NetworkBootstrapResult> ApplyProvisionedSettingsAsync(CancellationToken cancellationToken) => Task.FromResult(NetworkBootstrapResult.Success(string.Empty));

        public Task<NetworkBootstrapResult> ConnectConfiguredWifiAsync(CancellationToken cancellationToken) =>
            Task.FromResult(connectConfiguredWifiResult);

        public Task<NetworkBootstrapResult> ConnectWifiNetworkAsync(
            string ssid,
            string? ssidHex,
            string authentication,
            string? passphrase,
            CancellationToken cancellationToken) => Task.FromResult(NetworkBootstrapResult.Success(string.Empty));

        public Task<NetworkBootstrapResult> DisconnectWifiAsync(CancellationToken cancellationToken) => Task.FromResult(NetworkBootstrapResult.Success(string.Empty));
    }

    private sealed class CancellingNetworkBootstrapService : INetworkBootstrapService
    {
        public Task<NetworkBootstrapResult> ApplyProvisionedSettingsAsync(CancellationToken cancellationToken) =>
            Task.FromCanceled<NetworkBootstrapResult>(cancellationToken.IsCancellationRequested
                ? cancellationToken
                : new CancellationToken(canceled: true));

        public Task<NetworkBootstrapResult> ConnectConfiguredWifiAsync(CancellationToken cancellationToken) => Task.FromResult(NetworkBootstrapResult.Success(string.Empty));

        public Task<NetworkBootstrapResult> ConnectWifiNetworkAsync(
            string ssid,
            string? ssidHex,
            string authentication,
            string? passphrase,
            CancellationToken cancellationToken) => Task.FromResult(NetworkBootstrapResult.Success(string.Empty));

        public Task<NetworkBootstrapResult> DisconnectWifiAsync(CancellationToken cancellationToken) => Task.FromResult(NetworkBootstrapResult.Success(string.Empty));
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
