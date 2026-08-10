// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Connect.Models.Network;
using Foundry.Connect.Models.Readiness;

namespace Foundry.Connect.Services.Readiness;

public interface IConnectReadinessEvaluator
{
    ConnectReadinessDecision Evaluate(
        NetworkStatusSnapshot snapshot,
        bool isRefreshInProgress,
        bool refreshFailed,
        bool hasProvisionedProfile);
}
