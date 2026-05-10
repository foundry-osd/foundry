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
