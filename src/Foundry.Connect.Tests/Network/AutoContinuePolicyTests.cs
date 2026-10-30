// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Connect.Network;
using Foundry.Connect.Network.Policy;

namespace Foundry.Connect.Tests.Network;

public sealed class AutoContinuePolicyTests
{
    private static readonly AutoContinueState Counting = new(AutoContinueStatus.Counting, 6);

    public static TheoryData<string, AutoContinueState, InternetStatus, AutoContinueStatus> InternetScenarios => new()
    {
        { "online_starts", AutoContinueState.Initial, InternetStatus.Online, AutoContinueStatus.Counting },
        { "portal_does_not_start", AutoContinueState.Initial, InternetStatus.CaptivePortal, AutoContinueStatus.Idle },
        { "loss_resets", Counting, InternetStatus.NoInternet, AutoContinueStatus.Idle },
        { "portal_resets", Counting, InternetStatus.CaptivePortal, AutoContinueStatus.Idle },
        { "still_online_keeps_counting", Counting, InternetStatus.Online, AutoContinueStatus.Counting },
        { "paused_is_sticky", new(AutoContinueStatus.Paused, 0), InternetStatus.Online, AutoContinueStatus.Paused },
        { "cancelled_is_sticky", new(AutoContinueStatus.Cancelled, 0), InternetStatus.Online, AutoContinueStatus.Cancelled }
    };

    [Theory]
    [MemberData(nameof(InternetScenarios))]
    public void OnInternetStatus_ScenarioTable(string scenario, AutoContinueState state, InternetStatus internet, AutoContinueStatus expected)
    {
        Assert.True(expected == AutoContinuePolicy.OnInternetStatus(state, internet, hasNetworkingInteraction: false, isPasswordPromptOpen: false).Status, scenario);
    }

    [Fact]
    public void OnInternetStatus_WhenStillOnline_KeepsTheSecondsLeft()
    {
        Assert.Equal(Counting, AutoContinuePolicy.OnInternetStatus(Counting, InternetStatus.Online, hasNetworkingInteraction: false, isPasswordPromptOpen: false));
    }

    [Fact]
    public void OnInternetStatus_AfterANetworkingInteraction_OffersContinueWithoutCounting()
    {
        AutoContinueState state = AutoContinuePolicy.OnInternetStatus(AutoContinueState.Initial, InternetStatus.Online, hasNetworkingInteraction: true, isPasswordPromptOpen: false);

        Assert.Equal(new AutoContinueState(AutoContinueStatus.Paused, 0), state);
        Assert.Equal(AutoContinueState.Initial, AutoContinuePolicy.OnInternetStatus(AutoContinueState.Initial, InternetStatus.NoInternet, hasNetworkingInteraction: true, isPasswordPromptOpen: false));
    }

    [Fact]
    public void OnInternetStatus_WhilePasswordPromptIsOpen_NeverCounts()
    {
        Assert.Equal(AutoContinueState.Initial, AutoContinuePolicy.OnInternetStatus(AutoContinueState.Initial, InternetStatus.Online, hasNetworkingInteraction: false, isPasswordPromptOpen: true));
        Assert.Equal(AutoContinueState.Initial, AutoContinuePolicy.OnInternetStatus(Counting, InternetStatus.Online, hasNetworkingInteraction: false, isPasswordPromptOpen: true));
        Assert.Equal(AutoContinueState.Initial, AutoContinuePolicy.OnInternetStatus(AutoContinueState.Initial, InternetStatus.Online, hasNetworkingInteraction: true, isPasswordPromptOpen: true));
    }

    [Fact]
    public void Countdown_ShowsEachSecondOnceAndElapsesOnTheTenthTick()
    {
        AutoContinueState state = AutoContinuePolicy.OnInternetStatus(AutoContinueState.Initial, InternetStatus.Online, hasNetworkingInteraction: false, isPasswordPromptOpen: false);
        List<int> shown = [];

        for (int tick = 1; tick <= 10; tick++)
        {
            Assert.False(AutoContinuePolicy.ShouldContinue(state));
            shown.Add(state.SecondsRemaining);
            state = AutoContinuePolicy.OnTick(state);
        }

        Assert.Equal([10, 9, 8, 7, 6, 5, 4, 3, 2, 1], shown);
        Assert.Equal(new AutoContinueState(AutoContinueStatus.Elapsed, 0), state);
        Assert.True(AutoContinuePolicy.ShouldContinue(state));
    }

    [Fact]
    public void Interaction_PausesOnlyARunningCountdown()
    {
        Assert.Equal(new AutoContinueState(AutoContinueStatus.Paused, 0), AutoContinuePolicy.OnNetworkingInteraction(Counting));
        Assert.Equal(AutoContinueStatus.Idle, AutoContinuePolicy.OnNetworkingInteraction(AutoContinueState.Initial).Status);
    }

    [Fact]
    public void StayHere_CancelsUntilTheSessionEnds()
    {
        AutoContinueState cancelled = AutoContinuePolicy.OnStayHere(Counting);

        Assert.Equal(AutoContinueStatus.Cancelled, cancelled.Status);
        Assert.Equal(cancelled, AutoContinuePolicy.OnTick(cancelled));
    }

    [Fact]
    public void Tick_LeavesEveryStoppedStateUnchanged()
    {
        Assert.Equal(AutoContinueState.Initial, AutoContinuePolicy.OnTick(AutoContinueState.Initial));
        Assert.Equal(new AutoContinueState(AutoContinueStatus.Paused, 0), AutoContinuePolicy.OnTick(new(AutoContinueStatus.Paused, 0)));
    }

    [Fact]
    public void StayHere_DoesNotUndoAnElapsedCountdown()
    {
        AutoContinueState elapsed = new(AutoContinueStatus.Elapsed, 0);

        Assert.Equal(AutoContinueStatus.Elapsed, AutoContinuePolicy.OnStayHere(elapsed).Status);
    }

    [Fact]
    public void FinalProbeFailure_WaitsForTheNextOnlineState()
    {
        AutoContinueState elapsed = new(AutoContinueStatus.Elapsed, 0);

        Assert.Equal(AutoContinueStatus.Idle, AutoContinuePolicy.OnFinalProbeFailed(elapsed).Status);
        Assert.Equal(AutoContinueStatus.Paused, AutoContinuePolicy.OnFinalProbeFailed(new(AutoContinueStatus.Paused, 0)).Status);
    }
}
