// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Core.Models.Configuration;

/// <summary>Client certificate of one Connect 802.1X profile (schema 6).</summary>
/// <remarks>
/// Part of the Connect runtime schema contract. A client PFX carries a private key, so new media stage it encrypted with the
/// deployment key and its password as a deployment-keyed envelope; no plaintext secret is carried.
/// </remarks>
public sealed record ConnectClientCertificateSettings
{
    /// <summary>
    /// Gets the media-relative path of the staged client certificate; <c>&lt;name&gt;.pfx.encrypted</c> when
    /// <see cref="IsContentProtected"/> is <see langword="true"/>.
    /// </summary>
    public string? Path { get; init; }

    /// <summary>
    /// Gets whether the staged file is a <see cref="SecretEnvelope"/> JSON document encrypted with the deployment key
    /// instead of the PFX bytes.
    /// </summary>
    public bool IsContentProtected { get; init; }

    /// <summary>
    /// Gets the encrypted PFX password envelope; schema 6 writes it with key id <c>deployment</c>.
    /// </summary>
    public SecretEnvelope? PasswordSecret { get; init; }
}
