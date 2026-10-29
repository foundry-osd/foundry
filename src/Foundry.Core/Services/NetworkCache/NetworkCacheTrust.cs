// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Services.Sharing;

namespace Foundry.Core.Services.NetworkCache;

/// <summary>Decides which key boot media trust for the signed content index, and whether this PC publishes that index.</summary>
public static class NetworkCacheTrust
{
    /// <summary>
    /// Returns the trusted key and whether this PC writes the index. A reference or standalone PC trusts and signs with its own
    /// key; a follower trusts the reference key it pinned and never writes an index. Without the key its role needs, nothing is
    /// trusted and media with an active network cache cannot be built.
    /// </summary>
    /// <param name="role">The configuration sharing role of this PC.</param>
    /// <param name="ownPublicKey">The DER SubjectPublicKeyInfo of this PC's signing key, or <see langword="null"/> when it has none.</param>
    /// <param name="pinnedPublicKey">The DER SubjectPublicKeyInfo of the reference key a follower pinned, or <see langword="null"/>.</param>
    public static (byte[] PublicKey, bool CanPublishIndex)? Resolve(SharingRole role, byte[]? ownPublicKey, byte[]? pinnedPublicKey)
    {
        if (CanPublish(role)) return ownPublicKey is { Length: > 0 } ? (ownPublicKey, true) : null;
        return role == SharingRole.Follower && pinnedPublicKey is { Length: > 0 } ? (pinnedPublicKey, false) : null;
    }

    /// <summary>Returns whether a PC with <paramref name="role"/> signs and writes the index: a reference or standalone PC does, a follower never does.</summary>
    public static bool CanPublish(SharingRole role) => role is SharingRole.None or SharingRole.Reference;

    /// <summary>
    /// Returns whether the network cache creates the signing key of a PC with <paramref name="role"/> when it has none: only a
    /// standalone PC does. A reference PC signs configuration publications with the same key, which configuration sharing
    /// creates and replaces when it starts or takes over publishing; a reference PC without a usable key trusts nothing.
    /// </summary>
    public static bool CanCreateSigningKey(SharingRole role) => role == SharingRole.None;
}
