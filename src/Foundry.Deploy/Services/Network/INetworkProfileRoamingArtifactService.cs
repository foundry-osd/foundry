using Foundry.Deploy.Models.Configuration;
using Foundry.Deploy.Services.Deployment.PreOobe;

namespace Foundry.Deploy.Services.Network;

public interface INetworkProfileRoamingArtifactService
{
    Task<PreOobeNetworkProfileRoamingPayload?> LoadAsync(
        DeployNetworkProfileRoamingSettings settings,
        string workspaceRootPath,
        CancellationToken cancellationToken = default);
}
