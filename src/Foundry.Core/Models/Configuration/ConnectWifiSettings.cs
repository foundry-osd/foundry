// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Text.Json.Serialization;

namespace Foundry.Core.Models.Configuration;

/// <summary>
/// Describes the Wi-Fi settings written to the Foundry.Connect runtime configuration.
/// </summary>
/// <remarks>
/// This is part of the Connect runtime schema contract and is kept separate from the authoring <see cref="WifiSettings"/>,
/// so authoring changes cannot alter the generated JSON. Paths are media-relative and secrets are only carried as envelopes.
/// </remarks>
public sealed record ConnectWifiSettings
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
    /// Gets the normalized Wi-Fi security type, or <see langword="null"/> when Wi-Fi is disabled.
    /// </summary>
    public string? SecurityType { get; init; }

    /// <summary>
    /// Gets the encrypted personal Wi-Fi passphrase envelope stored on provisioned media.
    /// </summary>
    public SecretEnvelope? PassphraseSecret { get; init; }

    /// <summary>
    /// Gets whether an enterprise Wi-Fi XML profile is provided.
    /// </summary>
    public bool HasEnterpriseProfile { get; init; }

    /// <summary>
    /// Gets the media-relative path of the staged enterprise Wi-Fi XML profile.
    /// </summary>
    public string? EnterpriseProfileTemplatePath { get; init; }

    /// <summary>
    /// Gets the runtime enterprise authentication mode; generation always emitted the fixed user-only mode.
    /// </summary>
    /// <remarks>Schema 5 and earlier; schema 6 no longer writes it, and Foundry.Connect reads this default, the value generation always wrote.</remarks>
    public NetworkAuthenticationMode EnterpriseAuthenticationMode { get; init; } = NetworkAuthenticationMode.UserOnly;

    /// <summary>
    /// Gets whether runtime credential collection is allowed; generation always disabled it.
    /// </summary>
    /// <remarks>Schema 5 and earlier; schema 6 no longer writes it, and Foundry.Connect reads this default, the value generation always wrote.</remarks>
    public bool AllowRuntimeCredentials { get; init; }

    /// <summary>
    /// Gets whether a certificate file is staged with the Wi-Fi profile.
    /// </summary>
    /// <remarks>Schema 5 and earlier; schema 6 no longer writes it, and Foundry.Connect does not consume it.</remarks>
    public bool RequiresCertificate { get; init; }

    /// <summary>
    /// Gets the media-relative path of the staged certificate.
    /// </summary>
    /// <remarks>Schema 5; read for older media, not written from schema 6. <see cref="ConnectCertificateSet"/> maps it by shape.</remarks>
    public string? CertificatePath { get; init; }

    /// <summary>
    /// Gets the encrypted PFX password envelope stored on provisioned media.
    /// </summary>
    /// <remarks>Schema 5; read for older media, not written from schema 6.</remarks>
    public SecretEnvelope? CertificatePfxPasswordSecret { get; init; }

    /// <summary>
    /// Gets the client certificate staged with the enterprise profile (schema 6), or <see langword="null"/> when none is staged.
    /// </summary>
    public ConnectClientCertificateSettings? ClientCertificate { get; init; }

    /// <summary>
    /// Gets the media-relative paths of the trusted root certificates (<c>.cer</c>/<c>.crt</c>) staged with the enterprise
    /// profile (schema 6), in installation order, or <see langword="null"/> when none is staged.
    /// </summary>
    public IReadOnlyList<string>? TrustedRootCertificates { get; init; }

    /// <summary>
    /// Gets whether the staged enterprise profile template is a <see cref="SecretEnvelope"/> JSON document
    /// (<c>.xml.encrypted</c>) encrypted with the deployment key because it contains key material; omitted when false.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool IsEnterpriseProfileTemplateProtected { get; init; }
}
