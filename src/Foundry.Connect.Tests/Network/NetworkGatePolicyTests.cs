// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Connect.Network;
using Foundry.Connect.Network.Policy;

namespace Foundry.Connect.Tests.Network;

public sealed class NetworkGatePolicyTests
{
    [Fact]
    public void Constants_ProbeEveryTwoSecondsForFifteenSeconds()
    {
        Assert.Equal(TimeSpan.FromSeconds(15), NetworkGatePolicy.Duration);
        Assert.Equal(TimeSpan.FromSeconds(2), NetworkGatePolicy.RecheckInterval);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(8)]
    [InlineData(14)]
    [InlineData(15)]
    public void Evaluate_OnlineWithinTheGate_SkipsConnect(int elapsedSeconds)
    {
        Assert.Equal(
            NetworkGateOutcome.SkipConnect,
            NetworkGatePolicy.Evaluate(InternetStatus.Online, TimeSpan.FromSeconds(elapsedSeconds), isPasswordPromptOpen: false));
    }

    [Theory]
    [InlineData(InternetStatus.NoInternet)]
    [InlineData(InternetStatus.CaptivePortal)]
    [InlineData(InternetStatus.Checking)]
    public void Evaluate_NotOnlineBeforeTheDeadline_KeepsProbing(InternetStatus internet)
    {
        Assert.Null(NetworkGatePolicy.Evaluate(internet, TimeSpan.FromSeconds(14), isPasswordPromptOpen: false));
    }

    [Theory]
    [InlineData(InternetStatus.NoInternet)]
    [InlineData(InternetStatus.CaptivePortal)]
    public void Evaluate_NotOnlineAtTheDeadline_ShowsConnect(InternetStatus internet)
    {
        Assert.Equal(NetworkGateOutcome.ShowConnect, NetworkGatePolicy.Evaluate(internet, NetworkGatePolicy.Duration, isPasswordPromptOpen: false));
    }

    [Fact]
    public void Evaluate_OnlineWhileAPasswordPromptIsOpen_WaitsThenShowsConnectAtTheDeadline()
    {
        Assert.Null(NetworkGatePolicy.Evaluate(InternetStatus.Online, TimeSpan.FromSeconds(4), isPasswordPromptOpen: true));
        Assert.Equal(
            NetworkGateOutcome.ShowConnect,
            NetworkGatePolicy.Evaluate(InternetStatus.Online, NetworkGatePolicy.Duration, isPasswordPromptOpen: true));
    }

    [Theory]
    [InlineData(0, 0, 2)]
    [InlineData(0, 0.5, 1.5)]
    [InlineData(4, 7, 0)]
    [InlineData(14, 14, 1)]
    [InlineData(14, 15, 0)]
    public void NextProbeDelay_KeepsTheTwoSecondCadenceWithinTheGate(double probeStartedAt, double now, double expected)
    {
        Assert.Equal(
            TimeSpan.FromSeconds(expected),
            NetworkGatePolicy.NextProbeDelay(TimeSpan.FromSeconds(probeStartedAt), TimeSpan.FromSeconds(now)));
    }
}
