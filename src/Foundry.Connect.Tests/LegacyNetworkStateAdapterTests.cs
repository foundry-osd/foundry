// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Connect.Models;
using Foundry.Connect.Models.Network;
using Foundry.Connect.Network;
using Foundry.Connect.ViewModels;

namespace Foundry.Connect.Tests;

public sealed class LegacyNetworkStateAdapterTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 29, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ToSnapshot_CaptivePortalIsNotInternetAccess()
    {
        NetworkStatusSnapshot snapshot = CreateAdapter().ToSnapshot(NetworkState.Initial(Now) with { Internet = InternetStatus.CaptivePortal });

        Assert.False(snapshot.HasInternetAccess);
    }

    [Fact]
    public void ToSnapshot_ApipaIsNotAnIpv4Configuration()
    {
        NetworkState state = NetworkState.Initial(Now) with
        {
            Ethernet = new EthernetState([new EthernetInterfaceState("eth0", "Ethernet", true, IpConfigurationKind.Apipa, "169.254.1.2", null)], Dot1xState.NotConfigured)
        };

        NetworkStatusSnapshot snapshot = CreateAdapter().ToSnapshot(state);

        Assert.True(snapshot.IsEthernetConnected);
        Assert.False(snapshot.HasEthernetIpv4);
        Assert.Equal("Ethernet", snapshot.EthernetAdapterName);
    }

    [Theory]
    [InlineData(WifiAvailability.Available, NetworkLayoutMode.EthernetWifi, true, true)]
    [InlineData(WifiAvailability.NoAdapter, NetworkLayoutMode.EthernetWifi, true, false)]
    [InlineData(WifiAvailability.RuntimeMissing, NetworkLayoutMode.EthernetOnly, false, false)]
    [InlineData(WifiAvailability.Disabled, NetworkLayoutMode.EthernetOnly, false, false)]
    public void ToSnapshot_KeepsTodaysLayoutRule(WifiAvailability availability, NetworkLayoutMode layout, bool runtime, bool adapter)
    {
        NetworkStatusSnapshot snapshot = CreateAdapter().ToSnapshot(NetworkState.Initial(Now) with { Wifi = WifiState.Initial with { Availability = availability } });

        Assert.Equal(layout, snapshot.LayoutMode);
        Assert.Equal(runtime, snapshot.IsWifiRuntimeAvailable);
        Assert.Equal(adapter, snapshot.HasWirelessAdapter);
    }

    [Fact]
    public void ToSnapshot_ListsNetworksByBytesWithLegacyLabels()
    {
        NetworkState state = NetworkState.Initial(Now) with
        {
            Wifi = WifiState.Initial with
            {
                Availability = WifiAvailability.Available,
                Networks =
                [
                    new VisibleWifiNetwork(Guid.Empty, WifiSsid.FromText("Lab"), WifiSecurityType.Wpa2Personal, 70),
                    new VisibleWifiNetwork(Guid.Empty, WifiSsid.FromText("LAB"), WifiSecurityType.Open, 60)
                ],
                Connection = new WifiConnection(Guid.Empty, WifiSsid.FromText("LAB"), "LAB", WifiSecurityType.Open, WifiConnectionSource.Provisioned)
            }
        };

        NetworkStatusSnapshot snapshot = CreateAdapter().ToSnapshot(state);

        Assert.Equal(["4C6162", "4C4142"], snapshot.WifiNetworks.Select(static item => item.SsidHex));
        Assert.Equal(["WPA2-Personal", "Open"], snapshot.WifiNetworks.Select(static item => item.Authentication));
        Assert.Equal("4C4142", snapshot.ConnectedWifiSsidHex);
        Assert.True(snapshot.IsConnectedWifiProvisioned);
    }

    [Theory]
    [InlineData(NetworkOperationKind.ConnectWifiNetwork, NetworkFailureCode.UnsupportedSecurity, false, "Wifi.ActionSelectedNotSupported")]
    [InlineData(NetworkOperationKind.ConnectWifiNetwork, NetworkFailureCode.ConnectTimedOut, false, "Wifi.ActionConnectFailed")]
    [InlineData(NetworkOperationKind.ConnectWifiNetwork, NetworkFailureCode.NoWirelessAdapter, false, "Wifi.NoAdapterAvailable")]
    [InlineData(NetworkOperationKind.ConnectProvisionedWifi, NetworkFailureCode.WifiSecretUnavailable, true, "Wifi.ActionProvisionedProfileUnavailable")]
    [InlineData(NetworkOperationKind.ConnectProvisionedWifi, NetworkFailureCode.ConnectionFailed, true, "Wifi.ActionProvisionedConnectFailed")]
    [InlineData(NetworkOperationKind.DisconnectWifi, NetworkFailureCode.DisconnectFailed, true, "Wifi.ActionProvisionedDisconnectFailed")]
    [InlineData(NetworkOperationKind.DisconnectWifi, NetworkFailureCode.DisconnectTimedOut, false, "Wifi.ActionDisconnectFailed")]
    public void ResolveFeedbackKey_MapsCodesWithoutReadingText(NetworkOperationKind operation, NetworkFailureCode failure, bool provisionedPanel, string expected)
    {
        Assert.Equal(expected, LegacyNetworkStateAdapter.ResolveFeedbackKey(OperationOutcome.Failed(operation, failure), provisionedPanel));
    }

    [Fact]
    public void ResolveFeedbackKey_SuccessAndCancellationShowNothing()
    {
        Assert.Null(LegacyNetworkStateAdapter.ResolveFeedbackKey(OperationOutcome.Success(NetworkOperationKind.ConnectWifiNetwork), false));
        Assert.Null(LegacyNetworkStateAdapter.ResolveFeedbackKey(OperationOutcome.NoChange(NetworkOperationKind.DisconnectWifi), true));
        Assert.Null(LegacyNetworkStateAdapter.ResolveFeedbackKey(OperationOutcome.Cancelled(NetworkOperationKind.ConnectProvisionedWifi), true));
    }

    [Theory]
    [InlineData(ProvisionedWifiStatus.NotConfigured, NetworkFailureCode.None, null)]
    [InlineData(ProvisionedWifiStatus.Unavailable, NetworkFailureCode.WifiSecretUnavailable, "Wifi.ActionProvisionedProfileUnavailable")]
    [InlineData(ProvisionedWifiStatus.Ready, NetworkFailureCode.WifiProfileRejected, "Wifi.ActionApplyProvisionedFailed")]
    [InlineData(ProvisionedWifiStatus.Ready, NetworkFailureCode.WifiCertificateImportFailed, "Wifi.ActionApplyProvisionedFailed")]
    [InlineData(ProvisionedWifiStatus.Ready, NetworkFailureCode.WiredProfileImportFailed, null)]
    [InlineData(ProvisionedWifiStatus.Ready, NetworkFailureCode.Unexpected, null)]
    public void ResolveProvisioningFeedbackKey_ReportsOnlyWifiProblems(ProvisionedWifiStatus status, NetworkFailureCode failure, string? expected)
    {
        var outcome = new ProvisioningOutcome(
            Dot1xState.NotConfigured,
            new ProvisionedWifiState(status),
            null,
            failure == NetworkFailureCode.None ? [] : [failure]);

        Assert.Equal(expected, LegacyNetworkStateAdapter.ResolveProvisioningFeedbackKey(outcome));
    }

    [Fact]
    public void FeedbackKeys_ExistInTheConnectResources()
    {
        IEnumerable<string> keys = Enum.GetValues<NetworkFailureCode>()
            .Where(static code => code != NetworkFailureCode.None)
            .SelectMany(static code => Enum.GetValues<NetworkOperationKind>().SelectMany(operation => new[]
            {
                LegacyNetworkStateAdapter.ResolveFeedbackKey(OperationOutcome.Failed(operation, code), true),
                LegacyNetworkStateAdapter.ResolveFeedbackKey(OperationOutcome.Failed(operation, code), false)
            }))
            .OfType<string>()
            .Distinct();
        var resources = new System.Resources.ResourceManager("Foundry.Connect.Strings.Resources", typeof(App).Assembly);

        Assert.All(keys, key => Assert.False(string.IsNullOrEmpty(resources.GetString(key, System.Globalization.CultureInfo.GetCultureInfo("en-US"))), key));
    }

    private static LegacyNetworkStateAdapter CreateAdapter() => new(
        new Foundry.Wpf.Localization.ResourceLocalizationService(new System.Resources.ResourceManager("Foundry.Connect.Strings.Resources", typeof(App).Assembly)));
}
