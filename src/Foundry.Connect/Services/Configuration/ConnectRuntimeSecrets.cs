// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Connect.Services.Configuration;

/// <summary>
/// Holds the session-only network secrets resolved while loading the Foundry.Connect configuration.
/// </summary>
/// <remarks>
/// The Core runtime configuration carries secrets only as encrypted envelopes. Decrypted values and the legacy
/// plaintext Wi-Fi passphrase of older media live here instead, so they never round-trip through the shared
/// configuration contract. This is deliberately a class without a value-printing <c>ToString</c>, so the secrets
/// cannot appear in logs or diagnostics through string formatting.
/// </remarks>
public sealed class ConnectRuntimeSecrets
{
    /// <summary>
    /// Gets the personal Wi-Fi passphrase, decrypted from the media envelope or read from legacy plaintext media.
    /// </summary>
    public string? WifiPassphrase { get; init; }

    /// <summary>
    /// Gets the decrypted PFX password of the enterprise Wi-Fi certificate.
    /// </summary>
    public string? WifiCertificatePfxPassword { get; init; }

    /// <summary>
    /// Gets the decrypted PFX password of the wired 802.1X certificate.
    /// </summary>
    public string? WiredCertificatePfxPassword { get; init; }
}
