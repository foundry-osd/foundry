// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Core.Models.Configuration;

/// <summary>
/// Describes the wired 802.1X settings written to the Foundry.Connect runtime configuration.
/// </summary>
/// <remarks>
/// This is part of the Connect runtime schema contract and is kept separate from the authoring <see cref="Dot1xSettings"/>,
/// so authoring changes cannot alter the generated JSON. Paths are media-relative and no plaintext secret is carried.
/// </remarks>
public sealed record ConnectDot1xSettings
{
    /// <summary>
    /// Gets whether wired 802.1X provisioning is enabled.
    /// </summary>
    public bool IsEnabled { get; init; }

    /// <summary>
    /// Gets the media-relative path of the staged XML profile.
    /// </summary>
    public string? ProfileTemplatePath { get; init; }

    /// <summary>
    /// Gets the runtime authentication mode; generation emits the fixed machine-only mode.
    /// </summary>
    public NetworkAuthenticationMode AuthenticationMode { get; init; } = NetworkAuthenticationMode.MachineOnly;

    /// <summary>
    /// Gets whether runtime credential collection is allowed; generation always disables it.
    /// </summary>
    public bool AllowRuntimeCredentials { get; init; }

    /// <summary>
    /// Gets whether a certificate file is staged with the profile.
    /// </summary>
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
    /// Gets the client certificate staged with the profile (schema 6), or <see langword="null"/> when none is staged.
    /// </summary>
    public ConnectClientCertificateSettings? ClientCertificate { get; init; }

    /// <summary>
    /// Gets the media-relative paths of the trusted root certificates (<c>.cer</c>/<c>.crt</c>) staged with the profile
    /// (schema 6), in installation order, or <see langword="null"/> when none is staged.
    /// </summary>
    public IReadOnlyList<string>? TrustedRootCertificates { get; init; }
}
