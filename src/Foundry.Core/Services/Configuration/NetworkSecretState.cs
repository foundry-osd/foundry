// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Models.Configuration;

namespace Foundry.Core.Services.Configuration;

/// <summary>
/// Keeps the network passwords that never enter the authoring document, each bound to what it unlocks.
/// </summary>
/// <remarks>
/// The personal passphrase is bound to the SSID and each PFX password to its certificate content hash, so a password
/// never follows a different network or certificate.
/// </remarks>
public sealed class NetworkSecretState
{
    private string? wifiIdentity;
    private string? wiredCertificateHash;
    private string? wifiCertificateHash;
    private string? wiredCertificatePassword;
    private string? wifiCertificatePassword;

    /// <summary>Gets the personal Wi-Fi passphrase recorded for the current SSID.</summary>
    public string? PersonalWifiPassphrase { get; private set; }

    /// <summary>
    /// Drops passwords that no longer match <paramref name="settings"/>, then records the non-null values of <paramref name="entered"/>.
    /// </summary>
    /// <remarks>A blank passphrase leaves the recorded one; a blank PFX password is kept, as a PFX may have an empty password.</remarks>
    public void Update(NetworkSettings settings, NetworkSecrets? entered = null)
    {
        ArgumentNullException.ThrowIfNull(settings);

        UpdateCertificate(settings.Dot1x.IsEnabled && settings.Dot1x.RequiresCertificate ? settings.Dot1x.Certificate : null,
            entered?.WiredCertificatePfxPassword, ref wiredCertificateHash, ref wiredCertificatePassword);
        UpdateCertificate(settings.Wifi.IsEnabled && settings.Wifi.RequiresCertificate ? settings.Wifi.Certificate : null,
            entered?.WifiCertificatePfxPassword, ref wifiCertificateHash, ref wifiCertificatePassword);
        string identity = settings.Wifi.Ssid ?? string.Empty;
        if (!string.Equals(wifiIdentity, identity, StringComparison.Ordinal))
        {
            PersonalWifiPassphrase = null;
            wifiIdentity = identity;
        }

        if (!NetworkMediaReadinessEvaluator.RequiresPersonalWifiPassphrase(settings))
        {
            PersonalWifiPassphrase = null;
            return;
        }

        if (!string.IsNullOrWhiteSpace(entered?.WifiPassphrase))
        {
            PersonalWifiPassphrase = entered.WifiPassphrase.Trim();
        }
    }

    /// <summary>Removes the personal Wi-Fi passphrase.</summary>
    public void ClearPersonalWifiPassphrase()
    {
        PersonalWifiPassphrase = null;
    }

    /// <summary>
    /// Returns the passwords that apply to <paramref name="settings"/>: the passphrase when personal Wi-Fi needs a valid one
    /// for the same SSID, and each PFX password whose certificate is still the one it was entered for.
    /// </summary>
    public NetworkSecrets GetSecrets(NetworkSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        bool passphraseApplies = NetworkMediaReadinessEvaluator.RequiresPersonalWifiPassphrase(settings) &&
            string.Equals(wifiIdentity, settings.Wifi.Ssid ?? string.Empty, StringComparison.Ordinal) &&
            NetworkMediaReadinessEvaluator.IsPersonalWifiPassphraseValid(PersonalWifiPassphrase);
        return new NetworkSecrets
        {
            WifiPassphrase = passphraseApplies ? PersonalWifiPassphrase!.Trim() : null,
            WiredCertificatePfxPassword = Matches(wiredCertificateHash, settings.Dot1x.Certificate) ? wiredCertificatePassword : null,
            WifiCertificatePfxPassword = Matches(wifiCertificateHash, settings.Wifi.Certificate) ? wifiCertificatePassword : null
        };
    }

    private static bool Matches(string? hash, AuthoringFileReference? certificate) =>
        hash is not null && string.Equals(hash, certificate?.ContentHash, StringComparison.OrdinalIgnoreCase);

    private static void UpdateCertificate(AuthoringFileReference? certificate, string? password, ref string? previousHash, ref string? previousPassword)
    {
        string? hash = certificate?.ContentHash;
        if (!string.Equals(hash, previousHash, StringComparison.OrdinalIgnoreCase))
        {
            previousPassword = null;
            previousHash = hash;
        }

        if (hash is null)
        {
            previousPassword = null;
        }
        else if (password is not null)
        {
            previousPassword = password;
        }
    }
}
