// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Connect.Network;
using Foundry.Connect.Tests.Support;
using Foundry.Connect.ViewModels;

namespace Foundry.Connect.Tests.ViewModels;

public sealed class ConnectNetworkActionsTests
{
    private readonly FakeConnectionCoordinator _coordinator = new();

    [Fact]
    public async Task Actions_ReportInteractionThenReachTheCoordinator()
    {
        var actions = new ConnectNetworkActions(_coordinator, new RecordingLogger<ConnectNetworkActions>());
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
        var actions = new ConnectNetworkActions(_coordinator, logger) { IsSimulation = true };

        OperationOutcome outcome = await actions.ConnectWifiNetworkAsync(WifiSsid.FromText("Secret Lab"), "password1", TestContext.Current.CancellationToken);

        Assert.Equal(OperationStatus.NoChange, outcome.Status);
        Assert.Empty(_coordinator.ConnectNetworkCalls);
        Assert.DoesNotContain(logger.Entries, static entry => entry.Message.Contains("Secret Lab", StringComparison.Ordinal));
    }
}
