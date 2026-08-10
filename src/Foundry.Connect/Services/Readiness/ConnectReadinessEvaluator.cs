// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Connect.Models;
using Foundry.Connect.Models.Network;
using Foundry.Connect.Models.Readiness;

namespace Foundry.Connect.Services.Readiness;

public sealed class ConnectReadinessEvaluator : IConnectReadinessEvaluator
{
    public ConnectReadinessDecision Evaluate(
        NetworkStatusSnapshot snapshot,
        bool isRefreshInProgress,
        bool refreshFailed,
        bool hasProvisionedProfile)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        if (isRefreshInProgress)
        {
            return new ConnectReadinessDecision(ConnectReadinessState.Refreshing, false, false, false);
        }

        if (refreshFailed)
        {
            return new ConnectReadinessDecision(ConnectReadinessState.RefreshFailed, false, false, false);
        }

        if (snapshot.HasInternetAccess)
        {
            return new ConnectReadinessDecision(ConnectReadinessState.Ready, true, true, false);
        }

        bool shouldRetryProvisionedWifi = snapshot.LayoutMode == NetworkLayoutMode.EthernetWifi &&
                                            snapshot.IsWifiRuntimeAvailable &&
                                            hasProvisionedProfile;
        return new ConnectReadinessDecision(
            ConnectReadinessState.WaitingForNetwork,
            false,
            false,
            shouldRetryProvisionedWifi);
    }
}
