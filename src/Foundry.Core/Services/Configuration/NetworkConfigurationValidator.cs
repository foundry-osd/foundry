using System.Xml.Linq;
using Foundry.Core.Models.Configuration;

namespace Foundry.Core.Services.Configuration;

/// <summary>
/// Validates and normalizes Expert Deploy network settings before they are persisted or provisioned.
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

    private static readonly string[] LegacyWifiSecurityPersonalValues = ["WPA2-Personal", "WPA3-Personal", "Personal"];
    private static readonly string[] LegacyWifiSecurityEnterpriseValues = ["WPA2-Enterprise", "WPA3-Enterprise", "WPA3", "Enterprise"];

    /// <summary>
    /// Validates the full network configuration and returns the first blocking issue.
    /// </summary>
    /// <param name="settings">The network settings to validate.</param>
    /// <returns>A validation result.</returns>
    public static NetworkConfigurationValidationResult Validate(NetworkSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        if (settings.Wifi.IsEnabled && !settings.WifiProvisioned)
        {
            return NetworkConfigurationValidationResult.Failure(NetworkConfigurationValidationCode.WifiProvisioningRequired);
        }

        NetworkConfigurationValidationResult dot1xResult = ValidateDot1x(settings.Dot1x);
        if (!dot1xResult.IsValid)
        {
            return dot1xResult;
        }

        return ValidateWifi(settings.WifiProvisioned, settings.Wifi);
    }

    /// <summary>
    /// Removes volatile network values that should not be persisted in application settings.
    /// </summary>
    /// <param name="settings">The network settings to sanitize.</param>
    /// <returns>A sanitized copy of the network settings.</returns>
    public static NetworkSettings SanitizeForPersistence(NetworkSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        return settings with
        {
            Wifi = settings.Wifi with
            {
                Passphrase = null
            }
        };
    }

    /// <summary>
    /// Normalizes a Wi-Fi security type while preserving compatibility with older configuration values.
    /// </summary>
    /// <param name="settings">The Wi-Fi settings to inspect.</param>
    /// <returns>The normalized Wi-Fi security type.</returns>
    public static string NormalizeWifiSecurityType(WifiSettings settings)
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

        return !string.IsNullOrWhiteSpace(settings.Passphrase)
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

    private static NetworkConfigurationValidationResult ValidateDot1x(Dot1xSettings settings)
    {
        if (!settings.IsEnabled)
        {
            return NetworkConfigurationValidationResult.Success;
        }

        if (string.IsNullOrWhiteSpace(settings.ProfileTemplatePath))
        {
            return NetworkConfigurationValidationResult.Failure(NetworkConfigurationValidationCode.WiredProfileTemplateRequired);
        }

        if (!File.Exists(Path.GetFullPath(settings.ProfileTemplatePath)))
        {
            return NetworkConfigurationValidationResult.Failure(NetworkConfigurationValidationCode.WiredProfileTemplateMissing);
        }

        if (settings.RequiresCertificate && string.IsNullOrWhiteSpace(settings.CertificatePath))
        {
            return NetworkConfigurationValidationResult.Failure(NetworkConfigurationValidationCode.WiredCertificateRequired);
        }

        if (!string.IsNullOrWhiteSpace(settings.CertificatePath) &&
            !File.Exists(Path.GetFullPath(settings.CertificatePath)))
        {
            return NetworkConfigurationValidationResult.Failure(NetworkConfigurationValidationCode.WiredCertificateMissing);
        }

        return NetworkConfigurationValidationResult.Success;
    }

    private static NetworkConfigurationValidationResult ValidateWifi(bool wifiProvisioned, WifiSettings settings)
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
            int passphraseLength = settings.Passphrase?.Trim().Length ?? 0;
            if (passphraseLength is < 8 or > 63)
            {
                return NetworkConfigurationValidationResult.Failure(NetworkConfigurationValidationCode.WifiPersonalPassphraseInvalid);
            }
        }

        return isEnterprise
            ? ValidateEnterpriseWifi(settings, enterpriseSecurityType)
            : NetworkConfigurationValidationResult.Success;
    }

    private static NetworkConfigurationValidationResult ValidateEnterpriseWifi(
        WifiSettings settings,
        string? enterpriseSecurityType)
    {
        if (string.IsNullOrWhiteSpace(settings.EnterpriseProfileTemplatePath))
        {
            return NetworkConfigurationValidationResult.Failure(NetworkConfigurationValidationCode.WifiEnterpriseProfileTemplateRequired);
        }

        string fullTemplatePath = Path.GetFullPath(settings.EnterpriseProfileTemplatePath);
        if (!File.Exists(fullTemplatePath))
        {
            return NetworkConfigurationValidationResult.Failure(NetworkConfigurationValidationCode.WifiEnterpriseProfileTemplateMissing);
        }

        if (RequiresExplicitEnterpriseTemplateAuthentication(enterpriseSecurityType))
        {
            string? templateSecurityType = TryReadEnterpriseTemplateSecurityType(fullTemplatePath);
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

        if (settings.RequiresCertificate && string.IsNullOrWhiteSpace(settings.CertificatePath))
        {
            return NetworkConfigurationValidationResult.Failure(NetworkConfigurationValidationCode.WifiEnterpriseCertificateRequired);
        }

        if (!string.IsNullOrWhiteSpace(settings.CertificatePath) &&
            !File.Exists(Path.GetFullPath(settings.CertificatePath)))
        {
            return NetworkConfigurationValidationResult.Failure(NetworkConfigurationValidationCode.WifiEnterpriseCertificateMissing);
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

    private static string? TryReadEnterpriseTemplateSecurityType(string profileTemplatePath)
    {
        try
        {
            // WPA3 enterprise profiles must match the explicit authentication mode selected in the UI.
            XDocument document = XDocument.Load(Path.GetFullPath(profileTemplatePath));
            XNamespace wlanProfile = "http://www.microsoft.com/networking/WLAN/profile/v1";
            string? authentication = document
                .Descendants(wlanProfile + "authentication")
                .Select(static element => element.Value?.Trim())
                .FirstOrDefault(static value => !string.IsNullOrWhiteSpace(value));

            return NormalizeEnterpriseSecurityType(authentication);
        }
        catch
        {
            return null;
        }
    }
}
