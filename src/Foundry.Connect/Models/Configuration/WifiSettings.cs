namespace Foundry.Connect.Models.Configuration;

/// <summary>
/// Describes Wi-Fi provisioning and runtime connection behavior for Foundry.Connect.
/// </summary>
public sealed record WifiSettings
{
    /// <summary>
    /// Gets a value indicating whether Wi-Fi provisioning is enabled.
    /// </summary>
    public bool IsEnabled { get; init; }

    /// <summary>
    /// Gets the provisioned SSID for personal or open Wi-Fi networks.
    /// </summary>
    public string? Ssid { get; init; }

    /// <summary>
    /// Gets the configured security type label for the provisioned network.
    /// </summary>
    public string? SecurityType { get; init; }

    /// <summary>
    /// Gets the plaintext passphrase when provisioning uses an unsealed configuration.
    /// </summary>
    public string? Passphrase { get; init; }

    /// <summary>
    /// Gets the sealed passphrase envelope when provisioning uses protected secrets.
    /// </summary>
    public SecretEnvelope? PassphraseSecret { get; init; }

    /// <summary>
    /// Gets a value indicating whether an enterprise WLAN profile XML is provisioned.
    /// </summary>
    public bool HasEnterpriseProfile { get; init; }

    /// <summary>
    /// Gets the path to the enterprise WLAN profile XML template.
    /// </summary>
    public string? EnterpriseProfileTemplatePath { get; init; }

    /// <summary>
    /// Gets the enterprise authentication mode expected by the provisioned WLAN profile.
    /// </summary>
    public NetworkAuthenticationMode EnterpriseAuthenticationMode { get; init; } = NetworkAuthenticationMode.UserOnly;

    /// <summary>
    /// Gets a value indicating whether runtime credentials were requested for this profile.
    /// Runtime credential collection is not implemented in the current bootstrap flow.
    /// </summary>
    public bool AllowRuntimeCredentials { get; init; }

    /// <summary>
    /// Gets a value indicating whether a certificate asset is required for the profile.
    /// </summary>
    public bool RequiresCertificate { get; init; }

    /// <summary>
    /// Gets the certificate asset path used by enterprise authentication.
    /// </summary>
    public string? CertificatePath { get; init; }
}
