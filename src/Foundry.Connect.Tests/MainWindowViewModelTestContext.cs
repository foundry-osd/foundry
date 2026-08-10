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
using Foundry.Connect.Services.Readiness;
using Foundry.Connect.Services.Theme;
using Foundry.Connect.ViewModels;
using Foundry.Telemetry;
using Microsoft.Extensions.Logging.Abstractions;

namespace Foundry.Connect.Tests;

internal sealed class MainWindowViewModelTestContext
{
    public MainWindowViewModelTestContext(
        FoundryConnectConfiguration? configuration = null,
        INetworkStatusService? networkStatusService = null)
    {
        Configuration = configuration ?? CreateConfiguration();
        NetworkStatusService = networkStatusService ?? new QueueNetworkStatusService(CreateSnapshot());
        ViewModel = new MainWindowViewModel(
            ThemeService,
            LocalizationService,
            ShellService,
            LifetimeService,
            new FakeConnectConfigurationService(Configuration),
            Configuration,
            BootstrapService,
            NetworkStatusService,
            new ConnectReadinessEvaluator(),
            TelemetryService,
            NullLogger<MainWindowViewModel>.Instance);
    }

    public FoundryConnectConfiguration Configuration { get; }

    public RecordingApplicationLifetimeService LifetimeService { get; } = new();

    public RecordingApplicationShellService ShellService { get; } = new();

    public RecordingNetworkBootstrapService BootstrapService { get; } = new();

    public RecordingTelemetryService TelemetryService { get; } = new();

    public FakeThemeService ThemeService { get; } = new();

    public LocalizationService LocalizationService { get; } = new();

    public INetworkStatusService NetworkStatusService { get; }

    public MainWindowViewModel ViewModel { get; }

    public static FoundryConnectConfiguration CreateConfiguration(bool wifiEnabled = true)
    {
        return new FoundryConnectConfiguration
        {
            Capabilities = new NetworkCapabilitiesOptions { WifiProvisioned = wifiEnabled },
            Wifi = new WifiSettings
            {
                IsEnabled = wifiEnabled,
                SecurityType = "WPA2-Personal",
                Ssid = "Foundry"
            }
        };
    }

    public static NetworkStatusSnapshot CreateSnapshot(
        bool hasInternetAccess = false,
        NetworkLayoutMode layoutMode = NetworkLayoutMode.EthernetWifi,
        bool wifiRuntimeAvailable = true,
        bool hasWirelessAdapter = true,
        string? connectedWifiSsid = null,
        IReadOnlyList<WifiNetworkSummary>? wifiNetworks = null)
    {
        return new NetworkStatusSnapshot
        {
            LayoutMode = layoutMode,
            HasInternetAccess = hasInternetAccess,
            HasEthernetAdapter = true,
            IsEthernetConnected = true,
            HasDhcpLease = true,
            HasEthernetIpv4 = true,
            IsWifiRuntimeAvailable = wifiRuntimeAvailable,
            HasWirelessAdapter = hasWirelessAdapter,
            EthernetStatusText = "Connected",
            EthernetAdapterName = "Ethernet",
            EthernetIpAddress = "192.0.2.10",
            EthernetGateway = "192.0.2.1",
            ConnectedWifiSsid = connectedWifiSsid,
            WifiNetworks = wifiNetworks ?? []
        };
    }

    internal sealed class QueueNetworkStatusService(params object[] results) : INetworkStatusService
    {
        private readonly Queue<object> _results = new(results);

        public Task<NetworkStatusSnapshot> GetSnapshotAsync(CancellationToken cancellationToken)
        {
            object result = _results.Count > 1 ? _results.Dequeue() : _results.Peek();
            return result switch
            {
                NetworkStatusSnapshot snapshot => Task.FromResult(snapshot),
                Exception exception => Task.FromException<NetworkStatusSnapshot>(exception),
                _ => throw new InvalidOperationException("Unsupported test result.")
            };
        }
    }

    internal sealed class RecordingNetworkBootstrapService : INetworkBootstrapService
    {
        public int ApplyCalls { get; private set; }

        public int ConnectConfiguredCalls { get; private set; }

        public int ConnectSelectedCalls { get; private set; }

        public int DisconnectCalls { get; private set; }

        public string ApplyResult { get; set; } = string.Empty;

        public string ConnectConfiguredResult { get; set; } = string.Empty;

        public string ConnectSelectedResult { get; set; } = string.Empty;

        public string DisconnectResult { get; set; } = string.Empty;

        public Task<string> ApplyProvisionedSettingsAsync(CancellationToken cancellationToken)
        {
            ApplyCalls++;
            return Task.FromResult(ApplyResult);
        }

        public Task<string> ConnectConfiguredWifiAsync(CancellationToken cancellationToken)
        {
            ConnectConfiguredCalls++;
            return Task.FromResult(ConnectConfiguredResult);
        }

        public Task<string> ConnectWifiNetworkAsync(string ssid, string? ssidHex, string authentication, string? passphrase, CancellationToken cancellationToken)
        {
            ConnectSelectedCalls++;
            return Task.FromResult(ConnectSelectedResult);
        }

        public Task<string> DisconnectWifiAsync(CancellationToken cancellationToken)
        {
            DisconnectCalls++;
            return Task.FromResult(DisconnectResult);
        }
    }

    internal sealed class RecordingApplicationLifetimeService : IApplicationLifetimeService
    {
        public bool IsExitRequested { get; private set; }

        public FoundryConnectExitCode ExitCode { get; private set; }

        public int ExitCalls { get; private set; }

        public void Exit(FoundryConnectExitCode exitCode)
        {
            ExitCalls++;
            IsExitRequested = true;
            ExitCode = exitCode;
        }
    }

    internal sealed class RecordingApplicationShellService : IApplicationShellService
    {
        public int ShowAboutCalls { get; private set; }

        public void ShowAbout()
        {
            ShowAboutCalls++;
        }
    }

    internal sealed class RecordingTelemetryService : ITelemetryService
    {
        public List<string> Events { get; } = [];

        public Task TrackAsync(string eventName, IReadOnlyDictionary<string, object?> properties, CancellationToken cancellationToken = default)
        {
            Events.Add(eventName);
            return Task.CompletedTask;
        }

        public Task FlushAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    internal sealed class FakeThemeService : IThemeService
    {
        public ThemeMode CurrentTheme { get; private set; }

        public void SetTheme(ThemeMode theme)
        {
            CurrentTheme = theme;
        }
    }

    private sealed class FakeConnectConfigurationService(FoundryConnectConfiguration configuration) : IConnectConfigurationService
    {
        public string? ConfigurationPath => null;

        public bool IsLoadedFromDisk => false;

        public bool IsBootMediaUpdateRecommended => false;

        public FoundryConnectConfiguration Load() => configuration;
    }
}
