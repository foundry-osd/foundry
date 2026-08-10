// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Connect.Models.Readiness;

public enum ConnectReadinessState
{
    Refreshing,
    WaitingForNetwork,
    Ready,
    RefreshFailed
}
