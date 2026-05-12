namespace Foundry.Core.Models.Configuration;

/// <summary>
/// Describes a Wi-Fi profile and related secret/certificate inputs for Foundry.Connect.
/// </summary>
public sealed record WifiSettings
{
    /// <summary>
    /// Gets whether Wi-Fi profile provisioning is enabled.
    /// </summary>
    public bool IsEnabled { get; init; }

    /// <summary>
    /// Gets the Wi-Fi network SSID.
    /// </summary>
    public string? Ssid { get; init; }

    /// <summary>
    /// Gets the configured Wi-Fi security type before validation and runtime generation normalize it.
    /// </summary>
    public string? SecurityType { get; init; }

    /// <summary>
    /// Gets the plaintext passphrase before it is converted to a secret envelope.
    /// </summary>
    public string? Passphrase { get; init; }

    /// <summary>
    /// Gets the encrypted passphrase envelope stored on provisioned media.
    /// </summary>
    public SecretEnvelope? PassphraseSecret { get; init; }

    /// <summary>
    /// Gets whether an enterprise Wi-Fi XML profile is provided.
    /// </summary>
    public bool HasEnterpriseProfile { get; init; }

    /// <summary>
    /// Gets the source enterprise Wi-Fi XML profile template path.
    /// </summary>
    public string? EnterpriseProfileTemplatePath { get; init; }

    /// <summary>
    /// Gets the source enterprise authentication mode.
    /// Runtime Foundry.Connect generation currently emits the fixed user-only mode.
    /// </summary>
    public NetworkAuthenticationMode EnterpriseAuthenticationMode { get; init; } = NetworkAuthenticationMode.UserOnly;

    /// <summary>
    /// Gets whether the source configuration requested runtime credentials.
    /// Runtime Foundry.Connect generation currently disables runtime credential collection.
    /// </summary>
    public bool AllowRuntimeCredentials { get; init; }

    /// <summary>
    /// Gets whether a certificate file must be staged with the Wi-Fi profile.
    /// </summary>
    public bool RequiresCertificate { get; init; }

    /// <summary>
    /// Gets the optional source certificate path.
    /// </summary>
    public string? CertificatePath { get; init; }
}
