// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Services.Configuration;
using Foundry.Services.Localization;

namespace Foundry.ViewModels;

internal static class NetworkConfigurationValidationTextFormatter
{
    public static string Format(
        IApplicationLocalizationService localizationService,
        NetworkConfigurationValidationResult result)
    {
        if (result.IsValid)
        {
            return string.Empty;
        }

        string key = result.Code switch
        {
            NetworkConfigurationValidationCode.WifiProvisioningRequired => "Network.ErrorWifiProvisioningRequired",
            NetworkConfigurationValidationCode.WiredProfileTemplateRequired => "Network.ErrorWiredProfileTemplateRequired",
            NetworkConfigurationValidationCode.WiredProfileTemplateMissing => "Network.ErrorWiredProfileTemplateMissing",
            NetworkConfigurationValidationCode.WiredCertificateRequired => "Network.ErrorWiredCertificateRequired",
            NetworkConfigurationValidationCode.WiredCertificateMissing => "Network.ErrorWiredCertificateMissing",
            NetworkConfigurationValidationCode.WifiSsidRequired => "Network.ErrorWifiSsidRequired",
            NetworkConfigurationValidationCode.UnsupportedWifiSecurityType => "Network.ErrorUnsupportedWifiSecurityTypeFormat",
            NetworkConfigurationValidationCode.WifiPersonalPassphraseInvalid => "Network.ErrorWifiPersonalPassphraseInvalid",
            NetworkConfigurationValidationCode.WifiEnterpriseProfileTemplateRequired => "Network.ErrorWifiEnterpriseProfileTemplateRequired",
            NetworkConfigurationValidationCode.WifiEnterpriseProfileTemplateMissing => "Network.ErrorWifiEnterpriseProfileTemplateMissing",
            NetworkConfigurationValidationCode.WifiEnterpriseAuthenticationUnsupported => "Network.ErrorWifiEnterpriseAuthenticationUnsupported",
            NetworkConfigurationValidationCode.WifiEnterpriseAuthenticationMismatch => "Network.ErrorWifiEnterpriseAuthenticationMismatchFormat",
            NetworkConfigurationValidationCode.WifiEnterpriseCertificateRequired => "Network.ErrorWifiEnterpriseCertificateRequired",
            NetworkConfigurationValidationCode.WifiEnterpriseCertificateMissing => "Network.ErrorWifiEnterpriseCertificateMissing",
            _ => string.Empty
        };

        if (string.IsNullOrWhiteSpace(key))
        {
            return string.Empty;
        }

        object[] arguments = result.Code == NetworkConfigurationValidationCode.WifiEnterpriseAuthenticationMismatch
            ? result.FormatArguments.Select(value => FormatEnterpriseSecurityTypeLabel(localizationService, value)).Cast<object>().ToArray()
            : result.FormatArguments.Cast<object>().ToArray();

        return arguments.Length == 0
            ? localizationService.GetString(key)
            : localizationService.FormatString(key, arguments);
    }

    private static string FormatEnterpriseSecurityTypeLabel(
        IApplicationLocalizationService localizationService,
        string securityType)
    {
        return securityType switch
        {
            NetworkConfigurationValidator.WifiSecurityEnterpriseWpa3 => localizationService.GetString("Wifi.SecurityTypeEnterpriseWpa3"),
            NetworkConfigurationValidator.WifiSecurityEnterpriseWpa3192 => localizationService.GetString("Wifi.SecurityTypeEnterpriseWpa3192"),
            _ => localizationService.GetString("Wifi.SecurityTypeEnterprise")
        };
    }
}
