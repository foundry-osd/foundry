// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Text.Json.Serialization;

namespace Foundry.Core.Models.Configuration;

/// <summary>
/// Describes a wired 802.1X profile and optional certificates staged for Foundry.Connect.
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
    /// Gets whether certificates are used: a client certificate or at least one trusted root must be staged with the profile.
    /// </summary>
    public bool RequiresCertificate { get; init; }

    /// <summary>
    /// Gets the client certificate (.pfx/.p12) held in the authoring file library; its password stays in the vault under the content hash.
    /// </summary>
    public AuthoringFileReference? ClientCertificate { get; init; }

    /// <summary>
    /// Gets the public trusted root CA certificates held in the authoring file library.
    /// </summary>
    public IReadOnlyList<AuthoringFileReference> TrustedRootCertificates { get; init; } = [];

    /// <summary>
    /// Gets the single certificate written by configuration sharing before separate fields existed. Read only: migration moves it
    /// by shape and clears it, so it is never written.
    /// </summary>
    [JsonPropertyName("certificate")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public AuthoringFileReference? LegacyCertificate { get; init; }
}
