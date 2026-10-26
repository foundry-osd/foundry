// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

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
    /// Gets the runtime enterprise authentication mode; generation emits the fixed user-only mode.
    /// </summary>
    public NetworkAuthenticationMode EnterpriseAuthenticationMode { get; init; } = NetworkAuthenticationMode.UserOnly;

    /// <summary>
    /// Gets whether runtime credential collection is allowed; generation always disables it.
    /// </summary>
    public bool AllowRuntimeCredentials { get; init; }

    /// <summary>
    /// Gets whether a certificate file is staged with the Wi-Fi profile.
    /// </summary>
    public bool RequiresCertificate { get; init; }

    /// <summary>
    /// Gets the media-relative path of the staged certificate.
    /// </summary>
    public string? CertificatePath { get; init; }

    /// <summary>
    /// Gets the encrypted PFX password envelope stored on provisioned media.
    /// </summary>
    public SecretEnvelope? CertificatePfxPasswordSecret { get; init; }
}
