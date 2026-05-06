using Foundry.Core.Models.Configuration;
using Foundry.Core.Services.Configuration;

namespace Foundry.Services.Configuration;

internal sealed class NetworkSecretStateService : INetworkSecretStateService
{
    private readonly NetworkSecretState state = new();

    public string? PersonalWifiPassphrase => state.PersonalWifiPassphrase;

    public void Update(NetworkSettings settings)
    {
        state.Update(settings);
    }

    public void ClearPersonalWifiPassphrase()
    {
        state.ClearPersonalWifiPassphrase();
    }

    public NetworkSettings ApplyRequiredSecrets(NetworkSettings settings)
    {
        return state.ApplyRequiredSecrets(settings);
    }
}
