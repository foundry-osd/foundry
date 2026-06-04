using System.Text.Json.Serialization;

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
    /// Gets the source authentication mode.
    /// Runtime Foundry.Connect generation currently emits the fixed machine-only mode.
    /// </summary>
    public NetworkAuthenticationMode AuthenticationMode { get; init; } = NetworkAuthenticationMode.MachineOnly;

    /// <summary>
    /// Gets whether the source configuration requested runtime credentials.
    /// Runtime Foundry.Connect generation currently disables runtime credential collection.
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

    /// <summary>
    /// Gets the session-only password for a configured PFX certificate.
    /// </summary>
    [JsonIgnore]
    public string? CertificatePfxPassword { get; init; }

    /// <summary>
    /// Gets the encrypted PFX password envelope stored on provisioned media.
    /// </summary>
    public SecretEnvelope? CertificatePfxPasswordSecret { get; init; }
}
