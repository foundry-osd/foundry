// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Connect.Models;

namespace Foundry.Connect.Tests;

public sealed class MainWindowViewModelReadinessTests
{
    [Fact]
    public async Task InitializeAsync_WhenInternetProbeSucceeds_EnablesContinueAndStartsCountdown()
    {
        var context = new MainWindowViewModelTestContext(
            networkStatusService: new MainWindowViewModelTestContext.QueueNetworkStatusService(
                MainWindowViewModelTestContext.CreateSnapshot(hasInternetAccess: true)));

        await context.ViewModel.InitializeAsync();

        Assert.True(context.ViewModel.HasInternetAccess);
        Assert.True(context.ViewModel.CanContinueBootstrap);
        Assert.True(context.ViewModel.IsPrimaryStatusSuccessful);
        Assert.True(context.ViewModel.IsCountdownActive);
        context.ViewModel.Dispose();
    }

    [Theory]
    [InlineData(NetworkLayoutMode.EthernetOnly, false, false)]
    [InlineData(NetworkLayoutMode.EthernetWifi, false, true)]
    [InlineData(NetworkLayoutMode.EthernetWifi, true, false)]
    public async Task InitializeAsync_WhenInternetProbeFails_ProjectsWaitingState(
        NetworkLayoutMode layoutMode,
        bool wifiRuntimeAvailable,
        bool hasWirelessAdapter)
    {
        var context = new MainWindowViewModelTestContext(
            networkStatusService: new MainWindowViewModelTestContext.QueueNetworkStatusService(
                MainWindowViewModelTestContext.CreateSnapshot(
                    layoutMode: layoutMode,
                    wifiRuntimeAvailable: wifiRuntimeAvailable,
                    hasWirelessAdapter: hasWirelessAdapter)));

        await context.ViewModel.InitializeAsync();

        Assert.False(context.ViewModel.CanContinueBootstrap);
        Assert.False(context.ViewModel.IsPrimaryStatusSuccessful);
        Assert.False(context.ViewModel.IsCountdownActive);
        context.ViewModel.Dispose();
    }

    [Fact]
    public async Task RefreshStatusCommand_WhenRefreshFailsAfterReady_PreservesContinueReadiness()
    {
        var failure = new InvalidOperationException("probe failed");
        var context = new MainWindowViewModelTestContext(
            networkStatusService: new MainWindowViewModelTestContext.QueueNetworkStatusService(
                MainWindowViewModelTestContext.CreateSnapshot(hasInternetAccess: true),
                failure));
        await context.ViewModel.InitializeAsync();

        await context.ViewModel.RefreshStatusCommand.ExecuteAsync(null);

        Assert.True(context.ViewModel.HasInternetAccess);
        Assert.True(context.ViewModel.CanContinueBootstrap);
        Assert.False(context.ViewModel.IsPrimaryStatusSuccessful);
        Assert.Equal(failure.Message, context.ViewModel.PrimaryStatusDescription);
        context.ViewModel.Dispose();
    }

    [Fact]
    public async Task InitializeAsync_WhenInternetIsAlreadyReady_DoesNotRetryProvisionedWifi()
    {
        var context = new MainWindowViewModelTestContext(
            networkStatusService: new MainWindowViewModelTestContext.QueueNetworkStatusService(
                MainWindowViewModelTestContext.CreateSnapshot(hasInternetAccess: true)));

        await context.ViewModel.InitializeAsync();

        Assert.Equal(0, context.BootstrapService.ConnectConfiguredCalls);
        context.ViewModel.Dispose();
    }
}
