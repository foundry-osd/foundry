// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Connect.Network;
using Foundry.Connect.Services.Network;
using Foundry.Connect.Tests.Support;
using Foundry.Core.Models.Configuration;
using Microsoft.Extensions.Logging;

namespace Foundry.Connect.Tests.Network;

public sealed class RoamingCaptureTests
{
    private const string UnprotectedProfile = """
        <WLANProfile xmlns="http://www.microsoft.com/networking/WLAN/profile/v1"><name>Guest</name>
        <MSM><security><sharedKey><keyType>passPhrase</keyType><protected>false</protected><keyMaterial>guest passphrase</keyMaterial></sharedKey></security></MSM></WLANProfile>
        """;
    private const string OpenProfile = """
        <WLANProfile xmlns="http://www.microsoft.com/networking/WLAN/profile/v1"><name>Cafe</name>
        <MSM><security><authEncryption><authentication>open</authentication><encryption>none</encryption></authEncryption></security></MSM></WLANProfile>
        """;
    private static readonly Guid WifiInterface = Guid.Parse("dddddddd-0000-0000-0000-000000000001");
    private static readonly DateTimeOffset Now = new(2026, 10, 29, 8, 0, 0, TimeSpan.Zero);
    private readonly FakeWlanClient _wlan = new();
    private readonly RecordingRoamingService _roaming = new();
    private readonly StubHistory _history = new();
    private readonly RecordingLogger<RoamingCapture> _logger = new();

    [Fact]
    public async Task Capture_ManualWifiOnWifiPath_WritesProfileReadBackFromWlan()
    {
        _wlan.Profiles[(WifiInterface, "Guest")] = UnprotectedProfile;
        _history.Add("Guest", WifiConnectionSource.Manual, minutesAgo: 1);

        await Create(RoamingConfiguration()).CaptureAsync(WifiPathState("Guest", WifiConnectionSource.Manual), TestContext.Current.CancellationToken);

        (bool isWifi, NetworkProfileRoamingCaptureRequest request) = Assert.Single(_roaming.Requests);
        Assert.True(isWifi);
        Assert.Null(request.ProfilePath);
        Assert.Equal(UnprotectedProfile, request.ProfileXml);
        Assert.Equal(NetworkProfileRoamingProfileSource.ManualWifi, request.Source);
        Assert.Equal(NetworkProfileRoamingConnectivityExpectation.PreOobeConnectable, request.ConnectivityExpectation);
    }

    [Fact]
    public async Task Capture_SeveralJoinedNetworks_WritesThemInOneCallWithTheConnectedOneFirst()
    {
        _wlan.Profiles[(WifiInterface, "Guest")] = UnprotectedProfile;
        _wlan.Profiles[(WifiInterface, "Cafe")] = OpenProfile;
        _history.Add("Guest", WifiConnectionSource.Manual, minutesAgo: 10);
        _history.Add("Cafe", WifiConnectionSource.Manual, minutesAgo: 1);

        await Create(RoamingConfiguration()).CaptureAsync(WifiPathState("Guest", WifiConnectionSource.Manual), TestContext.Current.CancellationToken);

        IReadOnlyList<NetworkProfileRoamingCaptureRequest> batch = Assert.Single(_roaming.WifiBatches);
        Assert.Equal([UnprotectedProfile, OpenProfile], batch.Select(static request => request.ProfileXml));
    }

    [Fact]
    public async Task Capture_WhenKeyIsStillProtected_SkipsWithWarning()
    {
        _wlan.Profiles[(WifiInterface, "Guest")] = UnprotectedProfile.Replace("<protected>false</protected>", "<protected>true</protected>", StringComparison.Ordinal);
        _history.Add("Guest", WifiConnectionSource.Manual, minutesAgo: 1);

        await Create(RoamingConfiguration()).CaptureAsync(WifiPathState("Guest", WifiConnectionSource.Manual), TestContext.Current.CancellationToken);

        Assert.Empty(_roaming.Requests);
        Assert.Single(_logger.Entries, static entry => entry.Level == LogLevel.Warning);
    }

    [Fact]
    public async Task Capture_WhenOneProfileCannotBeRead_StillWritesTheOthers()
    {
        _wlan.Profiles[(WifiInterface, "Cafe")] = OpenProfile;
        _history.Add("Guest", WifiConnectionSource.Manual, minutesAgo: 1);
        _history.Add("Cafe", WifiConnectionSource.Manual, minutesAgo: 5);

        await Create(RoamingConfiguration()).CaptureAsync(WifiPathState("Guest", WifiConnectionSource.Manual), TestContext.Current.CancellationToken);

        Assert.Equal(OpenProfile, Assert.Single(_roaming.Requests).Request.ProfileXml);
        Assert.Single(_logger.Entries, static entry => entry.Level == LogLevel.Warning);
    }

    [Fact]
    public async Task Capture_NeverLogsTheSsidOrProfileName()
    {
        _wlan.Profiles[(WifiInterface, "Guest")] = UnprotectedProfile.Replace("<protected>false</protected>", "<protected>true</protected>", StringComparison.Ordinal);
        _history.Add("Guest", WifiConnectionSource.Manual, minutesAgo: 1);
        _history.Add("Cafe", WifiConnectionSource.Manual, minutesAgo: 5);
        _roaming.Failure = new IOException("disk full");

        await Create(RoamingConfiguration()).CaptureAsync(WifiPathState("Guest", WifiConnectionSource.Manual), TestContext.Current.CancellationToken);

        Assert.NotEmpty(_logger.Entries);
        Assert.All(_logger.Entries, static entry =>
        {
            string text = entry.Message + " " + string.Join(' ', entry.Properties.Values.Select(static value => Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture)));
            Assert.DoesNotContain("Guest", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Cafe", text, StringComparison.OrdinalIgnoreCase);
        });
    }

    [Fact]
    public async Task Capture_WhenWifiRoamingIsDisabled_WritesNothing()
    {
        _wlan.Profiles[(WifiInterface, "Guest")] = UnprotectedProfile;
        _history.Add("Guest", WifiConnectionSource.Manual, minutesAgo: 1);
        FoundryConnectConfigurationDocument configuration = RoamingConfiguration() with
        {
            Network = new ConnectNetworkSettings { ProfileRoaming = new ConnectNetworkProfileRoamingSettings() }
        };

        await Create(configuration).CaptureAsync(WifiPathState("Guest", WifiConnectionSource.Manual), TestContext.Current.CancellationToken);

        Assert.Empty(_roaming.Requests);
    }

    [Fact]
    public async Task Capture_ProvisionedEnterprise_UsesTemplateAndCertificate()
    {
        using var directory = new TemporaryDirectory();
        string template = directory.CreateFile("Network\\corp.xml", "<WLANProfile/>");
        string certificate = directory.CreateFile("Network\\root.cer", [1, 2, 3]);
        _history.Add("Corp", WifiConnectionSource.Provisioned, minutesAgo: 1);
        FoundryConnectConfigurationDocument configuration = RoamingConfiguration() with
        {
            Capabilities = new ConnectNetworkCapabilitiesSettings { WifiProvisioned = true },
            Wifi = new ConnectWifiSettings
            {
                IsEnabled = true,
                HasEnterpriseProfile = true,
                EnterpriseProfileTemplatePath = "Network\\corp.xml",
                EnterpriseAuthenticationMode = NetworkAuthenticationMode.MachineOnly,
                CertificatePath = "Network\\root.cer"
            }
        };

        await Create(configuration, directory).CaptureAsync(WifiPathState("Corp", WifiConnectionSource.Provisioned), TestContext.Current.CancellationToken);

        NetworkProfileRoamingCaptureRequest request = Assert.Single(_roaming.Requests).Request;
        Assert.Equal(template, request.ProfilePath);
        Assert.Null(request.ProfileXml);
        Assert.Equal([certificate], request.CertificatePaths);
        Assert.Equal(NetworkProfileRoamingProfileSource.ProvisionedWifi, request.Source);
        Assert.Equal(NetworkProfileRoamingConnectivityExpectation.DependsOnMachineCredential, request.ConnectivityExpectation);
    }

    [Fact]
    public async Task Capture_ProvisionedPersonal_ReadsTheProfileBack()
    {
        _wlan.Profiles[(WifiInterface, "Guest")] = UnprotectedProfile;
        _history.Add("Guest", WifiConnectionSource.Provisioned, minutesAgo: 1);

        await Create(RoamingConfiguration()).CaptureAsync(WifiPathState("Guest", WifiConnectionSource.Provisioned), TestContext.Current.CancellationToken);

        NetworkProfileRoamingCaptureRequest request = Assert.Single(_roaming.Requests).Request;
        Assert.Equal(UnprotectedProfile, request.ProfileXml);
        Assert.Equal(NetworkProfileRoamingProfileSource.ProvisionedWifi, request.Source);
        Assert.Equal(NetworkProfileRoamingConnectivityExpectation.PreOobeConnectable, request.ConnectivityExpectation);
    }

    [Fact]
    public async Task Capture_WiredDot1x_WhenProfileWasApplied()
    {
        using var directory = new TemporaryDirectory();
        string template = directory.CreateFile("Network\\wired.xml", "<LANProfile/>");
        FoundryConnectConfigurationDocument configuration = RoamingConfiguration() with
        {
            Dot1x = new ConnectDot1xSettings { IsEnabled = true, ProfileTemplatePath = "Network\\wired.xml", AuthenticationMode = NetworkAuthenticationMode.MachineOnly }
        };
        NetworkState state = NetworkState.Initial(Now) with
        {
            Internet = InternetStatus.Online,
            Ethernet = new EthernetState([new EthernetInterfaceState("eth0", "Ethernet", true, IpConfigurationKind.Dhcp, "192.0.2.10", "192.0.2.1")],
                new Dot1xState(Dot1xAuthenticationStatus.Authenticated))
        };

        await Create(configuration, directory).CaptureAsync(state, TestContext.Current.CancellationToken);

        (bool isWifi, NetworkProfileRoamingCaptureRequest request) = Assert.Single(_roaming.Requests);
        Assert.False(isWifi);
        Assert.Equal(template, request.ProfilePath);
        Assert.Equal(NetworkProfileRoamingProfileSource.ProvisionedWiredDot1x, request.Source);
        Assert.Equal(NetworkProfileRoamingConnectivityExpectation.DependsOnMachineCredential, request.ConnectivityExpectation);
    }

    [Fact]
    public async Task Capture_WhenRoamingServiceThrows_LogsWarningAndReturns()
    {
        _wlan.Profiles[(WifiInterface, "Guest")] = UnprotectedProfile;
        _history.Add("Guest", WifiConnectionSource.Manual, minutesAgo: 1);
        _roaming.Failure = new IOException("disk full");

        await Create(RoamingConfiguration()).CaptureAsync(WifiPathState("Guest", WifiConnectionSource.Manual), TestContext.Current.CancellationToken);

        RecordedLogEntry warning = Assert.Single(_logger.Entries, static entry => entry.Level == LogLevel.Warning);
        Assert.Equal("Wifi", warning.Properties["ProfileKind"]);
        Assert.Equal(nameof(IOException), warning.Properties["FailureType"]);
    }

    private RoamingCapture Create(FoundryConnectConfigurationDocument configuration, TemporaryDirectory? directory = null) => new(
        configuration,
        directory is null ? null : Path.Combine(directory.Path, "foundry.connect.config.json"),
        _history,
        _wlan,
        _roaming,
        _logger);

    private static FoundryConnectConfigurationDocument RoamingConfiguration() => new()
    {
        Network = new ConnectNetworkSettings
        {
            ProfileRoaming = new ConnectNetworkProfileRoamingSettings
            {
                Wifi = new NetworkProfileRoamingTransportSettings { IsEnabled = true },
                WiredDot1x = new NetworkProfileRoamingTransportSettings { IsEnabled = true }
            }
        }
    };

    private static NetworkState WifiPathState(string ssid, WifiConnectionSource source) => NetworkState.Initial(Now) with
    {
        Internet = InternetStatus.Online,
        Wifi = WifiState.Initial with
        {
            Availability = WifiAvailability.Available,
            Connection = new WifiConnection(WifiInterface, WifiSsid.FromText(ssid), ssid, WifiSecurityType.Wpa2Personal, source)
        }
    };

    private sealed class StubHistory : IWifiConnectionHistory
    {
        private readonly List<SuccessfulWifiConnection> _connections = [];

        public IReadOnlyList<SuccessfulWifiConnection> SuccessfulWifiConnections => _connections;

        public void Add(string name, WifiConnectionSource source, int minutesAgo) =>
            _connections.Add(new SuccessfulWifiConnection(WifiInterface, name, WifiSsid.FromText(name), source, Now.AddMinutes(-minutesAgo)));
    }

    private sealed class RecordingRoamingService : INetworkProfileRoamingService
    {
        public List<(bool IsWifi, NetworkProfileRoamingCaptureRequest Request)> Requests { get; } = [];

        public List<IReadOnlyList<NetworkProfileRoamingCaptureRequest>> WifiBatches { get; } = [];

        public Exception? Failure { get; set; }

        public Task CaptureWifiProfileAsync(NetworkProfileRoamingCaptureRequest request, CancellationToken cancellationToken) =>
            CaptureWifiProfilesAsync([request], cancellationToken);

        public Task CaptureWifiProfilesAsync(IReadOnlyList<NetworkProfileRoamingCaptureRequest> requests, CancellationToken cancellationToken)
        {
            ThrowIfFailing();
            WifiBatches.Add(requests);
            Requests.AddRange(requests.Select(static request => (true, request)));
            return Task.CompletedTask;
        }

        public Task CaptureWiredDot1xProfileAsync(NetworkProfileRoamingCaptureRequest request, CancellationToken cancellationToken)
        {
            ThrowIfFailing();
            Requests.Add((false, request));
            return Task.CompletedTask;
        }

        private void ThrowIfFailing()
        {
            if (Failure is not null)
            {
                throw Failure;
            }
        }
    }
}
