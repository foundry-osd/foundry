// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Connect.Network;
using Foundry.Connect.Tests.Support;
using Foundry.Connect.ViewModels;

namespace Foundry.Connect.Tests.ViewModels;

public sealed class DebugScenarioCatalogTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 29, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public void All_ListsLiveFirstThenTheFourteenScenarios()
    {
        Assert.Equal(ConnectDebugScenario.Live, DebugScenarioCatalog.All[0]);
        Assert.Equal(15, DebugScenarioCatalog.All.Count);
    }

    [Fact]
    public void EveryScenario_HasAnExistingMenuText()
    {
        Assert.All(DebugScenarioCatalog.All, scenario => Assert.True(ConnectResources.Exists(DebugScenarioCatalog.ResourceKey(scenario)), scenario.ToString()));
    }

    [Fact]
    public void EverySimulatedScenario_CreatesAnAutomaticStateAtNow()
    {
        foreach (ConnectDebugScenario scenario in DebugScenarioCatalog.All.Skip(1))
        {
            NetworkState state = DebugScenarioCatalog.Create(scenario, Now);

            Assert.Equal(Now, state.ObservedAt);
            Assert.Equal(ConnectionMode.Automatic, state.Mode);
            Assert.Null(state.ActiveOperation);
            Assert.Null(state.LastOutcome);
        }
    }

    [Fact]
    public void Live_IsNotASimulatedState()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => DebugScenarioCatalog.Create(ConnectDebugScenario.Live, Now));
    }

    [Fact]
    public void UndefinedScenario_IsRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => DebugScenarioCatalog.Create((ConnectDebugScenario)999, Now));
    }

    [Fact]
    public void WifiReady_IsOnlineThroughTheProvisionedWifi()
    {
        NetworkState state = DebugScenarioCatalog.Create(ConnectDebugScenario.WifiReady, Now);

        Assert.Equal(InternetPath.Wifi, state.ActiveInternetPath);
        Assert.Equal(WifiConnectionSource.Provisioned, state.Wifi.Connection!.Source);
        Assert.Equal(4, state.Wifi.Networks.Count);
    }

    [Fact]
    public void Dot1xFailed_CarriesTheAuthenticationFailure()
    {
        NetworkState state = DebugScenarioCatalog.Create(ConnectDebugScenario.Dot1xFailed, Now);

        Assert.Equal(new Dot1xState(Dot1xAuthenticationStatus.Failed, Dot1xFailureReason.AuthenticationFailed), state.Ethernet.Dot1x);
    }

    [Theory]
    [InlineData(ConnectDebugScenario.Checking, false, false, false, InternetStatus.Checking, WifiAvailability.Disabled)]
    [InlineData(ConnectDebugScenario.NoAdapter, false, false, false, InternetStatus.NoInternet, WifiAvailability.Disabled)]
    [InlineData(ConnectDebugScenario.NoLink, true, false, false, InternetStatus.NoInternet, WifiAvailability.Disabled)]
    [InlineData(ConnectDebugScenario.WaitingForAddress, true, true, false, InternetStatus.NoInternet, WifiAvailability.Disabled)]
    [InlineData(ConnectDebugScenario.ApipaAddress, true, true, false, InternetStatus.NoInternet, WifiAvailability.Disabled)]
    [InlineData(ConnectDebugScenario.EthernetNoInternet, true, true, true, InternetStatus.NoInternet, WifiAvailability.Disabled)]
    [InlineData(ConnectDebugScenario.CaptivePortal, true, true, true, InternetStatus.CaptivePortal, WifiAvailability.Disabled)]
    [InlineData(ConnectDebugScenario.EthernetReady, true, true, true, InternetStatus.Online, WifiAvailability.Disabled)]
    [InlineData(ConnectDebugScenario.Dot1xFailed, true, true, false, InternetStatus.NoInternet, WifiAvailability.Disabled)]
    [InlineData(ConnectDebugScenario.WifiRuntimeMissing, true, false, false, InternetStatus.NoInternet, WifiAvailability.RuntimeMissing)]
    [InlineData(ConnectDebugScenario.NoWifiAdapter, true, false, false, InternetStatus.NoInternet, WifiAvailability.NoAdapter)]
    [InlineData(ConnectDebugScenario.WifiNetworks, true, false, false, InternetStatus.NoInternet, WifiAvailability.Available)]
    [InlineData(ConnectDebugScenario.ProvisionedWifiUnavailable, true, false, false, InternetStatus.NoInternet, WifiAvailability.Available)]
    [InlineData(ConnectDebugScenario.WifiReady, true, false, false, InternetStatus.Online, WifiAvailability.Available)]
    public void Scenario_MatchesTheMatrix(
        ConnectDebugScenario scenario,
        bool hasAdapter,
        bool hasLink,
        bool hasUsableAddress,
        InternetStatus internet,
        WifiAvailability wifi)
    {
        NetworkState state = DebugScenarioCatalog.Create(scenario, Now);

        Assert.Equal(hasAdapter, state.Ethernet.HasAdapter);
        Assert.Equal(hasLink, state.Ethernet.HasLink);
        Assert.Equal(hasUsableAddress, state.Ethernet.HasUsableAddress);
        Assert.Equal(internet, state.Internet);
        Assert.Equal(wifi, state.Wifi.Availability);
    }
}
