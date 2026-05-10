using Foundry.Core.Models.Configuration;

namespace Foundry.Services.Configuration;

public interface INetworkSecretStateService
{
    string? PersonalWifiPassphrase { get; }

    void Update(NetworkSettings settings);

    void ClearPersonalWifiPassphrase();

    NetworkSettings ApplyRequiredSecrets(NetworkSettings settings);
}
