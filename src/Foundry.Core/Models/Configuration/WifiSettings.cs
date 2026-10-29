// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Text.Json.Serialization;

namespace Foundry.Core.Models.Configuration;

/// <summary>
/// Describes a Wi-Fi profile and related certificate inputs for Foundry.Connect.
/// </summary>
/// <remarks>
/// The personal passphrase and the PFX password never enter this record; the network secret state keeps them,
/// bound to the SSID and to the certificate content hash.
/// </remarks>
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
    /// Gets whether an enterprise Wi-Fi XML profile is provided.
    /// </summary>
    public bool HasEnterpriseProfile { get; init; }

    /// <summary>
    /// Gets the enterprise Wi-Fi XML profile template held in the authoring file library.
    /// </summary>
    public AuthoringFileReference? EnterpriseProfileTemplate { get; init; }

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
    /// Gets whether certificates are used: a client certificate or at least one trusted root must be staged with the Wi-Fi profile.
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
