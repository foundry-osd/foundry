// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Core.Services.Configuration;

public enum NetworkConfigurationValidationCode
{
    None,
    WifiProvisioningRequired,
    WiredProfileTemplateRequired,
    WiredProfileTemplateMissing,
    WiredCertificateRequired,
    WiredCertificateMissing,
    WifiSsidRequired,
    UnsupportedWifiSecurityType,
    WifiPersonalPassphraseInvalid,
    WifiEnterpriseProfileTemplateRequired,
    WifiEnterpriseProfileTemplateMissing,
    WifiEnterpriseAuthenticationUnsupported,
    WifiEnterpriseAuthenticationMismatch,
    WifiEnterpriseCertificateRequired,
    WifiEnterpriseCertificateMissing
}
