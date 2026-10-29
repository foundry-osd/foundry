// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Connect.Network;
using Foundry.Connect.Network.Policy;

namespace Foundry.Connect.Tests.Network;

public sealed class AutoContinuePolicyTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 29, 8, 0, 0, TimeSpan.Zero);
    private static readonly AutoContinueState Counting = new(AutoContinueStatus.Counting, Now.AddSeconds(10));

    public static TheoryData<string, AutoContinueState, InternetStatus, AutoContinueStatus> InternetScenarios => new()
    {
        { "online_starts", AutoContinueState.Initial, InternetStatus.Online, AutoContinueStatus.Counting },
        { "portal_does_not_start", AutoContinueState.Initial, InternetStatus.CaptivePortal, AutoContinueStatus.Idle },
        { "loss_resets", Counting, InternetStatus.NoInternet, AutoContinueStatus.Idle },
        { "portal_resets", Counting, InternetStatus.CaptivePortal, AutoContinueStatus.Idle },
        { "still_online_keeps_deadline", Counting, InternetStatus.Online, AutoContinueStatus.Counting },
        { "paused_is_sticky", new(AutoContinueStatus.Paused, null), InternetStatus.Online, AutoContinueStatus.Paused },
        { "cancelled_is_sticky", new(AutoContinueStatus.Cancelled, null), InternetStatus.Online, AutoContinueStatus.Cancelled }
    };

    [Theory]
    [MemberData(nameof(InternetScenarios))]
    public void OnInternetStatus_ScenarioTable(string scenario, AutoContinueState state, InternetStatus internet, AutoContinueStatus expected)
    {
        Assert.True(expected == AutoContinuePolicy.OnInternetStatus(state, internet, Now, hasNetworkingInteraction: false).Status, scenario);
    }

    [Fact]
    public void OnInternetStatus_WhenStillOnline_KeepsTheDeadline()
    {
        Assert.Equal(Counting, AutoContinuePolicy.OnInternetStatus(Counting, InternetStatus.Online, Now.AddSeconds(5), hasNetworkingInteraction: false));
    }

    [Fact]
    public void OnInternetStatus_AfterANetworkingInteraction_OffersContinueWithoutCounting()
    {
        AutoContinueState state = AutoContinuePolicy.OnInternetStatus(AutoContinueState.Initial, InternetStatus.Online, Now, hasNetworkingInteraction: true);

        Assert.Equal(new AutoContinueState(AutoContinueStatus.Paused, null), state);
        Assert.Equal(AutoContinueState.Initial, AutoContinuePolicy.OnInternetStatus(AutoContinueState.Initial, InternetStatus.NoInternet, Now, hasNetworkingInteraction: true));
    }

    [Fact]
    public void Countdown_ElapsesAfterTenSeconds()
    {
        AutoContinueState state = AutoContinuePolicy.OnInternetStatus(AutoContinueState.Initial, InternetStatus.Online, Now, hasNetworkingInteraction: false);

        Assert.Equal(TimeSpan.FromSeconds(10), state.RemainingAt(Now));
        Assert.False(AutoContinuePolicy.ShouldContinue(AutoContinuePolicy.OnTick(state, Now.AddSeconds(9))));
        Assert.True(AutoContinuePolicy.ShouldContinue(AutoContinuePolicy.OnTick(state, Now.AddSeconds(10))));
    }

    [Fact]
    public void Interaction_PausesOnlyARunningCountdown()
    {
        Assert.Equal(AutoContinueStatus.Paused, AutoContinuePolicy.OnNetworkingInteraction(Counting).Status);
        Assert.Equal(AutoContinueStatus.Idle, AutoContinuePolicy.OnNetworkingInteraction(AutoContinueState.Initial).Status);
        Assert.Equal(TimeSpan.Zero, AutoContinuePolicy.OnNetworkingInteraction(Counting).RemainingAt(Now));
    }

    [Fact]
    public void StayHere_CancelsUntilTheSessionEnds()
    {
        AutoContinueState cancelled = AutoContinuePolicy.OnStayHere(Counting);

        Assert.Equal(AutoContinueStatus.Cancelled, cancelled.Status);
        Assert.Equal(AutoContinueStatus.Cancelled, AutoContinuePolicy.OnTick(cancelled, Now.AddMinutes(5)).Status);
    }

    [Fact]
    public void StayHere_DoesNotUndoAnElapsedCountdown()
    {
        AutoContinueState elapsed = new(AutoContinueStatus.Elapsed, null);

        Assert.Equal(AutoContinueStatus.Elapsed, AutoContinuePolicy.OnStayHere(elapsed).Status);
    }

    [Fact]
    public void FinalProbeFailure_WaitsForTheNextOnlineState()
    {
        AutoContinueState elapsed = new(AutoContinueStatus.Elapsed, null);

        Assert.Equal(AutoContinueStatus.Idle, AutoContinuePolicy.OnFinalProbeFailed(elapsed).Status);
        Assert.Equal(AutoContinueStatus.Paused, AutoContinuePolicy.OnFinalProbeFailed(new(AutoContinueStatus.Paused, null)).Status);
    }
}
