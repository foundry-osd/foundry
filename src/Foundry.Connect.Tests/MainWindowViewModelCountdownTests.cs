// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Connect.Models;

namespace Foundry.Connect.Tests;

public sealed class MainWindowViewModelCountdownTests
{
    [Fact]
    public async Task RefreshStatusCommand_WhenReadyStateIsLost_CancelsCountdown()
    {
        var context = new MainWindowViewModelTestContext(
            networkStatusService: new MainWindowViewModelTestContext.QueueNetworkStatusService(
                MainWindowViewModelTestContext.CreateSnapshot(hasInternetAccess: true),
                MainWindowViewModelTestContext.CreateSnapshot()));
        await context.ViewModel.InitializeAsync();
        Assert.True(context.ViewModel.IsCountdownActive);

        await context.ViewModel.RefreshStatusCommand.ExecuteAsync(null);

        Assert.False(context.ViewModel.IsCountdownActive);
        Assert.Equal(0, context.ViewModel.CountdownSecondsRemaining);
        Assert.False(context.LifetimeService.IsExitRequested);
        context.ViewModel.Dispose();
    }

    [Fact]
    public async Task ContinueBootstrapCommand_WhenReady_ExitsSuccessfullyWithoutWaitingForCountdown()
    {
        var context = new MainWindowViewModelTestContext(
            networkStatusService: new MainWindowViewModelTestContext.QueueNetworkStatusService(
                MainWindowViewModelTestContext.CreateSnapshot(hasInternetAccess: true)));
        await context.ViewModel.InitializeAsync();

        await context.ViewModel.ContinueBootstrapCommand.ExecuteAsync(null);

        Assert.True(context.LifetimeService.IsExitRequested);
        Assert.Equal(FoundryConnectExitCode.Success, context.LifetimeService.ExitCode);
        Assert.Single(context.TelemetryService.Events);
        context.ViewModel.Dispose();
    }

    [Fact]
    public void HandleWindowClosing_WhenExitWasNotRequested_RequestsUserAbortedOnce()
    {
        var context = new MainWindowViewModelTestContext();

        context.ViewModel.HandleWindowClosing();
        context.ViewModel.HandleWindowClosing();

        Assert.Equal(1, context.LifetimeService.ExitCalls);
        Assert.Equal(FoundryConnectExitCode.UserAborted, context.LifetimeService.ExitCode);
        context.ViewModel.Dispose();
    }
}
