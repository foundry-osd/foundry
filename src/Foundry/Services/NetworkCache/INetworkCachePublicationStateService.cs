// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Models.Configuration;
using Foundry.Core.Services.NetworkCache;

namespace Foundry.Services.NetworkCache;

/// <summary>
/// Tells whether the network cache shares have the latest changes of this PC (up to date, outdated, or not published),
/// and keeps the publication records it is computed from.
/// </summary>
/// <remarks>Members run on the UI thread.</remarks>
public interface INetworkCachePublicationStateService
{
    /// <summary>
    /// Raised when <see cref="Level"/>, <see cref="FailedSharePaths"/>, the state of a share, <see cref="CanPublishIndex"/>,
    /// <see cref="CanSignIndex"/> or <see cref="HasTrustedKey"/> changes.
    /// </summary>
    event EventHandler? Changed;

    /// <summary>
    /// Gets whether media can trust a key for the index (<see cref="INetworkCacheSigningService.TrustedPublicKey"/>): this PC's
    /// own key, or the reference key a follower pinned. Without it, media with an active network cache cannot be created.
    /// </summary>
    bool HasTrustedKey { get; }

    /// <summary>Gets whether this PC publishes the index; on a follower the state is always up to date and Publish is hidden.</summary>
    bool CanPublishIndex { get; }

    /// <summary>Gets whether this PC can sign the index now; without a usable signing key, Publish stays disabled and the page says why.</summary>
    bool CanSignIndex { get; }

    /// <summary>Gets the worst state of the shares of the active network cache; always up to date when the cache is off.</summary>
    NetworkCachePublicationLevel Level { get; }

    /// <summary>Gets the paths of the shares whose latest publication attempt failed, in authoring order.</summary>
    IReadOnlyList<string> FailedSharePaths { get; }

    /// <summary>Gets the paths of the shares that received the latest changes but no longer hold them, in authoring order.</summary>
    IReadOnlyList<string> MissingContentSharePaths { get; }

    /// <summary>Gets the state of the share with this id; always up to date when the whole cache is.</summary>
    /// <param name="shareId">The id of a configured share.</param>
    NetworkCachePublicationLevel LevelOf(string shareId);

    /// <summary>
    /// Returns what the share must still hold for it to count as up to date: the public key this PC signs with and the sequence
    /// it last published there; <see langword="null"/> when there is nothing to check, because this PC does not publish, has no
    /// key, or has not published the latest changes to that share.
    /// </summary>
    /// <param name="shareId">The id of a configured share.</param>
    NetworkCacheShareExpectation? GetExpectation(string shareId);

    /// <summary>Records the result of checking a share against <see cref="GetExpectation"/> and re-evaluates the state.</summary>
    /// <param name="shareId">The id of the checked share.</param>
    /// <param name="isIntact">Whether the share still holds the index and the content this PC published.</param>
    void ReportContentCheck(string shareId, bool isIntact);

    /// <summary>
    /// Replaces the publication record of each share in <paramref name="results"/> with the outcome of this attempt, saves the app
    /// settings and re-evaluates the state. A failed save is already logged by the settings service and leaves the shares due again.
    /// </summary>
    /// <param name="published">The configuration the attempt published.</param>
    /// <param name="results">One result per share.</param>
    void Record(FoundryConfigurationDocument published, IReadOnlyList<NetworkCacheContentPublishResult> results);
}

/// <summary>What a share must hold for its last publication by this PC to stand.</summary>
/// <param name="PublicKey">The public key this PC signs the index with.</param>
/// <param name="Sequence">The sequence this PC last published to the share.</param>
public sealed record NetworkCacheShareExpectation(byte[] PublicKey, long Sequence);
