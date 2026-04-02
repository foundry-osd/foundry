using Foundry.Connect.Models.Network;

namespace Foundry.Connect.Services.Network;

public interface INetworkStatusService
{
    Task<NetworkStatusSnapshot> GetSnapshotAsync(CancellationToken cancellationToken);
}
