// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Connect.Lifetime;
using Foundry.Connect.Network;
using Foundry.Connect.Network.Policy;
using Foundry.Connect.Tests.Support;
using Foundry.Connect.ViewModels;
using Foundry.Core.Models.Runtime;
using Foundry.Wpf.Localization;
using Microsoft.Extensions.Logging;

namespace Foundry.Connect.Tests.ViewModels;

public sealed class AutoContinueViewModelTests : IDisposable
{
    private readonly ManualTimeProvider _clock = new();
    private readonly FakeSuccessfulExitService _exit = new();
    private readonly FakeApplicationLifetime _lifetime = new();
    private readonly ResourceLocalizationService _localization = TestLocalization.Create();
    private AutoContinueViewModel? _viewModel;

    public void Dispose() => _viewModel?.Dispose();

    private AutoContinueViewModel Create(bool enabled = true, RecordingLogger<AutoContinueViewModel>? logger = null) =>
        _viewModel = new AutoContinueViewModel(_localization, _exit, _lifetime, _clock, new ImmediateUiDispatcher(), logger ?? new RecordingLogger<AutoContinueViewModel>(), enabled);

    private void Tick(int seconds)
    {
        for (int index = 0; index < seconds; index++)
        {
            _clock.Advance(AutoContinueViewModel.TickInterval);
        }
    }

    [Fact]
    public void Online_StartsATenSecondCountdown()
    {
        AutoContinueViewModel viewModel = Create();

        viewModel.OnInternetStatusChanged(InternetStatus.Online);

        Assert.True(viewModel.IsCounting);
        Assert.Equal(10, viewModel.SecondsRemaining);
        Assert.Equal(_localization.Format("Status.AutoContinueFormat", 10), viewModel.CaptionText);
        Assert.True(viewModel.IsContinueDefault);
        Tick(1);
        Assert.Equal(9, viewModel.SecondsRemaining);
        Assert.Equal(1, _clock.ActiveTimerCount);
    }

    [Fact]
    public async Task Countdown_WhenTicksDriftAroundTheSecondBoundaries_ShowsEveryDigitOnceAndEndsAfterTenTicks()
    {
        // A real timer fires a little early or late: the ticks read 0.999 s, 2.001 s, 2.999 s and so on.
        var clock = new SkewedClock(_clock);
        AutoContinueViewModel viewModel = _viewModel = new AutoContinueViewModel(
            _localization, _exit, _lifetime, clock, new ImmediateUiDispatcher(), new RecordingLogger<AutoContinueViewModel>(), isAutoContinueEnabled: true);
        List<int> shownBeforeEachTick = [];

        viewModel.OnInternetStatusChanged(InternetStatus.Online);
        for (int tick = 1; tick <= 10; tick++)
        {
            Assert.True(viewModel.IsCounting, $"tick {tick}");
            shownBeforeEachTick.Add(viewModel.SecondsRemaining);
            clock.Skew = TimeSpan.FromMilliseconds(tick % 2 == 1 ? -1 : 1);
            _clock.Advance(AutoContinueViewModel.TickInterval);
        }

        await viewModel.PendingCompletion!;
        Assert.Equal([10, 9, 8, 7, 6, 5, 4, 3, 2, 1], shownBeforeEachTick);
        Assert.Equal(1, _exit.Calls);
    }

    [Fact]
    public async Task Countdown_Elapsed_ContinuesExactlyOnce()
    {
        AutoContinueViewModel viewModel = Create();
        viewModel.OnInternetStatusChanged(InternetStatus.Online);

        Tick(9);
        Assert.Equal(0, _exit.Calls);
        Tick(1);
        await viewModel.PendingCompletion!;
        Tick(30);

        Assert.Equal(1, _exit.Calls);
        Assert.Equal(0, _clock.ActiveTimerCount);
    }

    [Fact]
    public void NetworkingInteraction_PausesForTheSession()
    {
        AutoContinueViewModel viewModel = Create();
        viewModel.OnInternetStatusChanged(InternetStatus.Online);
        Tick(3);

        viewModel.OnNetworkingInteraction();
        Tick(30);
        viewModel.OnInternetStatusChanged(InternetStatus.Online);

        Assert.Equal(0, _exit.Calls);
        Assert.Equal(AutoContinueStatus.Paused, viewModel.State.Status);
        Assert.Equal(_localization.GetString("Status.AutoContinuePaused"), viewModel.CaptionText);
        Assert.Equal(0, _clock.ActiveTimerCount);
        Assert.False(viewModel.IsContinueDefault);
        Assert.True(viewModel.ContinueCommand.CanExecute(null));
    }

    [Fact]
    public void NetworkingInteraction_BeforeOnline_OffersContinueWithoutCounting()
    {
        AutoContinueViewModel viewModel = Create();

        viewModel.OnNetworkingInteraction();
        viewModel.OnInternetStatusChanged(InternetStatus.Online);

        Assert.Equal(AutoContinueStatus.Paused, viewModel.State.Status);
        Assert.Equal(_localization.GetString("Status.AutoContinuePaused"), viewModel.CaptionText);
        Assert.True(viewModel.HasCaption);
        Assert.Equal(0, _clock.ActiveTimerCount);
        Assert.False(viewModel.IsContinueDefault);
        Assert.True(viewModel.ContinueCommand.CanExecute(null));
    }

    [Fact]
    public void NetworkingInteraction_KeepsContinueOffTheDefaultForTheSession()
    {
        AutoContinueViewModel viewModel = Create();
        viewModel.OnInternetStatusChanged(InternetStatus.Online);
        viewModel.StayHereCommand.Execute(null);
        Assert.True(viewModel.IsContinueDefault);

        viewModel.OnNetworkingInteraction();
        Assert.Equal(AutoContinueStatus.Cancelled, viewModel.State.Status);
        Assert.False(viewModel.IsContinueDefault);
        viewModel.OnInternetStatusChanged(InternetStatus.NoInternet);
        viewModel.OnInternetStatusChanged(InternetStatus.Online);

        Assert.False(viewModel.IsContinueDefault);
        Assert.True(viewModel.ContinueCommand.CanExecute(null));
    }

    [Fact]
    public void PasswordPrompt_HoldsTheCountdownUntilItCloses()
    {
        AutoContinueViewModel viewModel = Create();
        viewModel.OnPasswordPromptOpened();

        viewModel.OnInternetStatusChanged(InternetStatus.Online);
        Tick(30);

        Assert.Equal(AutoContinueState.Initial, viewModel.State);
        Assert.Equal(0, _clock.ActiveTimerCount);
        Assert.Equal(0, _exit.Calls);

        viewModel.OnPasswordPromptClosed();

        Assert.True(viewModel.IsCounting);
        Assert.Equal(10, viewModel.SecondsRemaining);
        Assert.True(viewModel.IsContinueDefault);
    }

    [Fact]
    public void PasswordPrompt_OpenedDuringTheCountdown_StopsItAndRestartsFromTenAfterClose()
    {
        AutoContinueViewModel viewModel = Create();
        viewModel.OnInternetStatusChanged(InternetStatus.Online);
        Tick(4);

        viewModel.OnPasswordPromptOpened();
        Tick(30);
        Assert.False(viewModel.IsCounting);
        Assert.Equal(0, _exit.Calls);

        viewModel.OnPasswordPromptClosed();
        Assert.Equal(10, viewModel.SecondsRemaining);
    }

    [Fact]
    public void PasswordPrompt_ClosedWithoutInternet_DoesNotCount()
    {
        AutoContinueViewModel viewModel = Create();
        viewModel.OnPasswordPromptOpened();
        viewModel.OnInternetStatusChanged(InternetStatus.Online);
        viewModel.OnInternetStatusChanged(InternetStatus.NoInternet);

        viewModel.OnPasswordPromptClosed();

        Assert.Equal(AutoContinueState.Initial, viewModel.State);
    }

    [Fact]
    public void StayHere_RequestsTheFocusOnContinue()
    {
        AutoContinueViewModel viewModel = Create();
        int requests = 0;
        viewModel.ContinueFocusRequested += (_, _) => requests++;
        viewModel.OnInternetStatusChanged(InternetStatus.Online);

        viewModel.StayHereCommand.Execute(null);

        Assert.Equal(1, requests);
    }

    [Fact]
    public void StayHere_AfterDispose_DoesNothing()
    {
        AutoContinueViewModel viewModel = Create();
        int requests = 0;
        viewModel.ContinueFocusRequested += (_, _) => requests++;
        viewModel.OnInternetStatusChanged(InternetStatus.Online);
        viewModel.Dispose();

        viewModel.StayHereCommand.Execute(null);

        Assert.Equal(0, requests);
        Assert.NotEqual(AutoContinueStatus.Cancelled, viewModel.State.Status);
    }

    [Fact]
    public async Task FinalProbeThrowing_LogsTheTypeAndWaitsForTheNextOnlineState()
    {
        var logger = new RecordingLogger<AutoContinueViewModel>();
        _exit.Gate = new TaskCompletionSource<SuccessfulExitResult>();
        _exit.Gate.SetException(new InvalidOperationException("Profile 'Foundry Lab' failed."));
        AutoContinueViewModel viewModel = Create(logger: logger);
        viewModel.OnInternetStatusChanged(InternetStatus.Online);

        Tick(10);
        await viewModel.PendingCompletion!;
        await viewModel.ContinueCommand.ExecuteAsync(null);

        Assert.Equal(AutoContinueStatus.Idle, viewModel.State.Status);
        List<RecordedLogEntry> warnings = logger.Entries.Where(static entry => entry.Level == LogLevel.Warning).ToList();
        Assert.Equal(2, warnings.Count);
        Assert.All(warnings, static warning => Assert.Equal(nameof(InvalidOperationException), warning.Properties["FailureType"]));
        Assert.DoesNotContain(logger.Entries, static entry => entry.Exception is not null || entry.Message.Contains("Foundry Lab", StringComparison.Ordinal));
        viewModel.OnInternetStatusChanged(InternetStatus.Online);
        Assert.True(viewModel.IsCounting);
    }

    [Fact]
    public async Task CountdownEnd_DuringAManualContinue_CompletesOnce()
    {
        _exit.Gate = new TaskCompletionSource<SuccessfulExitResult>();
        AutoContinueViewModel viewModel = Create();
        viewModel.OnInternetStatusChanged(InternetStatus.Online);
        Tick(9);

        Task manual = viewModel.ContinueCommand.ExecuteAsync(null);
        Tick(1);
        _exit.Gate.SetResult(SuccessfulExitResult.Exited);
        await manual;
        await (viewModel.PendingCompletion ?? Task.CompletedTask);

        Assert.Equal(1, _exit.Calls);
    }

    [Fact]
    public void Dispose_StopsTheCountdown()
    {
        AutoContinueViewModel viewModel = Create();
        viewModel.OnInternetStatusChanged(InternetStatus.Online);
        Tick(3);

        viewModel.Dispose();
        Tick(30);

        Assert.Equal(0, _clock.ActiveTimerCount);
        Assert.Equal(7, viewModel.SecondsRemaining);
        Assert.Equal(AutoContinueStatus.Counting, viewModel.State.Status);
        Assert.Equal(0, _exit.Calls);
    }

    [Fact]
    public async Task StayHere_CancelsButContinueStillExits()
    {
        AutoContinueViewModel viewModel = Create();
        viewModel.OnInternetStatusChanged(InternetStatus.Online);
        Assert.True(viewModel.StayHereCommand.CanExecute(null));

        viewModel.StayHereCommand.Execute(null);
        Tick(30);

        Assert.Equal(0, _exit.Calls);
        Assert.Equal(AutoContinueStatus.Cancelled, viewModel.State.Status);
        Assert.Equal(_localization.GetString("Status.AutoContinueCancelled"), viewModel.CaptionText);
        Assert.False(viewModel.StayHereCommand.CanExecute(null));
        await viewModel.ContinueCommand.ExecuteAsync(null);
        Assert.Equal(1, _exit.Calls);
    }

    [Fact]
    public void InternetLoss_ResetsAndOnlineRestartsFromTen()
    {
        AutoContinueViewModel viewModel = Create();
        viewModel.OnInternetStatusChanged(InternetStatus.Online);
        Tick(4);

        viewModel.OnInternetStatusChanged(InternetStatus.CaptivePortal);

        Assert.False(viewModel.IsCounting);
        Assert.False(viewModel.HasCaption);
        Assert.False(viewModel.ContinueCommand.CanExecute(null));
        viewModel.OnInternetStatusChanged(InternetStatus.Online);
        Assert.Equal(10, viewModel.SecondsRemaining);
    }

    [Fact]
    public async Task FinalProbeFailure_WaitsForTheNextOnlineState()
    {
        _exit.Result = SuccessfulExitResult.NotOnline;
        AutoContinueViewModel viewModel = Create();
        viewModel.OnInternetStatusChanged(InternetStatus.Online);

        Tick(10);
        await viewModel.PendingCompletion!;

        Assert.Equal(1, _exit.Calls);
        Assert.False(viewModel.IsCounting);
        viewModel.OnInternetStatusChanged(InternetStatus.Online);
        Assert.True(viewModel.IsCounting);
        Assert.Equal(10, viewModel.SecondsRemaining);
    }

    [Fact]
    public void Disabled_DoesNotCountButContinueWorks()
    {
        AutoContinueViewModel viewModel = Create(enabled: false);

        viewModel.OnInternetStatusChanged(InternetStatus.Online);

        Assert.False(viewModel.IsCounting);
        Assert.Equal(0, _clock.ActiveTimerCount);
        Assert.True(viewModel.ContinueCommand.CanExecute(null));
    }

    [Fact]
    public async Task Simulation_CountsButNeverExits()
    {
        AutoContinueViewModel viewModel = Create(enabled: false);
        viewModel.IsSimulation = true;
        viewModel.OnInternetStatusChanged(InternetStatus.Online);
        Assert.True(viewModel.IsCounting);

        Tick(10);
        Assert.Equal(AutoContinueStatus.Elapsed, viewModel.State.Status);
        await (viewModel.PendingCompletion ?? Task.CompletedTask);
        await viewModel.ContinueCommand.ExecuteAsync(null);

        Assert.Equal(0, _exit.Calls);
    }

    [Fact]
    public void ExitAlreadyRequested_DisablesContinue()
    {
        _lifetime.TryExit(ChildRuntimeExitCode.UserAborted);
        AutoContinueViewModel viewModel = Create();

        viewModel.OnInternetStatusChanged(InternetStatus.Online);

        Assert.False(viewModel.ContinueCommand.CanExecute(null));
    }

    // Reads the manual clock shifted by Skew, as a timer callback that fires slightly early or late would.
    private sealed class SkewedClock(ManualTimeProvider inner) : TimeProvider
    {
        public TimeSpan Skew { get; set; }

        public override DateTimeOffset GetUtcNow() => inner.GetUtcNow() + Skew;

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period) =>
            inner.CreateTimer(callback, state, dueTime, period);
    }
}
