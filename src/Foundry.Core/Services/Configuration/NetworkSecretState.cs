// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Models.Configuration;

namespace Foundry.Core.Services.Configuration;

public sealed class NetworkSecretState
{
    private string? wifiIdentity;
    private string? wiredCertificatePath;
    private string? wifiCertificatePath;
    private string? wiredCertificatePassword;
    private string? wifiCertificatePassword;

    public string? PersonalWifiPassphrase { get; private set; }

    public void Update(NetworkSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        UpdateCertificate(settings.Dot1x.IsEnabled && settings.Dot1x.RequiresCertificate ? settings.Dot1x.CertificatePath : null,
            settings.Dot1x.CertificatePfxPassword, ref wiredCertificatePath, ref wiredCertificatePassword);
        UpdateCertificate(settings.Wifi.IsEnabled && settings.Wifi.RequiresCertificate ? settings.Wifi.CertificatePath : null,
            settings.Wifi.CertificatePfxPassword, ref wifiCertificatePath, ref wifiCertificatePassword);
        string identity = settings.Wifi.Ssid ?? string.Empty;
        if (!string.Equals(wifiIdentity, identity, StringComparison.Ordinal))
        {
            PersonalWifiPassphrase = null;
            wifiIdentity = identity;
        }

        if (!RequiresPersonalWifiPassphrase(settings))
        {
            PersonalWifiPassphrase = null;
            return;
        }

        if (!string.IsNullOrWhiteSpace(settings.Wifi.Passphrase))
        {
            PersonalWifiPassphrase = settings.Wifi.Passphrase.Trim();
        }
    }

    public void ClearPersonalWifiPassphrase()
    {
        PersonalWifiPassphrase = null;
    }

    public NetworkSettings ApplyRequiredSecrets(NetworkSettings settings)
    {
        NetworkSettings result = NetworkMediaReadinessEvaluator.ApplyRequiredSecrets(settings,
            string.Equals(wifiIdentity, settings.Wifi.Ssid ?? string.Empty, StringComparison.Ordinal) ? PersonalWifiPassphrase : null);
        return result with
        {
            Dot1x = result.Dot1x with
            {
                CertificatePfxPassword = string.Equals(wiredCertificatePath, result.Dot1x.CertificatePath, StringComparison.OrdinalIgnoreCase)
                    ? wiredCertificatePassword : null
            },
            Wifi = result.Wifi with
            {
                CertificatePfxPassword = string.Equals(wifiCertificatePath, result.Wifi.CertificatePath, StringComparison.OrdinalIgnoreCase)
                    ? wifiCertificatePassword : null
            }
        };
    }

    private static void UpdateCertificate(string? path, string? password, ref string? previousPath, ref string? previousPassword)
    {
        if (!string.Equals(path, previousPath, StringComparison.OrdinalIgnoreCase))
        {
            previousPassword = null;
            previousPath = path;
        }

        if (string.IsNullOrWhiteSpace(path))
        {
            previousPassword = null;
        }
        else if (password is not null)
        {
            previousPassword = password;
        }
    }

    private static bool RequiresPersonalWifiPassphrase(NetworkSettings settings)
    {
        return settings.WifiProvisioned &&
               settings.Wifi.IsEnabled &&
               !settings.Wifi.HasEnterpriseProfile &&
               string.Equals(
                   NetworkConfigurationValidator.NormalizeWifiSecurityType(settings.Wifi),
                   NetworkConfigurationValidator.WifiSecurityPersonal,
                   StringComparison.OrdinalIgnoreCase);
    }
}
