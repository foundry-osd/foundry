// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using CoreDeployNetworkProfileRoamingSettings = Foundry.Core.Models.Configuration.Deploy.DeployNetworkProfileRoamingSettings;
using Foundry.Deploy.Services.Deployment.PreOobe;

namespace Foundry.Deploy.Services.Network;

public interface INetworkProfileRoamingArtifactService
{
    Task<PreOobeNetworkProfileRoamingPayload?> LoadAsync(
        CoreDeployNetworkProfileRoamingSettings settings,
        string workspaceRootPath,
        CancellationToken cancellationToken = default);
}
