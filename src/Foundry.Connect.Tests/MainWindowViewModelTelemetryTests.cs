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
        public Task<string> ApplyProvisionedSettingsAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(string.Empty);
        }

        public Task<string> ConnectConfiguredWifiAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(string.Empty);
        }

        public Task<string> ConnectWifiNetworkAsync(
            string ssid,
            string? ssidHex,
            string authentication,
            string? passphrase,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(string.Empty);
        }

        public Task<string> DisconnectWifiAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(string.Empty);
        }
    }

    private sealed class ThrowingNetworkBootstrapService : INetworkBootstrapService
    {
        public Task<string> ApplyProvisionedSettingsAsync(CancellationToken cancellationToken) =>
            Task.FromException<string>(new HttpRequestException("Simulated failure.", null, System.Net.HttpStatusCode.BadGateway));

        public Task<string> ConnectConfiguredWifiAsync(CancellationToken cancellationToken) => Task.FromResult(string.Empty);

        public Task<string> ConnectWifiNetworkAsync(
            string ssid,
            string? ssidHex,
            string authentication,
            string? passphrase,
            CancellationToken cancellationToken) => Task.FromResult(string.Empty);

        public Task<string> DisconnectWifiAsync(CancellationToken cancellationToken) => Task.FromResult(string.Empty);
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
