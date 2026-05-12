namespace Foundry.Connect.Models.Configuration;

/// <summary>
/// Describes wired 802.1X profile provisioning for Foundry.Connect.
/// </summary>
public sealed record Dot1xSettings
{
    /// <summary>
    /// Gets a value indicating whether wired 802.1X provisioning is enabled.
    /// </summary>
    public bool IsEnabled { get; init; }

    /// <summary>
    /// Gets the path to the wired LAN profile XML template.
    /// </summary>
    public string? ProfileTemplatePath { get; init; }

    /// <summary>
    /// Gets the authentication mode expected by the wired profile.
    /// </summary>
    public NetworkAuthenticationMode AuthenticationMode { get; init; } = NetworkAuthenticationMode.MachineOnly;

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
    /// Gets the certificate asset path used by 802.1X authentication.
    /// </summary>
    public string? CertificatePath { get; init; }
}
