// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Models.Configuration;

namespace Foundry.Core.Services.Configuration;

public static class NetworkMediaReadinessEvaluator
{
    private const string ValidationPassphrasePlaceholder = "FoundryTransientSecret";

    /// <summary>
    /// Separates network configuration readiness from the readiness of the personal Wi-Fi passphrase kept outside the document.
    /// </summary>
    /// <param name="settings">The authoring network settings.</param>
    /// <param name="inputs">Where referenced files can be read, and the current network passwords.</param>
    /// <param name="isMediaProtected">Whether Password protection is on and usable; a network secret is not ready without it.</param>
    public static NetworkMediaReadinessEvaluation Evaluate(NetworkSettings settings, MediaInputs inputs, bool isMediaProtected)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(inputs);

        bool requiresPersonalWifiPassphrase = RequiresPersonalWifiPassphrase(settings);
        MediaInputs validationInputs = requiresPersonalWifiPassphrase
            ? inputs with { Network = inputs.Network with { WifiPassphrase = ValidationPassphrasePlaceholder } }
            : inputs;

        NetworkConfigurationValidationResult validationResult = NetworkConfigurationValidator.Validate(settings, validationInputs, isMediaProtected);
        bool isNetworkConfigurationReady = validationResult.IsValid;
        bool areRequiredSecretsReady = !requiresPersonalWifiPassphrase ||
            IsPersonalWifiPassphraseValid(inputs.Network.WifiPassphrase);

        return new NetworkMediaReadinessEvaluation
        {
            IsNetworkConfigurationReady = isNetworkConfigurationReady,
            NetworkConfigurationValidationCode = validationResult.Code,
            IsConnectProvisioningReady = isNetworkConfigurationReady && areRequiredSecretsReady,
            AreRequiredSecretsReady = areRequiredSecretsReady
        };
    }

    /// <summary>Gets whether the settings provision personal Wi-Fi, whose passphrase the media needs.</summary>
    /// <remarks>
    /// Same rule as <see cref="NetworkConfigurationValidator"/>: a personal security type needs the passphrase even when an
    /// enterprise profile is still recorded from an earlier security type.
    /// </remarks>
    internal static bool RequiresPersonalWifiPassphrase(NetworkSettings settings)
    {
        if (!settings.WifiProvisioned || !settings.Wifi.IsEnabled)
        {
            return false;
        }

        return string.Equals(
            NetworkConfigurationValidator.NormalizeWifiSecurityType(settings.Wifi),
            NetworkConfigurationValidator.WifiSecurityPersonal,
            StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Gets whether a passphrase has the WPA2/WPA3 personal length once trimmed.</summary>
    internal static bool IsPersonalWifiPassphraseValid(string? passphrase)
    {
        int length = passphrase?.Trim().Length ?? 0;
        return length is >= 8 and <= 63;
    }
}
