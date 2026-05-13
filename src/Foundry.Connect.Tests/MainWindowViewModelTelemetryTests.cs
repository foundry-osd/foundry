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
        Assert.True((bool)telemetryEvent.Properties["success"]!);
        Assert.Equal("ethernet", telemetryEvent.Properties["connection_type"]);
        Assert.Equal("ethernet_wifi", telemetryEvent.Properties["layout_mode"]);
        Assert.Equal("none", telemetryEvent.Properties["wifi_security"]);
        Assert.Equal("none", telemetryEvent.Properties["wifi_source"]);
        Assert.True((bool)telemetryEvent.Properties["wifi_provisioned"]!);
        Assert.True((bool)telemetryEvent.Properties["wired_dot1x_enabled"]!);
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

    private static MainWindowViewModel CreateViewModel(
        RecordingTelemetryService telemetryService,
        INetworkStatusService networkStatusService,
        RecordingApplicationLifetimeService? lifetimeService = null)
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
            new FakeNetworkBootstrapService(),
            networkStatusService,
            telemetryService,
            NullLogger<MainWindowViewModel>.Instance);
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
}
