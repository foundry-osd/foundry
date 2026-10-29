// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Services.NetworkCache;
using Foundry.Core.Services.Sharing;

namespace Foundry.Services.NetworkCache;

/// <summary>
/// Resolves the key that boot media trust for the signed network cache index, and gives a reference or standalone PC the key
/// that signs it: the configuration sharing signing key of the vault. Reading trust never writes; the key is created only at
/// explicit points (<see cref="EnsureSigningKey"/>, <see cref="GetOrCreateSigningKey"/>).
/// </summary>
/// <remarks>Members run on the UI thread, which owns the secret vault bridge.</remarks>
public interface INetworkCacheSigningService
{
    /// <summary>Gets whether this PC writes the index: a reference or standalone PC does, a follower never does.</summary>
    bool CanPublishIndex { get; }

    /// <summary>
    /// Gets whether this PC can sign the index now: it publishes the index and its signing key can be read, or, on a standalone
    /// PC only, created in a loaded and readable vault. Never creates or writes a key.
    /// </summary>
    bool CanSignIndex { get; }

    /// <summary>
    /// Raised on the UI thread after <see cref="Initialize"/> when what this service answers may have changed: the sharing role
    /// changed, the signing key was created, or the vault status changed. Readiness and the publication state re-evaluate on it.
    /// </summary>
    event EventHandler? Changed;

    /// <summary>
    /// Gets the DER SubjectPublicKeyInfo media trust: this PC's own key as stored in the vault, or the reference key a follower
    /// pinned. <see langword="null"/> when there is none, so media with an active network cache cannot be built. Never creates
    /// or writes a key.
    /// </summary>
    byte[]? TrustedPublicKey { get; }

    /// <summary>Returns the binding of <see cref="TrustedPublicKey"/> with <paramref name="minimumSequence"/>, or <see langword="null"/> without a trusted key.</summary>
    NetworkCacheIndexBinding? GetBinding(long minimumSequence);

    /// <summary>
    /// Starts creating the signing key at the explicit points where it becomes needed: now, when the network cache becomes active,
    /// when the sharing role turns to a publishing role while the cache is active, and when the vault status changes while the
    /// cache is active. Call once on the UI thread, after the secret vault bridge and configuration sharing are initialized.
    /// </summary>
    void Initialize();

    /// <summary>
    /// Creates and stores the signing key when this standalone PC has none in its vault, once loaded and readable; a reference
    /// PC only reads the key configuration sharing created. A creation failure is logged once and not retried until the vault
    /// status changes. Never throws.
    /// </summary>
    /// <returns>Whether this PC now has a signing key.</returns>
    bool EnsureSigningKey();

    /// <summary>Returns this PC's signing key, creating and storing it when a standalone PC has none; the caller disposes it.</summary>
    /// <exception cref="InvalidOperationException">
    /// This PC cannot publish the index, the vault is not loaded or unreadable, or a reference PC has no key that can be read.
    /// </exception>
    /// <exception cref="IOException">The vault cannot be written.</exception>
    /// <exception cref="UnauthorizedAccessException">Access to the vault is denied.</exception>
    /// <exception cref="System.Security.Cryptography.CryptographicException">The vault content cannot be protected.</exception>
    PublicationSigningKey GetOrCreateSigningKey();
}
