// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Models.Configuration;
using Foundry.Core.Services.Configuration;

namespace Foundry.Services.Configuration;

/// <summary>
/// Holds the network secrets required during provisioning; they never enter the configuration, and the secret vault bridge
/// remembers them in the local vault.
/// </summary>
internal sealed class NetworkSecretStateService : INetworkSecretStateService
{
    private readonly NetworkSecretState state = new();

    public event EventHandler? Changed;

    /// <inheritdoc />
    public string? PersonalWifiPassphrase => state.PersonalWifiPassphrase;

    /// <inheritdoc />
    public void Update(NetworkSettings settings, NetworkSecrets? entered = null)
    {
        string? previousPassphrase = state.PersonalWifiPassphrase;
        NetworkSecrets previousSecrets = state.GetSecrets(settings);
        state.Update(settings, entered);
        if (previousSecrets != state.GetSecrets(settings))
        {
            Changed?.Invoke(this, EventArgs.Empty);
            return;
        }

        RaiseChangedIfNeeded(previousPassphrase);
    }

    /// <inheritdoc />
    public void ClearPersonalWifiPassphrase()
    {
        string? previousPassphrase = state.PersonalWifiPassphrase;
        state.ClearPersonalWifiPassphrase();
        RaiseChangedIfNeeded(previousPassphrase);
    }

    /// <inheritdoc />
    public NetworkSecrets GetSecrets(NetworkSettings settings)
    {
        return state.GetSecrets(settings);
    }

    private void RaiseChangedIfNeeded(string? previousPassphrase)
    {
        if (!string.Equals(previousPassphrase, state.PersonalWifiPassphrase, StringComparison.Ordinal))
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }
}
