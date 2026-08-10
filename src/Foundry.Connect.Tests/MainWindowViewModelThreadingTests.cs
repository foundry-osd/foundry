// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Globalization;
using Foundry.Connect.Models;

namespace Foundry.Connect.Tests;

public sealed class MainWindowViewModelThreadingTests
{
    [Fact]
    public async Task InitializeAsync_WhenCalledOutsideUiThread_AppliesSnapshotThroughDispatcher()
    {
        var dispatcher = new MainWindowViewModelTestContext.RecordingUiDispatcher(checkAccess: false);
        var context = new MainWindowViewModelTestContext(
            networkStatusService: new MainWindowViewModelTestContext.QueueNetworkStatusService(
                MainWindowViewModelTestContext.CreateSnapshot()),
            dispatcher: dispatcher);

        await context.ViewModel.InitializeAsync();

        Assert.True(dispatcher.InvokeCalls >= 1);
        context.ViewModel.Dispose();
    }

    [Fact]
    public void LanguageChange_WhenCalledOutsideUiThread_PostsLocalizedPropertyUpdates()
    {
        var dispatcher = new MainWindowViewModelTestContext.RecordingUiDispatcher(checkAccess: false);
        var context = new MainWindowViewModelTestContext(dispatcher: dispatcher);

        string cultureName = context.LocalizationService.CurrentCulture.Name == "fr-FR" ? "en-US" : "fr-FR";
        context.LocalizationService.SetCulture(CultureInfo.GetCultureInfo(cultureName));

        Assert.True(dispatcher.PostCalls >= 1);
        context.ViewModel.Dispose();
    }

    [Fact]
    public async Task ReadyCountdown_UsesUiTimerAndExitsAfterFinalTick()
    {
        var timerFactory = new MainWindowViewModelTestContext.RecordingUiTimerFactory();
        var context = new MainWindowViewModelTestContext(
            networkStatusService: new MainWindowViewModelTestContext.QueueNetworkStatusService(
                MainWindowViewModelTestContext.CreateSnapshot(hasInternetAccess: true)),
            timerFactory: timerFactory);
        await context.ViewModel.InitializeAsync();
        MainWindowViewModelTestContext.RecordingUiTimer timer = Assert.Single(timerFactory.Timers);

        for (int index = 0; index < FoundryConnectApplicationInfo.DefaultAutoContinueDelaySeconds; index++)
        {
            timer.Fire();
        }

        Assert.True(context.LifetimeService.IsExitRequested);
        Assert.Equal(FoundryConnectExitCode.Success, context.LifetimeService.ExitCode);
        Assert.True(timer.IsDisposed);
        context.ViewModel.Dispose();
    }

    [Fact]
    public async Task Dispose_WhenCountdownIsActive_StopsAndDisposesUiTimer()
    {
        var timerFactory = new MainWindowViewModelTestContext.RecordingUiTimerFactory();
        var context = new MainWindowViewModelTestContext(
            networkStatusService: new MainWindowViewModelTestContext.QueueNetworkStatusService(
                MainWindowViewModelTestContext.CreateSnapshot(hasInternetAccess: true)),
            timerFactory: timerFactory);
        await context.ViewModel.InitializeAsync();
        MainWindowViewModelTestContext.RecordingUiTimer timer = Assert.Single(timerFactory.Timers);

        context.ViewModel.Dispose();

        Assert.False(timer.IsEnabled);
        Assert.True(timer.IsDisposed);
    }
}
