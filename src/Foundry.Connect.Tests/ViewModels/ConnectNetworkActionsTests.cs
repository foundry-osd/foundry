// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Connect.Network;
using Foundry.Connect.Security;
using Foundry.Connect.Tests.Support;
using Foundry.Connect.ViewModels;

namespace Foundry.Connect.Tests.ViewModels;

public sealed class ConnectNetworkActionsTests
{
    private readonly FakeConnectionCoordinator _coordinator = new();

    [Fact]
    public async Task Actions_ReportInteractionThenReachTheCoordinator()
    {
        var actions = new ConnectNetworkActions(_coordinator, new FakeUnlockService(), new RecordingLogger<ConnectNetworkActions>());
        int interactions = 0;
        actions.InteractionOccurred += (_, _) => interactions++;

        await actions.ConnectProvisionedWifiAsync(TestContext.Current.CancellationToken);
        await actions.ConnectWifiNetworkAsync(WifiSsid.FromText("Lab"), " pass word ", TestContext.Current.CancellationToken);
        await actions.DisconnectWifiAsync(TestContext.Current.CancellationToken);

        Assert.Equal(3, interactions);
        Assert.Equal(1, _coordinator.ConnectProvisionedCalls);
        Assert.Equal((WifiSsid.FromText("Lab"), " pass word "), Assert.Single(_coordinator.ConnectNetworkCalls));
        Assert.Equal(1, _coordinator.DisconnectCalls);
    }

    [Fact]
    public async Task Simulation_SkipsTheCoordinatorAndLogsWithoutSsid()
    {
        var logger = new RecordingLogger<ConnectNetworkActions>();
        var actions = new ConnectNetworkActions(_coordinator, new FakeUnlockService(), logger) { IsSimulation = true };

        OperationOutcome outcome = await actions.ConnectWifiNetworkAsync(WifiSsid.FromText("Secret Lab"), "password1", TestContext.Current.CancellationToken);

        Assert.Equal(OperationStatus.NoChange, outcome.Status);
        Assert.Empty(_coordinator.ConnectNetworkCalls);
        Assert.DoesNotContain(logger.Entries, static entry => entry.Message.Contains("Secret Lab", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ConnectProvisionedWifi_WhenLocked_UnlocksRefreshesThenConnects()
    {
        var unlock = new FakeUnlockService { IsWifiUnlockRequired = true, Result = ConnectUnlockResult.Unlocked };
        _coordinator.Publish(LockedState());
        var actions = new ConnectNetworkActions(_coordinator, unlock, new RecordingLogger<ConnectNetworkActions>());
        int interactions = 0;
        actions.InteractionOccurred += (_, _) => interactions++;
        int refreshesAtUnlock = -1;
        unlock.OnUnlock = () => refreshesAtUnlock = _coordinator.RefreshProvisionedWifiCalls;

        OperationOutcome outcome = await actions.ConnectProvisionedWifiAsync(TestContext.Current.CancellationToken);

        Assert.Equal(OperationStatus.Succeeded, outcome.Status);
        Assert.Equal([ConnectUnlockTrigger.ManualWifi], unlock.Calls);
        Assert.Equal(0, refreshesAtUnlock);
        Assert.Equal(1, _coordinator.RefreshProvisionedWifiCalls);
        Assert.Equal(1, _coordinator.ConnectProvisionedCalls);
        Assert.Equal(1, interactions);
    }

    [Fact]
    public async Task ConnectProvisionedWifi_WhenUnlockIsCancelled_DoesNotConnect()
    {
        var unlock = new FakeUnlockService { IsWifiUnlockRequired = true, Result = ConnectUnlockResult.Cancelled };
        _coordinator.Publish(LockedState());
        var actions = new ConnectNetworkActions(_coordinator, unlock, new RecordingLogger<ConnectNetworkActions>());

        OperationOutcome outcome = await actions.ConnectProvisionedWifiAsync(TestContext.Current.CancellationToken);

        Assert.Equal(OperationStatus.Cancelled, outcome.Status);
        Assert.Equal(1, _coordinator.RefreshProvisionedWifiCalls);
        Assert.Equal(0, _coordinator.ConnectProvisionedCalls);
    }

    [Fact]
    public async Task ConnectProvisionedWifi_WhenAConcurrentAutomaticUnlockSucceeded_RefreshesThenConnects()
    {
        // The manual call waited behind the automatic prompt, which unlocked everything: nothing is locked any more.
        var unlock = new FakeUnlockService { IsWifiUnlockRequired = false, Result = ConnectUnlockResult.NotRequired };
        _coordinator.Publish(LockedState());
        var actions = new ConnectNetworkActions(_coordinator, unlock, new RecordingLogger<ConnectNetworkActions>());

        OperationOutcome outcome = await actions.ConnectProvisionedWifiAsync(TestContext.Current.CancellationToken);

        Assert.Equal(OperationStatus.Succeeded, outcome.Status);
        Assert.Equal([ConnectUnlockTrigger.ManualWifi], unlock.Calls);
        Assert.Equal(1, _coordinator.RefreshProvisionedWifiCalls);
        Assert.Equal(1, _coordinator.ConnectProvisionedCalls);
    }

    [Fact]
    public async Task ConnectProvisionedWifi_WhenTheCallerCancelsDuringUnlock_ReturnsCancelled()
    {
        using var cancellation = new CancellationTokenSource();
        var unlock = new FakeUnlockService
        {
            IsWifiUnlockRequired = true,
            OnUnlock = () =>
            {
                cancellation.Cancel();
                throw new OperationCanceledException(cancellation.Token);
            }
        };
        _coordinator.Publish(LockedState());
        var actions = new ConnectNetworkActions(_coordinator, unlock, new RecordingLogger<ConnectNetworkActions>());

        OperationOutcome outcome = await actions.ConnectProvisionedWifiAsync(cancellation.Token);

        Assert.Equal(OperationStatus.Cancelled, outcome.Status);
        Assert.Equal(0, _coordinator.ConnectProvisionedCalls);
    }

    [Fact]
    public async Task UnlockProvisionedWifi_RefreshesWithoutReportingAnInteraction()
    {
        var unlock = new FakeUnlockService { IsWifiUnlockRequired = true, Result = ConnectUnlockResult.Unlocked };
        _coordinator.Publish(LockedState());
        var actions = new ConnectNetworkActions(_coordinator, unlock, new RecordingLogger<ConnectNetworkActions>());
        int interactions = 0;
        actions.InteractionOccurred += (_, _) => interactions++;

        Assert.Equal(ConnectUnlockResult.Unlocked, await actions.UnlockProvisionedWifiAsync(TestContext.Current.CancellationToken));

        Assert.Equal([ConnectUnlockTrigger.AutomaticWifi], unlock.Calls);
        Assert.Equal(1, _coordinator.RefreshProvisionedWifiCalls);
        Assert.Equal(0, interactions);
    }

    [Fact]
    public async Task UnlockProvisionedWifi_WhenNothingIsLocked_NeverPrompts()
    {
        var unlock = new FakeUnlockService { Result = ConnectUnlockResult.Unlocked };
        var actions = new ConnectNetworkActions(_coordinator, unlock, new RecordingLogger<ConnectNetworkActions>());
        int interactions = 0;
        actions.InteractionOccurred += (_, _) => interactions++;

        Assert.Equal(ConnectUnlockResult.NotRequired, await actions.UnlockProvisionedWifiAsync(TestContext.Current.CancellationToken));

        Assert.Empty(unlock.Calls);
        Assert.Equal(0, _coordinator.RefreshProvisionedWifiCalls);
        Assert.Equal(0, interactions);
    }

    [Fact]
    public async Task UnlockProvisionedWifi_InSimulation_NeverPrompts()
    {
        var unlock = new FakeUnlockService { IsWifiUnlockRequired = true, Result = ConnectUnlockResult.Unlocked };
        var logger = new RecordingLogger<ConnectNetworkActions>();
        var actions = new ConnectNetworkActions(_coordinator, unlock, logger) { IsSimulation = true };

        Assert.Equal(ConnectUnlockResult.NotRequired, await actions.UnlockProvisionedWifiAsync(TestContext.Current.CancellationToken));
        Assert.Empty(unlock.Calls);
        Assert.Empty(logger.Entries);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UnlockWiredAtStartup_PromptsOnlyWhenRequiredWithoutReportingAnInteraction(bool required)
    {
        var unlock = new FakeUnlockService { IsWiredUnlockRequired = required, Result = ConnectUnlockResult.Cancelled };
        var actions = new ConnectNetworkActions(_coordinator, unlock, new RecordingLogger<ConnectNetworkActions>());
        int interactions = 0;
        actions.InteractionOccurred += (_, _) => interactions++;

        ConnectUnlockResult result = await actions.UnlockWiredAtStartupAsync(TestContext.Current.CancellationToken);

        Assert.Equal(required ? ConnectUnlockResult.Cancelled : ConnectUnlockResult.NotRequired, result);
        Assert.Equal(required ? [ConnectUnlockTrigger.WiredStartup] : [], unlock.Calls);
        Assert.Equal(0, interactions);
        Assert.Equal(0, _coordinator.RefreshProvisionedWifiCalls);
    }

    private static NetworkState LockedState() => NetworkState.Initial(new DateTimeOffset(2026, 10, 29, 8, 0, 0, TimeSpan.Zero)) with
    {
        Wifi = new WifiState(WifiAvailability.Available, null, [],
            new ProvisionedWifiState(ProvisionedWifiStatus.Locked, ProvisionedWifiUnavailableReason.None, WifiSsid.FromText("Lab")))
    };
}
