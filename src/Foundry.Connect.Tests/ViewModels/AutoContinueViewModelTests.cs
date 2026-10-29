// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Connect.Lifetime;
using Foundry.Connect.Network;
using Foundry.Connect.Tests.Support;
using Foundry.Connect.ViewModels;
using Foundry.Core.Models.Runtime;
using Foundry.Wpf.Localization;

namespace Foundry.Connect.Tests.ViewModels;

public sealed class AutoContinueViewModelTests : IDisposable
{
    private readonly ManualTimeProvider _clock = new();
    private readonly FakeSuccessfulExitService _exit = new();
    private readonly FakeApplicationLifetime _lifetime = new();
    private readonly ResourceLocalizationService _localization = TestLocalization.Create();
    private AutoContinueViewModel? _viewModel;

    public void Dispose() => _viewModel?.Dispose();

    private AutoContinueViewModel Create(bool enabled = true) =>
        _viewModel = new AutoContinueViewModel(_localization, _exit, _lifetime, _clock, new ImmediateUiDispatcher(), new RecordingLogger<AutoContinueViewModel>(), enabled);

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
        Assert.True(viewModel.IsPaused);
        Assert.Equal(_localization.GetString("Status.AutoContinuePaused"), viewModel.CaptionText);
        Assert.Equal(0, _clock.ActiveTimerCount);
        Assert.False(viewModel.IsContinueDefault);
        Assert.True(viewModel.ContinueCommand.CanExecute(null));
    }

    [Fact]
    public void NetworkingInteraction_BeforeOnline_DoesNotPause()
    {
        AutoContinueViewModel viewModel = Create();

        viewModel.OnNetworkingInteraction();
        viewModel.OnInternetStatusChanged(InternetStatus.Online);

        Assert.True(viewModel.IsCounting);
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
        Assert.True(viewModel.IsCancelled);
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
}
