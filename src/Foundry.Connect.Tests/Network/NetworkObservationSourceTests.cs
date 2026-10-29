// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.ComponentModel;
using System.Net.NetworkInformation;
using Foundry.Connect.Network;
using Foundry.Connect.Network.Native;
using Foundry.Connect.Tests.Support;
using Foundry.Utilities.Networking;
using Microsoft.Extensions.Logging;

namespace Foundry.Connect.Tests.Network;

public sealed class NetworkObservationSourceTests
{
    private static readonly Guid WifiInterface = Guid.Parse("cccccccc-0000-0000-0000-000000000001");
    private readonly ManualTimeProvider _clock = new();
    private readonly FakeWlanClient _wlan = new();
    private readonly RecordingLogger<NetworkObservationSource> _logger = new();
    private CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Observe_WhenWlanThrows_KeepsEthernetAndInternet()
    {
        _wlan.SnapshotException = new Win32Exception(1062);

        NetworkObservation observation = await CreateSource().ObserveAsync(false, Token);

        Assert.True(Assert.Single(observation.Ethernet).HasUsableAddress);
        Assert.Equal(InternetStatus.Online, observation.Internet);
        Assert.Equal(WifiAvailability.RuntimeMissing, observation.Wifi.Availability);
    }

    [Fact]
    public async Task Observe_WhenProbeThrows_IsNoInternetAndKeepsWifi()
    {
        _wlan.Snapshot = new WlanSnapshot(WifiAvailability.Available, [FakeWlanClient.Interface(WifiInterface, "Lab")], [FakeWlanClient.Visible(WifiInterface, "Lab")]);

        NetworkObservation observation = await CreateSource(probe: new StubProbe(new InvalidOperationException("boom"))).ObserveAsync(false, Token);

        Assert.Equal(InternetStatus.NoInternet, observation.Internet);
        Assert.Equal(WifiSsid.FromText("Lab"), observation.Wifi.Connection!.Ssid);
        Assert.Equal("Lab", Assert.Single(observation.Wifi.Networks).Ssid.DisplayName);
    }

    [Fact]
    public async Task Observe_WhenAdapterEnumerationThrows_KeepsWifiAndInternet()
    {
        _wlan.Snapshot = new WlanSnapshot(WifiAvailability.Available, [FakeWlanClient.Interface(WifiInterface)], [FakeWlanClient.Visible(WifiInterface, "Lab")]);

        NetworkObservation observation = await CreateSource(adapters: new StubAdapterProvider(new InvalidOperationException("boom"))).ObserveAsync(false, Token);

        Assert.Empty(observation.Ethernet);
        Assert.Equal(InternetStatus.Online, observation.Internet);
        Assert.Single(observation.Wifi.Networks);
    }

    [Fact]
    public async Task Observe_WhenWifiFeatureIsDisabled_DoesNotCallWlan()
    {
        NetworkObservation observation = await CreateSource(wifiEnabled: false).ObserveAsync(false, Token);

        Assert.Equal(WifiAvailability.Disabled, observation.Wifi.Availability);
        Assert.Equal(0, _wlan.SnapshotCalls);
    }

    [Fact]
    public async Task Observe_ReportsWifiInterfacesAndAvailability()
    {
        Guid other = Guid.Parse("cccccccc-0000-0000-0000-000000000002");
        _wlan.Snapshot = new WlanSnapshot(WifiAvailability.Available, [FakeWlanClient.Interface(WifiInterface), FakeWlanClient.Interface(other)], []);

        NetworkObservation observation = await CreateSource().ObserveAsync(false, Token);

        Assert.Equal(WifiAvailability.Available, observation.Wifi.Availability);
        Assert.Equal([WifiInterface, other], observation.Wifi.InterfaceIds);
        Assert.Null(observation.Wifi.Connection);
        Assert.Equal(_clock.GetUtcNow(), observation.ObservedAt);
    }

    [Fact]
    public async Task Observe_ReusesRecentNetworksWhenDiscoveryIsBrieflyEmpty()
    {
        NetworkObservationSource source = CreateSource();
        _wlan.Snapshot = new WlanSnapshot(WifiAvailability.Available, [FakeWlanClient.Interface(WifiInterface)], [FakeWlanClient.Visible(WifiInterface, "Lab")]);
        await source.ObserveAsync(false, Token);
        _wlan.Snapshot = new WlanSnapshot(WifiAvailability.Available, [FakeWlanClient.Interface(WifiInterface)], []);

        _clock.Advance(TimeSpan.FromSeconds(10));
        Assert.Single((await source.ObserveAsync(false, Token)).Wifi.Networks);
        _clock.Advance(TimeSpan.FromSeconds(10));
        Assert.Empty((await source.ObserveAsync(false, Token)).Wifi.Networks);
    }

    [Fact]
    public async Task Observe_DoesNotReuseNetworksWhenTheAdapterDisappears()
    {
        NetworkObservationSource source = CreateSource();
        _wlan.Snapshot = new WlanSnapshot(WifiAvailability.Available, [FakeWlanClient.Interface(WifiInterface)], [FakeWlanClient.Visible(WifiInterface, "Lab")]);
        await source.ObserveAsync(false, Token);
        _wlan.Snapshot = new WlanSnapshot(WifiAvailability.NoAdapter, [], []);

        _clock.Advance(TimeSpan.FromSeconds(1));

        Assert.Empty((await source.ObserveAsync(false, Token)).Wifi.Networks);
    }

    [Fact]
    public async Task Observe_ReadsWiredAuthenticationOnlyWhenRequested()
    {
        var wired = new StubWiredClient([new WiredInterfaceAuthentication("Ethernet", Dot1xAuthenticationStatus.Authenticated)]);
        NetworkObservationSource source = CreateSource(wired: wired);

        Assert.Null((await source.ObserveAsync(false, Token)).WiredAuthentication);
        Assert.Equal(Dot1xAuthenticationStatus.Authenticated, (await source.ObserveAsync(true, Token)).WiredAuthentication);
        Assert.Equal(1, wired.Calls);
    }

    [Fact]
    public async Task Observe_WhenWiredReadThrows_LeavesAuthenticationUnknown()
    {
        NetworkObservation observation = await CreateSource(wired: new StubWiredClient(new InvalidOperationException("boom"))).ObserveAsync(true, Token);

        Assert.Null(observation.WiredAuthentication);
        Assert.Equal(InternetStatus.Online, observation.Internet);
    }

    [Fact]
    public async Task Observe_LogsAPartFailureOnceUntilItRecovers()
    {
        NetworkObservationSource source = CreateSource();
        _wlan.SnapshotException = new Win32Exception(1062);
        await source.ObserveAsync(false, Token);
        await source.ObserveAsync(false, Token);
        _wlan.SnapshotException = null;
        _wlan.Snapshot = new WlanSnapshot(WifiAvailability.NoAdapter, [], []);
        await source.ObserveAsync(false, Token);

        Assert.Single(_logger.Entries, static entry => entry.Level == LogLevel.Warning);
        Assert.Single(_logger.Entries, static entry => entry.Level == LogLevel.Information && entry.Message.Contains("recovered", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Observe_LogsTheFailureTypeAtWarningAndTheExceptionOnlyAtDebug()
    {
        const string Identifying = "Lab-Guest-SSID";
        _wlan.SnapshotException = new Win32Exception(1062, Identifying);

        await CreateSource().ObserveAsync(false, Token);

        RecordedLogEntry warning = Assert.Single(_logger.Entries, static entry => entry.Level == LogLevel.Warning);
        Assert.Equal("wifi", warning.Properties["Part"]);
        Assert.Equal(nameof(Win32Exception), warning.Properties["FailureType"]);
        Assert.Null(warning.Exception);
        Assert.All(
            _logger.Entries.Where(static entry => entry.Level > LogLevel.Debug),
            entry =>
            {
                Assert.Null(entry.Exception);
                Assert.DoesNotContain(Identifying, entry.Message, StringComparison.Ordinal);
                Assert.DoesNotContain(entry.Properties.Values, value => value?.ToString()?.Contains(Identifying, StringComparison.Ordinal) == true);
            });
        Assert.Contains(_logger.Entries, static entry => entry.Level == LogLevel.Debug && entry.Exception is Win32Exception);
    }

    [Fact]
    public async Task Observe_WhenCallerCancels_Throws()
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => CreateSource(probe: new StubProbe(new OperationCanceledException(cancellation.Token))).ObserveAsync(false, cancellation.Token));
    }

    private NetworkObservationSource CreateSource(
        bool wifiEnabled = true,
        StubProbe? probe = null,
        StubWiredClient? wired = null,
        StubAdapterProvider? adapters = null) => new(
        adapters ?? new StubAdapterProvider(
        [
            new NetworkAdapterSnapshot("eth0", "Ethernet", NetworkInterfaceType.Ethernet, OperationalStatus.Up, "00-11-22-33-44-55",
                [new NetworkIpv4AddressSnapshot("192.0.2.10", "255.255.255.0")], ["192.0.2.1"], [], true)
        ]),
        _wlan,
        probe ?? new StubProbe(InternetStatus.Online),
        wired ?? new StubWiredClient([]),
        new ConnectNetworkOptions(wifiEnabled, TimeSpan.FromSeconds(10)),
        _clock,
        _logger);

    private sealed class StubAdapterProvider : INetworkAdapterSnapshotProvider
    {
        private readonly IReadOnlyList<NetworkAdapterSnapshot> _adapters = [];
        private readonly Exception? _exception;

        public StubAdapterProvider(IReadOnlyList<NetworkAdapterSnapshot> adapters) => _adapters = adapters;

        public StubAdapterProvider(Exception exception) => _exception = exception;

        public IReadOnlyList<NetworkAdapterSnapshot> GetAdapters() => _exception is null ? _adapters : throw _exception;
    }

    private sealed class StubProbe : IConnectivityProbe
    {
        private readonly InternetStatus _status;
        private readonly Exception? _exception;

        public StubProbe(InternetStatus status) => _status = status;

        public StubProbe(Exception exception) => _exception = exception;

        public Task<InternetStatus> CheckAsync(CancellationToken cancellationToken) =>
            _exception is null ? Task.FromResult(_status) : Task.FromException<InternetStatus>(_exception);
    }

    private sealed class StubWiredClient : IWiredDot1xClient
    {
        private readonly IReadOnlyList<WiredInterfaceAuthentication> _authentication = [];
        private readonly Exception? _exception;

        public StubWiredClient(IReadOnlyList<WiredInterfaceAuthentication> authentication) => _authentication = authentication;

        public StubWiredClient(Exception exception) => _exception = exception;

        public int Calls { get; private set; }

        public Task<WiredCommandResult> AddProfileAsync(string profilePath, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<WiredCommandResult> ReconnectAsync(string interfaceName, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<IReadOnlyList<WiredInterfaceAuthentication>> ReadAuthenticationAsync(CancellationToken cancellationToken)
        {
            Calls++;
            return _exception is null ? Task.FromResult(_authentication) : Task.FromException<IReadOnlyList<WiredInterfaceAuthentication>>(_exception);
        }
    }
}
