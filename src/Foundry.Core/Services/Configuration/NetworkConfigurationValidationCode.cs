namespace Foundry.Core.Services.Configuration;

public enum NetworkConfigurationValidationCode
{
    None,
    WifiProvisioningRequired,
    WiredProfileTemplateRequired,
    WiredCertificateRequired,
    WifiSsidRequired,
    UnsupportedWifiSecurityType,
    WifiPersonalPassphraseInvalid,
    WifiEnterpriseProfileTemplateRequired,
    WifiEnterpriseProfileTemplateMissing,
    WifiEnterpriseAuthenticationUnsupported,
    WifiEnterpriseAuthenticationMismatch,
    WifiEnterpriseCertificateRequired
}
