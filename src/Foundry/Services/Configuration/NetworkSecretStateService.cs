using Foundry.Core.Models.Configuration;
using Foundry.Core.Services.Configuration;

namespace Foundry.Services.Configuration;

/// <summary>
/// Holds volatile network secrets that are required during provisioning but should not be saved to disk.
/// </summary>
internal sealed class NetworkSecretStateService : INetworkSecretStateService
{
    private readonly NetworkSecretState state = new();

    /// <inheritdoc />
    public string? PersonalWifiPassphrase => state.PersonalWifiPassphrase;

    /// <inheritdoc />
    public void Update(NetworkSettings settings)
    {
        state.Update(settings);
    }

    /// <inheritdoc />
    public void ClearPersonalWifiPassphrase()
    {
        state.ClearPersonalWifiPassphrase();
    }

    /// <inheritdoc />
    public NetworkSettings ApplyRequiredSecrets(NetworkSettings settings)
    {
        return state.ApplyRequiredSecrets(settings);
    }
}
