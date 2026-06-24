// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Core.Services.Configuration;

public sealed record NetworkMediaReadinessEvaluation
{
    public bool IsNetworkConfigurationReady { get; init; }
    public bool IsConnectProvisioningReady { get; init; }
    public bool AreRequiredSecretsReady { get; init; }
}
