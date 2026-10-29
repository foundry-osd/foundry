// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Models.Configuration;
using Foundry.Core.Services.Sharing;
using Foundry.Utilities.Networking;

namespace Foundry.Core.Services.NetworkCache;

/// <summary>Outcome of publishing imported content to one share.</summary>
public enum NetworkCacheContentPublishStatus
{
    /// <summary>The content is on the share.</summary>
    Published,
    /// <summary>The share could not be opened.</summary>
    Unreachable,
    /// <summary>The share was opened but the publication failed.</summary>
    Failed
}

/// <summary>Carries the publication outcome of one share.</summary>
/// <param name="Share">The share.</param>
/// <param name="Status">The outcome.</param>
/// <param name="Connection">The SMB connection result, or <see langword="null"/> when no connection was attempted.</param>
/// <param name="Error">What made the share unreachable without a connection result, or the publication fail.</param>
/// <param name="IndexSequence">The sequence of the index written to the share, or <see langword="null"/> when none was written: a follower, an unreachable share, a content failure or an index write failure.</param>
public sealed record NetworkCacheContentPublishResult(
    NetworkCacheShare Share,
    NetworkCacheContentPublishStatus Status,
    SmbConnectResult? Connection,
    Exception? Error,
    long? IndexSequence = null);

/// <summary>What the reference or standalone PC needs to sign and publish the content index after the content itself.</summary>
/// <param name="Signer">The publication signing key; its key id names the index file.</param>
/// <param name="Sections">The bound custom image, custom driver and post-installation sections the index carries.</param>
/// <param name="RecordedSequences">The last sequence published to each share, by share id; the next one continues from the larger of this and the sequence found on the share.</param>
public sealed record NetworkCacheIndexPublication(
    PublicationSigningKey Signer,
    NetworkCacheContentSections Sections,
    IReadOnlyDictionary<string, long> RecordedSequences);

/// <summary>
/// Publishes the imported content of a media build (custom images, post-installation packages, custom driver sets) to every
/// share of the network cache, so devices started without that media, PXE included, find it on their share. The caller
/// supplies the publication, which runs once per share against <c>&lt;share&gt;\Foundry</c> with the media layout, so
/// manifest bindings of the boot configuration resolve on the share exactly as on media. Shares are processed in order;
/// a share error is a result, never an exception, and only cancellation propagates. On a share the signed content index
/// is written last, after the content: readers locate everything through the index, so a share never advertises content it
/// does not yet hold.
/// </summary>
public sealed class NetworkCacheContentPublisher(ISmbConnector connector)
{
    private readonly NetworkCacheShareAccess access = new(connector);

    /// <summary>
    /// Gets whether media creation publishes imported content: the section is enabled and has at least one share. ISO and USB
    /// media then leave the imported content out, so devices take it from their share; otherwise media stay standalone.
    /// </summary>
    public static bool IsActive(NetworkCacheSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return settings.IsEnabled && settings.Shares.Count > 0;
    }

    /// <summary>Publishes to every share of an active section and returns one result per share, in share order.</summary>
    /// <param name="settings">The network cache section of the build snapshot.</param>
    /// <param name="getPasswordCopy">Returns an owned copy of an account password by id; each copy is cleared after use.</param>
    /// <param name="publishContent">Publishes the content under the given content root; it may throw for any share error, which also skips the index of that share.</param>
    /// <param name="index">The signed index to publish after the content of each share, or <see langword="null"/> on a follower, which never writes an index.</param>
    /// <param name="cancellationToken">Cancels the publication; the current share is closed before the cancellation propagates.</param>
    public async Task<IReadOnlyList<NetworkCacheContentPublishResult>> PublishAsync(
        NetworkCacheSettings settings,
        Func<string, char[]?> getPasswordCopy,
        Func<string, CancellationToken, Task> publishContent,
        NetworkCacheIndexPublication? index,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(getPasswordCopy);
        ArgumentNullException.ThrowIfNull(publishContent);
        if (!IsActive(settings)) return [];

        var results = new List<NetworkCacheContentPublishResult>(settings.Shares.Count);
        foreach (NetworkCacheShare share in settings.Shares)
        {
            NetworkCacheShareAccessResult<long?> result = await access.RunAsync<long?>(settings, share, getPasswordCopy, async (contentRoot, token) =>
            {
                await publishContent(contentRoot, token).ConfigureAwait(false);
                return index is null ? null : await WriteIndexAsync(index, share, contentRoot, token).ConfigureAwait(false);
            }, cancellationToken).ConfigureAwait(false);
            results.Add(new(share, result.Status switch
            {
                NetworkCacheShareAccessStatus.Completed => NetworkCacheContentPublishStatus.Published,
                NetworkCacheShareAccessStatus.Unreachable => NetworkCacheContentPublishStatus.Unreachable,
                _ => NetworkCacheContentPublishStatus.Failed
            }, result.Connection, result.Error, result.Value));
        }

        return results;
    }

    /// <summary>Signs and writes the index with the sequence that follows the larger of the recorded one and the one already on the share.</summary>
    private static async Task<long> WriteIndexAsync(NetworkCacheIndexPublication publication, NetworkCacheShare share, string contentRoot, CancellationToken cancellationToken)
    {
        PublicationSigningKey signer = publication.Signer;
        long onShare = await NetworkCacheIndexStore.ReadSequenceAsync(contentRoot, signer.PublicKey, cancellationToken).ConfigureAwait(false);
        long sequence = Math.Max(publication.RecordedSequences.GetValueOrDefault(share.Id), onShare) + 1;
        byte[] signedIndex = NetworkCacheContentIndexCodec.Write(new NetworkCacheContentIndex
        {
            KeyId = signer.KeyId,
            Sequence = sequence,
            PublishedAtUtc = DateTimeOffset.UtcNow,
            CustomImages = publication.Sections.CustomImages,
            CustomDrivers = publication.Sections.CustomDrivers,
            PreOobe = publication.Sections.PreOobe
        }, signer);
        await NetworkCacheIndexStore.WriteAsync(contentRoot, signer.KeyId, signedIndex, cancellationToken).ConfigureAwait(false);
        return sequence;
    }
}
