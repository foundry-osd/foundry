// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Models.Configuration;
using Foundry.Utilities.Networking;

namespace Foundry.Core.Services.Configuration;

/// <summary>
/// Validates and normalizes Foundry network settings before they are persisted or provisioned.
/// </summary>
public static class NetworkConfigurationValidator
{
    /// <summary>
    /// Represents an open Wi-Fi network.
    /// </summary>
    public const string WifiSecurityOpen = "Open";

    /// <summary>
    /// Represents opportunistic wireless encryption.
    /// </summary>
    public const string WifiSecurityOwe = "OWE";

    /// <summary>
    /// Represents WPA2/WPA3 personal Wi-Fi security.
    /// </summary>
    public const string WifiSecurityPersonal = "WPA2/WPA3-Personal";

    /// <summary>
    /// Represents WPA2 personal Wi-Fi security.
    /// </summary>
    public const string WifiSecurityLegacyWpa2Personal = "WPA2-Personal";

    /// <summary>
    /// Represents WPA3 personal Wi-Fi security.
    /// </summary>
    public const string WifiSecurityWpa3Personal = "WPA3-Personal";

    /// <summary>
    /// Represents WPA2/WPA3 enterprise Wi-Fi security.
    /// </summary>
    public const string WifiSecurityEnterprise = "WPA2/WPA3-Enterprise";

    /// <summary>
    /// Represents WPA3 enterprise Wi-Fi security.
    /// </summary>
    public const string WifiSecurityEnterpriseWpa3 = "WPA3ENT";

    /// <summary>
    /// Represents WPA3 enterprise 192-bit Wi-Fi security.
    /// </summary>
    public const string WifiSecurityEnterpriseWpa3192 = "WPA3ENT192";

    private static readonly string[] LegacyWifiSecurityPersonalValues = [WifiSecurityLegacyWpa2Personal, WifiSecurityWpa3Personal, "Personal"];
    private static readonly string[] LegacyWifiSecurityEnterpriseValues = ["WPA2-Enterprise", "WPA3-Enterprise", "WPA3", "Enterprise"];

    /// <summary>
    /// Validates the full network configuration and returns the first blocking issue.
    /// </summary>
    /// <param name="settings">The network settings to validate.</param>
    /// <param name="inputs">Where referenced profiles and certificates can be read, and the personal Wi-Fi passphrase.</param>
    /// <param name="isMediaProtected">
    /// Whether Password protection is on and usable: its session password is valid in Foundry OSD, or the build material is protected in the generator.
    /// A network secret (personal Wi-Fi passphrase, client certificate) is refused without it.
    /// </param>
    /// <returns>A validation result.</returns>
    public static NetworkConfigurationValidationResult Validate(NetworkSettings settings, MediaInputs inputs, bool isMediaProtected)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(inputs);

        if (settings.Wifi.IsEnabled && !settings.WifiProvisioned)
        {
            return NetworkConfigurationValidationResult.Failure(NetworkConfigurationValidationCode.WifiProvisioningRequired);
        }

        NetworkConfigurationValidationResult dot1xResult = ValidateDot1x(settings.Dot1x, inputs, isMediaProtected);
        if (!dot1xResult.IsValid)
        {
            return dot1xResult;
        }

        return ValidateWifi(settings.WifiProvisioned, settings.Wifi, inputs, isMediaProtected);
    }

    /// <summary>
    /// Gets whether an enabled feature provisions a network secret, which requires Password protection on the media.
    /// </summary>
    /// <remarks>
    /// Counts a personal Wi-Fi passphrase present in <see cref="MediaInputs.Network"/> (the vault-backed state) and a client certificate
    /// on enabled wired 802.1X or on enabled, provisioned enterprise Wi-Fi; a PFX counts even without a password because it carries the private key.
    /// Trusted root certificates are public and never count.
    /// </remarks>
    /// <param name="settings">The network settings to inspect.</param>
    /// <param name="inputs">The current network secrets.</param>
    /// <returns><see langword="true"/> when the media must be protected.</returns>
    public static bool RequiresMediaProtection(NetworkSettings settings, MediaInputs inputs)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(inputs);

        if (settings.Dot1x.IsEnabled && settings.Dot1x.ClientCertificate is not null)
        {
            return true;
        }

        if (!settings.WifiProvisioned || !settings.Wifi.IsEnabled)
        {
            return false;
        }

        // Same classification as ValidateWifi, so the rule and the validation agree: the personal and enterprise checks are
        // independent, so a personal type with a stale enterprise flag still requires protection for its passphrase.
        bool isEnterprise = settings.Wifi.HasEnterpriseProfile || NormalizeEnterpriseSecurityType(settings.Wifi.SecurityType) is not null;
        bool isPersonal = IsPersonalSecurityType(settings.Wifi.SecurityType);
        return (isEnterprise && settings.Wifi.ClientCertificate is not null) ||
               (isPersonal && !string.IsNullOrWhiteSpace(inputs.Network.WifiPassphrase));
    }

    /// <summary>
    /// Normalizes a Wi-Fi security type while preserving compatibility with older configuration values.
    /// </summary>
    /// <param name="settings">The Wi-Fi settings to inspect.</param>
    /// <param name="hasPassphrase">Whether a personal passphrase is known; it selects personal security when no type is recorded.</param>
    /// <returns>The normalized Wi-Fi security type.</returns>
    public static string NormalizeWifiSecurityType(WifiSettings settings, bool hasPassphrase = false)
    {
        ArgumentNullException.ThrowIfNull(settings);

        if (string.Equals(settings.SecurityType, WifiSecurityOpen, StringComparison.OrdinalIgnoreCase))
        {
            return WifiSecurityOpen;
        }

        if (string.Equals(settings.SecurityType, WifiSecurityOwe, StringComparison.OrdinalIgnoreCase))
        {
            return WifiSecurityOwe;
        }

        if (string.Equals(settings.SecurityType, WifiSecurityPersonal, StringComparison.OrdinalIgnoreCase) ||
            LegacyWifiSecurityPersonalValues.Any(value => string.Equals(settings.SecurityType, value, StringComparison.OrdinalIgnoreCase)))
        {
            return WifiSecurityPersonal;
        }

        string? enterpriseSecurityType = NormalizeEnterpriseSecurityType(settings.SecurityType);
        if (enterpriseSecurityType is not null || settings.HasEnterpriseProfile)
        {
            return enterpriseSecurityType ?? WifiSecurityEnterprise;
        }

        return hasPassphrase
            ? WifiSecurityPersonal
            : WifiSecurityOpen;
    }

    /// <summary>
    /// Gets whether a security type represents an enterprise Wi-Fi profile.
    /// </summary>
    /// <param name="securityType">The security type to inspect.</param>
    /// <returns><see langword="true"/> when the security type is enterprise.</returns>
    public static bool IsEnterpriseSecurityType(string? securityType)
    {
        return NormalizeEnterpriseSecurityType(securityType) is not null;
    }

    /// <summary>
    /// Normalizes an enterprise Wi-Fi security type.
    /// </summary>
    /// <param name="securityType">The security type to normalize.</param>
    /// <returns>The normalized enterprise security type, or <see langword="null"/> when unsupported.</returns>
    public static string? NormalizeEnterpriseSecurityType(string? securityType)
    {
        if (string.IsNullOrWhiteSpace(securityType))
        {
            return null;
        }

        if (string.Equals(securityType, WifiSecurityEnterprise, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(securityType, "WPA2-Enterprise", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(securityType, "Enterprise", StringComparison.OrdinalIgnoreCase))
        {
            return WifiSecurityEnterprise;
        }

        if (string.Equals(securityType, WifiSecurityEnterpriseWpa3, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(securityType, "WPA3-Enterprise", StringComparison.OrdinalIgnoreCase))
        {
            return WifiSecurityEnterpriseWpa3;
        }

        if (string.Equals(securityType, WifiSecurityEnterpriseWpa3192, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(securityType, "WPA3", StringComparison.OrdinalIgnoreCase))
        {
            return WifiSecurityEnterpriseWpa3192;
        }

        return LegacyWifiSecurityEnterpriseValues.Any(value => string.Equals(securityType, value, StringComparison.OrdinalIgnoreCase))
            ? WifiSecurityEnterprise
            : null;
    }

    private static NetworkConfigurationValidationResult ValidateDot1x(Dot1xSettings settings, MediaInputs inputs, bool isMediaProtected)
    {
        if (!settings.IsEnabled)
        {
            return NetworkConfigurationValidationResult.Success;
        }

        if (settings.ProfileTemplate is null)
        {
            return NetworkConfigurationValidationResult.Failure(NetworkConfigurationValidationCode.WiredProfileTemplateRequired);
        }

        if (!IsAvailable(settings.ProfileTemplate, inputs))
        {
            return NetworkConfigurationValidationResult.Failure(NetworkConfigurationValidationCode.WiredProfileTemplateMissing);
        }

        if (settings.RequiresCertificate && !NetworkCertificateShapes.HasCertificates(settings))
        {
            return NetworkConfigurationValidationResult.Failure(NetworkConfigurationValidationCode.WiredCertificateRequired);
        }

        if (NetworkCertificateShapes.All(settings).Any(certificate => !IsAvailable(certificate, inputs)))
        {
            return NetworkConfigurationValidationResult.Failure(NetworkConfigurationValidationCode.WiredCertificateMissing);
        }

        if (settings.ClientCertificate is not null && !isMediaProtected)
        {
            return NetworkConfigurationValidationResult.Failure(NetworkConfigurationValidationCode.WiredMediaProtectionRequired);
        }

        return NetworkConfigurationValidationResult.Success;
    }

    private static bool IsAvailable(AuthoringFileReference reference, MediaInputs inputs) =>
        inputs.ResolveFile(reference) is { } path && File.Exists(path);

    private static NetworkConfigurationValidationResult ValidateWifi(bool wifiProvisioned, WifiSettings settings, MediaInputs inputs, bool isMediaProtected)
    {
        if (!settings.IsEnabled)
        {
            return NetworkConfigurationValidationResult.Success;
        }

        if (!wifiProvisioned)
        {
            return NetworkConfigurationValidationResult.Failure(NetworkConfigurationValidationCode.WifiProvisioningRequired);
        }

        if (string.IsNullOrWhiteSpace(settings.Ssid))
        {
            return NetworkConfigurationValidationResult.Failure(NetworkConfigurationValidationCode.WifiSsidRequired);
        }

        bool isOpen = string.Equals(settings.SecurityType, WifiSecurityOpen, StringComparison.OrdinalIgnoreCase);
        bool isOwe = string.Equals(settings.SecurityType, WifiSecurityOwe, StringComparison.OrdinalIgnoreCase);
        bool isPersonal = IsPersonalSecurityType(settings.SecurityType);
        string? enterpriseSecurityType = NormalizeEnterpriseSecurityType(settings.SecurityType);
        bool isEnterprise = settings.HasEnterpriseProfile || enterpriseSecurityType is not null;

        if (!isOpen && !isOwe && !isPersonal && !isEnterprise)
        {
            return NetworkConfigurationValidationResult.Failure(
                NetworkConfigurationValidationCode.UnsupportedWifiSecurityType,
                settings.SecurityType ?? string.Empty);
        }

        if (isPersonal)
        {
            int passphraseLength = inputs.Network.WifiPassphrase?.Trim().Length ?? 0;
            if (passphraseLength is < 8 or > 63)
            {
                return NetworkConfigurationValidationResult.Failure(NetworkConfigurationValidationCode.WifiPersonalPassphraseInvalid);
            }

            if (!isMediaProtected)
            {
                return NetworkConfigurationValidationResult.Failure(NetworkConfigurationValidationCode.WifiMediaProtectionRequired);
            }
        }

        return isEnterprise
            ? ValidateEnterpriseWifi(settings, enterpriseSecurityType, inputs, isMediaProtected)
            : NetworkConfigurationValidationResult.Success;
    }

    private static NetworkConfigurationValidationResult ValidateEnterpriseWifi(
        WifiSettings settings,
        string? enterpriseSecurityType,
        MediaInputs inputs,
        bool isMediaProtected)
    {
        if (settings.EnterpriseProfileTemplate is null)
        {
            return NetworkConfigurationValidationResult.Failure(NetworkConfigurationValidationCode.WifiEnterpriseProfileTemplateRequired);
        }

        string? fullTemplatePath = inputs.ResolveFile(settings.EnterpriseProfileTemplate);
        if (fullTemplatePath is null || !File.Exists(fullTemplatePath))
        {
            return NetworkConfigurationValidationResult.Failure(NetworkConfigurationValidationCode.WifiEnterpriseProfileTemplateMissing);
        }

        if (RequiresExplicitEnterpriseTemplateAuthentication(enterpriseSecurityType))
        {
            // WPA3 enterprise profiles must match the explicit authentication mode selected in the UI.
            string? templateSecurityType = NormalizeEnterpriseSecurityType(
                WlanProfileReader.TryReadAuthentication(fullTemplatePath));
            if (templateSecurityType is null)
            {
                return NetworkConfigurationValidationResult.Failure(NetworkConfigurationValidationCode.WifiEnterpriseAuthenticationUnsupported);
            }

            if (!string.Equals(templateSecurityType, enterpriseSecurityType, StringComparison.OrdinalIgnoreCase))
            {
                return NetworkConfigurationValidationResult.Failure(
                    NetworkConfigurationValidationCode.WifiEnterpriseAuthenticationMismatch,
                    templateSecurityType);
            }
        }

        if (settings.RequiresCertificate && !NetworkCertificateShapes.HasCertificates(settings))
        {
            return NetworkConfigurationValidationResult.Failure(NetworkConfigurationValidationCode.WifiEnterpriseCertificateRequired);
        }

        if (NetworkCertificateShapes.All(settings).Any(certificate => !IsAvailable(certificate, inputs)))
        {
            return NetworkConfigurationValidationResult.Failure(NetworkConfigurationValidationCode.WifiEnterpriseCertificateMissing);
        }

        if (settings.ClientCertificate is not null && !isMediaProtected)
        {
            return NetworkConfigurationValidationResult.Failure(NetworkConfigurationValidationCode.WifiMediaProtectionRequired);
        }

        return NetworkConfigurationValidationResult.Success;
    }

    private static bool IsPersonalSecurityType(string? securityType)
    {
        return string.Equals(securityType, WifiSecurityPersonal, StringComparison.OrdinalIgnoreCase) ||
               LegacyWifiSecurityPersonalValues.Any(value => string.Equals(securityType, value, StringComparison.OrdinalIgnoreCase));
    }

    private static bool RequiresExplicitEnterpriseTemplateAuthentication(string? securityType)
    {
        return string.Equals(securityType, WifiSecurityEnterpriseWpa3, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(securityType, WifiSecurityEnterpriseWpa3192, StringComparison.OrdinalIgnoreCase);
    }

}
