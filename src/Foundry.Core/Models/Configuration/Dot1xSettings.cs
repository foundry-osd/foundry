// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Core.Models.Configuration;

/// <summary>
/// Describes a wired 802.1X profile and optional certificate staged for Foundry.Connect.
/// </summary>
/// <remarks>The PFX password never enters this record; the network secret state keeps it, keyed by the certificate content hash.</remarks>
public sealed record Dot1xSettings
{
    /// <summary>
    /// Gets whether wired 802.1X provisioning is enabled.
    /// </summary>
    public bool IsEnabled { get; init; }

    /// <summary>
    /// Gets the XML profile template held in the authoring file library.
    /// </summary>
    public AuthoringFileReference? ProfileTemplate { get; init; }

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
    /// Gets the optional certificate held in the authoring file library.
    /// </summary>
    public AuthoringFileReference? Certificate { get; init; }

    /// <summary>
    /// Gets the encrypted PFX password envelope stored on provisioned media.
    /// </summary>
    public SecretEnvelope? CertificatePfxPasswordSecret { get; init; }
}
