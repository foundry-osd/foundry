// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Models.Configuration;
using Foundry.Core.Services.Configuration;

namespace Foundry.Services.Configuration;

/// <summary>
/// Holds volatile network secrets that are required during provisioning but should not be saved to disk.
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
        state.Update(settings, entered);
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
