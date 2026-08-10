// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Connect.Models;
using Foundry.Connect.Models.Readiness;
using Foundry.Connect.Services.Readiness;

namespace Foundry.Connect.Tests;

public sealed class ConnectReadinessEvaluatorTests
{
    private readonly ConnectReadinessEvaluator _evaluator = new();

    [Fact]
    public void Evaluate_WhenRefreshIsInProgress_ReturnsRefreshing()
    {
        ConnectReadinessDecision decision = _evaluator.Evaluate(
            MainWindowViewModelTestContext.CreateSnapshot(hasInternetAccess: true),
            isRefreshInProgress: true,
            refreshFailed: false,
            hasProvisionedProfile: true);

        Assert.Equal(ConnectReadinessState.Refreshing, decision.State);
        Assert.False(decision.CanContinue);
        Assert.False(decision.ShouldStartCountdown);
        Assert.False(decision.ShouldRetryProvisionedWifi);
    }

    [Fact]
    public void Evaluate_WhenRefreshFailed_ReturnsRefreshFailed()
    {
        ConnectReadinessDecision decision = _evaluator.Evaluate(
            MainWindowViewModelTestContext.CreateSnapshot(),
            isRefreshInProgress: false,
            refreshFailed: true,
            hasProvisionedProfile: true);

        Assert.Equal(ConnectReadinessState.RefreshFailed, decision.State);
        Assert.False(decision.CanContinue);
        Assert.False(decision.ShouldStartCountdown);
        Assert.False(decision.ShouldRetryProvisionedWifi);
    }

    [Fact]
    public void Evaluate_WhenInternetIsAvailable_ReturnsReady()
    {
        ConnectReadinessDecision decision = _evaluator.Evaluate(
            MainWindowViewModelTestContext.CreateSnapshot(hasInternetAccess: true),
            isRefreshInProgress: false,
            refreshFailed: false,
            hasProvisionedProfile: true);

        Assert.Equal(ConnectReadinessState.Ready, decision.State);
        Assert.True(decision.CanContinue);
        Assert.True(decision.ShouldStartCountdown);
        Assert.False(decision.ShouldRetryProvisionedWifi);
    }

    [Theory]
    [InlineData(NetworkLayoutMode.EthernetOnly, true, true, false)]
    [InlineData(NetworkLayoutMode.EthernetWifi, false, true, false)]
    [InlineData(NetworkLayoutMode.EthernetWifi, true, false, false)]
    [InlineData(NetworkLayoutMode.EthernetWifi, true, true, true)]
    public void Evaluate_WhenWaitingForNetwork_ClassifiesProvisionedWifiRetryEligibility(
        NetworkLayoutMode layoutMode,
        bool wifiRuntimeAvailable,
        bool hasProvisionedProfile,
        bool expectedRetry)
    {
        ConnectReadinessDecision decision = _evaluator.Evaluate(
            MainWindowViewModelTestContext.CreateSnapshot(
                layoutMode: layoutMode,
                wifiRuntimeAvailable: wifiRuntimeAvailable),
            isRefreshInProgress: false,
            refreshFailed: false,
            hasProvisionedProfile: hasProvisionedProfile);

        Assert.Equal(ConnectReadinessState.WaitingForNetwork, decision.State);
        Assert.False(decision.CanContinue);
        Assert.False(decision.ShouldStartCountdown);
        Assert.Equal(expectedRetry, decision.ShouldRetryProvisionedWifi);
    }
}
