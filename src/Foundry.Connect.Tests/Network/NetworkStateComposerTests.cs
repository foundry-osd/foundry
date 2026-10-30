// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Connect.Network;

namespace Foundry.Connect.Tests.Network;

public sealed class NetworkStateComposerTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 29, 8, 0, 0, TimeSpan.Zero);
    private static readonly Guid Wifi = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000001");

    private static ProvisioningOutcome Provisioned(Dot1xState wired) =>
        new(wired, new ProvisionedWifiState(ProvisionedWifiStatus.Ready, Ssid: WifiSsid.FromText("Lab"), ProfileName: "Lab Profile"), null, []);

    private static NetworkObservation Connected(string ssid, string profileName, Dot1xAuthenticationStatus? wired = null) =>
        new([], new WifiObservation(WifiAvailability.Available, new WifiConnection(Wifi, WifiSsid.FromText(ssid), profileName, WifiSecurityType.Wpa2Personal), [], [Wifi]),
            InternetStatus.Online, wired, Now);

    [Theory]
    [InlineData("Lab", "Lab Profile", WifiConnectionSource.Provisioned)]
    [InlineData("Lab", "Other name", WifiConnectionSource.Provisioned)]
    [InlineData("Guest", "Lab Profile", WifiConnectionSource.Provisioned)]
    [InlineData("Guest", "Guest", WifiConnectionSource.Manual)]
    [InlineData("LAB", "LAB", WifiConnectionSource.Manual)]
    public void Compose_DetectsProvisionedConnectionBySsidBytesOrProfileName(string ssid, string profileName, WifiConnectionSource expected)
    {
        NetworkState state = NetworkStateComposer.Compose(Connected(ssid, profileName), ConnectionMode.Automatic, Provisioned(Dot1xState.NotConfigured), null, null, Now);

        Assert.Equal(expected, state.Wifi.Connection!.Source);
    }

    [Theory]
    [InlineData(Dot1xAuthenticationStatus.ProfileApplied, null, Dot1xAuthenticationStatus.ProfileApplied, Dot1xFailureReason.None)]
    [InlineData(Dot1xAuthenticationStatus.ProfileApplied, Dot1xAuthenticationStatus.Authenticated, Dot1xAuthenticationStatus.Authenticated, Dot1xFailureReason.None)]
    [InlineData(Dot1xAuthenticationStatus.ProfileApplied, Dot1xAuthenticationStatus.Failed, Dot1xAuthenticationStatus.Failed, Dot1xFailureReason.AuthenticationFailed)]
    [InlineData(Dot1xAuthenticationStatus.Unavailable, Dot1xAuthenticationStatus.Authenticated, Dot1xAuthenticationStatus.Unavailable, Dot1xFailureReason.SecretUnavailable)]
    public void Compose_MergesObservedWiredAuthenticationOnlyWhenProfileApplied(
        Dot1xAuthenticationStatus provisioned, Dot1xAuthenticationStatus? observed, Dot1xAuthenticationStatus expectedStatus, Dot1xFailureReason expectedReason)
    {
        Dot1xFailureReason provisionedReason = provisioned == Dot1xAuthenticationStatus.Unavailable ? Dot1xFailureReason.SecretUnavailable : Dot1xFailureReason.None;

        NetworkState state = NetworkStateComposer.Compose(
            Connected("Guest", "Guest", observed), ConnectionMode.Automatic, Provisioned(new Dot1xState(provisioned, provisionedReason)), null, null, Now);

        Assert.Equal(new Dot1xState(expectedStatus, expectedReason), state.Ethernet.Dot1x);
    }

    [Fact]
    public void Compose_WithoutObservation_IsChecking()
    {
        NetworkState state = NetworkStateComposer.Compose(null, ConnectionMode.Manual, null, NetworkOperationKind.ApplyProvisioning, null, Now);

        Assert.Equal(InternetStatus.Checking, state.Internet);
        Assert.Equal(ConnectionMode.Manual, state.Mode);
        Assert.Equal(NetworkOperationKind.ApplyProvisioning, state.ActiveOperation);
        Assert.Equal(ProvisionedWifiStatus.NotConfigured, state.Wifi.Provisioned.Status);
        Assert.Equal(Now, state.ObservedAt);
    }
}
