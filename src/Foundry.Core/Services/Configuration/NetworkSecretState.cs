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
/// never follows a different network or certificate. A password is kept while its owner (the same SSID or the same certificate)
/// is still in the settings, even when its feature is disabled; <see cref="GetSecrets"/> returns only the passwords the enabled
/// features use, and <see cref="GetRetainedSecrets"/> every password still bound to the settings.
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
    /// Drops passwords whose SSID or certificate changed in <paramref name="settings"/>, then records the non-null values of <paramref name="entered"/>.
    /// </summary>
    /// <remarks>A blank passphrase leaves the recorded one; a blank PFX password is kept, as a PFX may have an empty password.</remarks>
    public void Update(NetworkSettings settings, NetworkSecrets? entered = null)
    {
        ArgumentNullException.ThrowIfNull(settings);

        UpdateCertificate(settings.Dot1x.Certificate, entered?.WiredCertificatePfxPassword, ref wiredCertificateHash, ref wiredCertificatePassword);
        UpdateCertificate(settings.Wifi.Certificate, entered?.WifiCertificatePfxPassword, ref wifiCertificateHash, ref wifiCertificatePassword);
        string identity = settings.Wifi.Ssid ?? string.Empty;
        if (!string.Equals(wifiIdentity, identity, StringComparison.Ordinal))
        {
            PersonalWifiPassphrase = null;
            wifiIdentity = identity;
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
    /// for the same SSID, and each PFX password whose feature requires its certificate and whose certificate is still the one
    /// it was entered for.
    /// </summary>
    public NetworkSecrets GetSecrets(NetworkSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        bool passphraseApplies = NetworkMediaReadinessEvaluator.RequiresPersonalWifiPassphrase(settings) &&
            string.Equals(wifiIdentity, settings.Wifi.Ssid ?? string.Empty, StringComparison.Ordinal) &&
            NetworkMediaReadinessEvaluator.IsPersonalWifiPassphraseValid(PersonalWifiPassphrase);
        NetworkSecrets retained = GetRetainedSecrets(settings);
        return new NetworkSecrets
        {
            WifiPassphrase = passphraseApplies ? PersonalWifiPassphrase!.Trim() : null,
            WiredCertificatePfxPassword = settings.Dot1x is { IsEnabled: true, RequiresCertificate: true } ? retained.WiredCertificatePfxPassword : null,
            WifiCertificatePfxPassword = settings.Wifi is { IsEnabled: true, RequiresCertificate: true } ? retained.WifiCertificatePfxPassword : null
        };
    }

    /// <summary>
    /// Returns every password still bound to <paramref name="settings"/> (same SSID, same certificate content hash), whether or
    /// not its feature is enabled; the secret vault keeps these.
    /// </summary>
    public NetworkSecrets GetRetainedSecrets(NetworkSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return new NetworkSecrets
        {
            WifiPassphrase = string.Equals(wifiIdentity, settings.Wifi.Ssid ?? string.Empty, StringComparison.Ordinal) ? PersonalWifiPassphrase : null,
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
