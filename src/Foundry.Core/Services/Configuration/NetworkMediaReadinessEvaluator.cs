using Foundry.Core.Models.Configuration;

namespace Foundry.Core.Services.Configuration;

public static class NetworkMediaReadinessEvaluator
{
    private const string ValidationPassphrasePlaceholder = "FoundryTransientSecret";

    public static NetworkMediaReadinessEvaluation Evaluate(NetworkSettings settings, string? personalWifiPassphrase = null)
    {
        ArgumentNullException.ThrowIfNull(settings);

        bool requiresPersonalWifiPassphrase = RequiresPersonalWifiPassphrase(settings);
        NetworkSettings validationSettings = requiresPersonalWifiPassphrase
            ? settings with
            {
                Wifi = settings.Wifi with
                {
                    Passphrase = ValidationPassphrasePlaceholder
                }
            }
            : settings;

        bool isNetworkConfigurationReady = NetworkConfigurationValidator.Validate(validationSettings).IsValid;
        bool areRequiredSecretsReady = !requiresPersonalWifiPassphrase ||
            IsPersonalWifiPassphraseValid(personalWifiPassphrase);

        return new NetworkMediaReadinessEvaluation
        {
            IsNetworkConfigurationReady = isNetworkConfigurationReady,
            IsConnectProvisioningReady = isNetworkConfigurationReady && areRequiredSecretsReady,
            AreRequiredSecretsReady = areRequiredSecretsReady
        };
    }

    public static NetworkSettings ApplyRequiredSecrets(NetworkSettings settings, string? personalWifiPassphrase)
    {
        ArgumentNullException.ThrowIfNull(settings);

        if (!RequiresPersonalWifiPassphrase(settings) || !IsPersonalWifiPassphraseValid(personalWifiPassphrase))
        {
            return settings;
        }

        return settings with
        {
            Wifi = settings.Wifi with
            {
                Passphrase = personalWifiPassphrase!.Trim()
            }
        };
    }

    private static bool RequiresPersonalWifiPassphrase(NetworkSettings settings)
    {
        if (!settings.WifiProvisioned || !settings.Wifi.IsEnabled || settings.Wifi.HasEnterpriseProfile)
        {
            return false;
        }

        return string.Equals(
            NetworkConfigurationValidator.NormalizeWifiSecurityType(settings.Wifi),
            NetworkConfigurationValidator.WifiSecurityPersonal,
            StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsPersonalWifiPassphraseValid(string? passphrase)
    {
        int length = passphrase?.Trim().Length ?? 0;
        return length is >= 8 and <= 63;
    }
}
