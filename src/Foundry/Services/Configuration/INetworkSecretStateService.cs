// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Models.Configuration;
using Foundry.Core.Services.Configuration;

namespace Foundry.Services.Configuration;

/// <summary>
/// Keeps the network passwords that never enter the authoring document; the secret vault bridge remembers them in the local vault.
/// </summary>
public interface INetworkSecretStateService
{
    /// <summary>
    /// Occurs when the effective in-memory network secrets change.
    /// </summary>
    event EventHandler? Changed;

    /// <summary>
    /// Gets the in-memory personal Wi-Fi passphrase captured from the UI.
    /// </summary>
    string? PersonalWifiPassphrase { get; }

    /// <summary>
    /// Drops passwords whose SSID or certificate changed in <paramref name="settings"/> and records the non-null values the operator entered.
    /// </summary>
    /// <param name="settings">Network settings the passwords belong to.</param>
    /// <param name="entered">Passwords entered on the network pages, or <see langword="null"/> to only drop stale ones.</param>
    void Update(NetworkSettings settings, NetworkSecrets? entered = null);

    /// <summary>
    /// Removes the in-memory personal Wi-Fi passphrase.
    /// </summary>
    void ClearPersonalWifiPassphrase();

    /// <summary>
    /// Gets the in-memory passwords that apply to <paramref name="settings"/> for validation and provisioning output.
    /// </summary>
    /// <param name="settings">Network settings that may need passwords.</param>
    /// <returns>The passwords bound to the same SSID and certificates.</returns>
    NetworkSecrets GetSecrets(NetworkSettings settings);

    /// <summary>
    /// Gets every password still bound to <paramref name="settings"/> (same SSID, same certificate), even for a disabled feature;
    /// the secret vault bridge persists these.
    /// </summary>
    /// <param name="settings">Network settings whose SSID and certificates own the passwords.</param>
    NetworkSecrets GetRetainedSecrets(NetworkSettings settings);
}
