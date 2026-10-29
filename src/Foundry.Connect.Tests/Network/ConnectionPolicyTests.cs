// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Connect.Network;
using Foundry.Connect.Network.Policy;

namespace Foundry.Connect.Tests.Network;

public sealed class ConnectionPolicyTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 29, 8, 0, 0, TimeSpan.Zero);

    private static readonly AutomaticConnectInput Eligible = new(
        ConnectionMode.Automatic,
        ProvisionedWifiStatus.Ready,
        WifiAvailability.Available,
        ProvisionedNetworkVisible: true,
        ProvisionedProfileIsHidden: false,
        ConnectedToProvisioned: false,
        InternetStatus.NoInternet,
        IsOperationRunning: false,
        ConsecutiveFailures: 0,
        LastAttemptAt: null,
        Now);

    public static TheoryData<string, AutomaticConnectInput, bool> Scenarios => new()
    {
        { "first_attempt", Eligible, true },
        { "captive_portal_counts_as_no_internet", Eligible with { Internet = InternetStatus.CaptivePortal }, true },
        { "online", Eligible with { Internet = InternetStatus.Online }, false },
        { "still_checking", Eligible with { Internet = InternetStatus.Checking }, false },
        { "manual_mode", Eligible with { Mode = ConnectionMode.Manual }, false },
        { "operation_running", Eligible with { IsOperationRunning = true }, false },
        { "profile_unavailable", Eligible with { ProvisionedStatus = ProvisionedWifiStatus.Unavailable }, false },
        { "not_configured", Eligible with { ProvisionedStatus = ProvisionedWifiStatus.NotConfigured }, false },
        { "no_adapter", Eligible with { WifiAvailability = WifiAvailability.NoAdapter }, false },
        { "runtime_missing", Eligible with { WifiAvailability = WifiAvailability.RuntimeMissing }, false },
        { "wifi_disabled", Eligible with { WifiAvailability = WifiAvailability.Disabled }, false },
        { "ssid_not_visible", Eligible with { ProvisionedNetworkVisible = false }, false },
        { "hidden_profile_not_visible", Eligible with { ProvisionedNetworkVisible = false, ProvisionedProfileIsHidden = true }, true },
        { "already_connected", Eligible with { ConnectedToProvisioned = true }, false },
        { "after_success_no_delay", Eligible with { LastAttemptAt = Now.AddSeconds(-1) }, true },
        { "one_failure_wait", Eligible with { ConsecutiveFailures = 1, LastAttemptAt = Now.AddSeconds(-19) }, false },
        { "one_failure_due", Eligible with { ConsecutiveFailures = 1, LastAttemptAt = Now.AddSeconds(-20) }, true },
        { "two_failures_wait", Eligible with { ConsecutiveFailures = 2, LastAttemptAt = Now.AddSeconds(-39) }, false },
        { "two_failures_due", Eligible with { ConsecutiveFailures = 2, LastAttemptAt = Now.AddSeconds(-40) }, true },
        { "three_failures_wait", Eligible with { ConsecutiveFailures = 3, LastAttemptAt = Now.AddSeconds(-79) }, false },
        { "many_failures_capped", Eligible with { ConsecutiveFailures = 9, LastAttemptAt = Now.AddMinutes(-2) }, true },
        { "many_failures_cap_not_reached", Eligible with { ConsecutiveFailures = 9, LastAttemptAt = Now.AddSeconds(-119) }, false }
    };

    [Theory]
    [MemberData(nameof(Scenarios))]
    public void ShouldConnectProvisionedAutomatically_ScenarioTable(string scenario, AutomaticConnectInput input, bool expected)
    {
        Assert.True(expected == ConnectionPolicy.ShouldConnectProvisionedAutomatically(input), scenario);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 20)]
    [InlineData(2, 40)]
    [InlineData(3, 80)]
    [InlineData(4, 120)]
    [InlineData(50, 120)]
    public void RetryDelay_DoublesUpToTwoMinutes(int failures, int expectedSeconds)
    {
        Assert.Equal(TimeSpan.FromSeconds(expectedSeconds), ConnectionPolicy.RetryDelay(failures));
    }

    [Theory]
    [InlineData(ConnectionMode.Automatic)]
    [InlineData(ConnectionMode.Manual)]
    public void AfterManualAction_IsAlwaysManual(ConnectionMode current)
    {
        Assert.Equal(ConnectionMode.Manual, ConnectionPolicy.AfterManualAction(current));
    }

    private static VisibleWifiNetwork Visible(string ssid) =>
        new(Guid.NewGuid(), WifiSsid.FromText(ssid), WifiSecurityType.Wpa2Personal, 70);

    private static WifiState WifiWith(ProvisionedWifiState provisioned, params VisibleWifiNetwork[] networks) =>
        new(WifiAvailability.Available, null, networks, provisioned);

    [Fact]
    public void IsProvisionedNetworkVisible_WhenSsidIsListed_IsTrue()
    {
        WifiState wifi = WifiWith(
            new(ProvisionedWifiStatus.Ready, Ssid: WifiSsid.FromText("Provisioned")),
            Visible("Other"),
            Visible("Provisioned"));

        Assert.True(ConnectionPolicy.IsProvisionedNetworkVisible(wifi));
    }

    [Fact]
    public void IsProvisionedNetworkVisible_WhenSsidIsAbsent_IsFalse()
    {
        WifiState wifi = WifiWith(new(ProvisionedWifiStatus.Ready, Ssid: WifiSsid.FromText("Provisioned")), Visible("Other"));

        Assert.False(ConnectionPolicy.IsProvisionedNetworkVisible(wifi));
    }

    [Fact]
    public void IsProvisionedNetworkVisible_WhenProvisionedProfileHasNoSsid_CountsAsReachable()
    {
        // An enterprise template without an SSID element cannot be matched against the list: let the attempt run.
        WifiState wifi = WifiWith(new(ProvisionedWifiStatus.Ready, Ssid: null, IsEnterprise: true), Visible("Other"));

        Assert.True(ConnectionPolicy.IsProvisionedNetworkVisible(wifi));
    }

    [Fact]
    public void IsProvisionedNetworkVisible_WhenLockedSsidIsListed_IsTrue()
    {
        WifiState wifi = WifiWith(new(ProvisionedWifiStatus.Locked, Ssid: WifiSsid.FromText("Provisioned")), Visible("Provisioned"));

        Assert.True(ConnectionPolicy.IsProvisionedNetworkVisible(wifi));
    }

    [Fact]
    public void IsProvisionedNetworkVisible_WhenLockedProfileHasNoSsid_IsFalse()
    {
        // R4: a locked enterprise profile has no readable SSID; only the Connect button unlocks it.
        WifiState wifi = WifiWith(new(ProvisionedWifiStatus.Locked, Ssid: null, IsEnterprise: true), Visible("Other"));

        Assert.False(ConnectionPolicy.IsProvisionedNetworkVisible(wifi));
    }

    private static AutomaticConnectInput LockedInput(InternetStatus internet = InternetStatus.NoInternet, ConnectionMode mode = ConnectionMode.Automatic,
        bool visible = true, bool running = false) =>
        new(mode, ProvisionedWifiStatus.Locked, WifiAvailability.Available, visible, false, false, internet, running, 0, null,
            new DateTimeOffset(2026, 10, 29, 8, 0, 0, TimeSpan.Zero));

    [Theory]
    [InlineData("no_internet", InternetStatus.NoInternet, ConnectionMode.Automatic, true, false, false, true)]
    [InlineData("captive_portal", InternetStatus.CaptivePortal, ConnectionMode.Automatic, true, false, false, true)]
    [InlineData("ethernet_online", InternetStatus.Online, ConnectionMode.Automatic, true, false, false, false)]
    [InlineData("checking", InternetStatus.Checking, ConnectionMode.Automatic, true, false, false, false)]
    [InlineData("manual_mode", InternetStatus.NoInternet, ConnectionMode.Manual, true, false, false, false)]
    [InlineData("not_visible", InternetStatus.NoInternet, ConnectionMode.Automatic, false, false, false, false)]
    [InlineData("operation_running", InternetStatus.NoInternet, ConnectionMode.Automatic, true, true, false, false)]
    [InlineData("already_requested", InternetStatus.NoInternet, ConnectionMode.Automatic, true, false, true, false)]
    public void ShouldRequestUnlock(string row, InternetStatus internet, ConnectionMode mode, bool visible, bool running, bool requested, bool expected)
    {
        Assert.True(expected == ConnectionPolicy.ShouldRequestUnlock(LockedInput(internet, mode, visible, running), requested), row);
    }

    [Fact]
    public void ShouldConnectProvisionedAutomatically_NeverForLocked() =>
        Assert.False(ConnectionPolicy.ShouldConnectProvisionedAutomatically(LockedInput()));

    [Fact]
    public void IsProvisionedNetworkVisible_WhenNothingIsProvisioned_IsFalse()
    {
        WifiState wifi = WifiWith(ProvisionedWifiState.NotConfigured, Visible("Provisioned"));

        Assert.False(ConnectionPolicy.IsProvisionedNetworkVisible(wifi));
    }

    [Fact]
    public void ProvisionedProfileWithoutSsid_ConnectsAutomatically()
    {
        WifiState wifi = WifiWith(new(ProvisionedWifiStatus.Ready, Ssid: null, IsEnterprise: true));

        AutomaticConnectInput input = Eligible with
        {
            ProvisionedNetworkVisible = ConnectionPolicy.IsProvisionedNetworkVisible(wifi),
            ProvisionedProfileIsHidden = wifi.Provisioned.IsHidden
        };

        Assert.True(ConnectionPolicy.ShouldConnectProvisionedAutomatically(input));
    }
}
