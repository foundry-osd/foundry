using Foundry.Core.Models.Configuration;

namespace Foundry.Core.Services.Configuration;

public sealed class NetworkSecretState
{
    public string? PersonalWifiPassphrase { get; private set; }

    public void Update(NetworkSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

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
        return NetworkMediaReadinessEvaluator.ApplyRequiredSecrets(settings, PersonalWifiPassphrase);
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
