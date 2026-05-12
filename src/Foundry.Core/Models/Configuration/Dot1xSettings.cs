namespace Foundry.Core.Models.Configuration;

/// <summary>
/// Describes a wired 802.1X profile and optional certificate staged for Foundry.Connect.
/// </summary>
public sealed record Dot1xSettings
{
    /// <summary>
    /// Gets whether wired 802.1X provisioning is enabled.
    /// </summary>
    public bool IsEnabled { get; init; }

    /// <summary>
    /// Gets the source XML profile template path.
    /// </summary>
    public string? ProfileTemplatePath { get; init; }

    /// <summary>
    /// Gets the expected authentication mode for the profile template.
    /// </summary>
    public NetworkAuthenticationMode AuthenticationMode { get; init; } = NetworkAuthenticationMode.MachineOnly;

    /// <summary>
    /// Gets whether runtime credentials are allowed when the profile is applied.
    /// </summary>
    public bool AllowRuntimeCredentials { get; init; }

    /// <summary>
    /// Gets whether a certificate file must be staged with the profile.
    /// </summary>
    public bool RequiresCertificate { get; init; }

    /// <summary>
    /// Gets the optional source certificate path.
    /// </summary>
    public string? CertificatePath { get; init; }
}
