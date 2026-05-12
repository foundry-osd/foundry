using Foundry.Core.Models.Configuration;

namespace Foundry.Services.Configuration;

/// <summary>
/// Keeps transient network secrets available without persisting them back to configuration JSON.
/// </summary>
public interface INetworkSecretStateService
{
    /// <summary>
    /// Gets the in-memory personal Wi-Fi passphrase captured from the UI.
    /// </summary>
    string? PersonalWifiPassphrase { get; }

    /// <summary>
    /// Captures required secrets from the current network settings.
    /// </summary>
    /// <param name="settings">Network settings to inspect.</param>
    void Update(NetworkSettings settings);

    /// <summary>
    /// Removes the in-memory personal Wi-Fi passphrase.
    /// </summary>
    void ClearPersonalWifiPassphrase();

    /// <summary>
    /// Applies required in-memory secrets to a network settings document before provisioning output is generated.
    /// </summary>
    /// <param name="settings">Network settings that may need secret values restored.</param>
    /// <returns>Network settings with required secrets applied.</returns>
    NetworkSettings ApplyRequiredSecrets(NetworkSettings settings);
}
