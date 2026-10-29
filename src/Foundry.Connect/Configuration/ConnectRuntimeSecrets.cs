// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Connect.Configuration;

/// <summary>
/// Holds the session-only network secrets resolved while loading the Foundry.Connect configuration.
/// </summary>
/// <remarks>
/// The Core runtime configuration carries secrets only as encrypted envelopes. Decrypted values and the legacy
/// plaintext Wi-Fi passphrase of older media live here instead, so they never round-trip through the shared
/// configuration contract. Each secret has its own availability: one that cannot be read disables only the feature
/// that needs it (D9). This is deliberately a class whose <c>ToString</c> prints no value.
/// </remarks>
public sealed class ConnectRuntimeSecrets
{
    /// <summary>
    /// Initializes the secrets holder.
    /// </summary>
    /// <param name="wifiPassphrase">The personal Wi-Fi passphrase.</param>
    /// <param name="wifiCertificatePfxPassword">The PFX password of the enterprise Wi-Fi certificate.</param>
    /// <param name="wiredCertificatePfxPassword">The PFX password of the wired 802.1X certificate.</param>
    public ConnectRuntimeSecrets(ConnectSecret wifiPassphrase, ConnectSecret wifiCertificatePfxPassword, ConnectSecret wiredCertificatePfxPassword)
    {
        ArgumentNullException.ThrowIfNull(wifiPassphrase);
        ArgumentNullException.ThrowIfNull(wifiCertificatePfxPassword);
        ArgumentNullException.ThrowIfNull(wiredCertificatePfxPassword);
        WifiPassphrase = wifiPassphrase;
        WifiCertificatePfxPassword = wifiCertificatePfxPassword;
        WiredCertificatePfxPassword = wiredCertificatePfxPassword;
    }

    /// <summary>
    /// Gets the personal Wi-Fi passphrase, decrypted from the media envelope or read from legacy plaintext media.
    /// </summary>
    public ConnectSecret WifiPassphrase { get; }

    /// <summary>
    /// Gets the decrypted PFX password of the enterprise Wi-Fi certificate.
    /// </summary>
    public ConnectSecret WifiCertificatePfxPassword { get; }

    /// <summary>
    /// Gets the decrypted PFX password of the wired 802.1X certificate.
    /// </summary>
    public ConnectSecret WiredCertificatePfxPassword { get; }

    /// <summary>
    /// Creates a holder in which no secret is provisioned.
    /// </summary>
    public static ConnectRuntimeSecrets None() =>
        new(ConnectSecret.NotProvisioned(), ConnectSecret.NotProvisioned(), ConnectSecret.NotProvisioned());

    /// <summary>
    /// Zeroes every available secret.
    /// </summary>
    public void ClearAll()
    {
        WifiPassphrase.Clear();
        WifiCertificatePfxPassword.Clear();
        WiredCertificatePfxPassword.Clear();
    }

    /// <inheritdoc />
    public override string ToString() => nameof(ConnectRuntimeSecrets);
}
