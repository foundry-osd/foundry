// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Models.Configuration;

namespace Foundry.Core.Services.NetworkCache;

/// <summary>
/// Maintains the <see cref="NetworkCachePublicationRecord"/> list Foundry OSD keeps in its app settings. Pure logic; the caller
/// owns persistence.
/// </summary>
public static class NetworkCachePublicationRecords
{
    /// <summary>
    /// Returns the records after a publication attempt: every share in <paramref name="results"/> gets exactly one record that
    /// replaces its previous ones, and the records of other shares are kept. A share that received an index records its
    /// sequence and <paramref name="fingerprint"/>; a share that received the content without an index (a follower) succeeds
    /// with its sequence unchanged; a share that failed or was unreachable is not succeeded and keeps its last published
    /// sequence and fingerprint. Records without a share id are dropped.
    /// </summary>
    /// <param name="records">The records persisted so far.</param>
    /// <param name="results">The results of the publication attempt, one per share.</param>
    /// <param name="fingerprint">The <see cref="NetworkCachePublicationState.Fingerprint"/> of the published configuration.</param>
    /// <param name="now">When the publication ended.</param>
    public static IReadOnlyList<NetworkCachePublicationRecord> Apply(
        IReadOnlyList<NetworkCachePublicationRecord> records,
        IReadOnlyList<NetworkCacheContentPublishResult> results,
        string fingerprint,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(records);
        ArgumentNullException.ThrowIfNull(results);
        ArgumentNullException.ThrowIfNull(fingerprint);

        List<NetworkCachePublicationRecord> valid = Valid(records);
        Dictionary<string, NetworkCachePublicationRecord> latest = Latest(valid);
        var updated = new Dictionary<string, NetworkCachePublicationRecord>(StringComparer.Ordinal);
        foreach (NetworkCacheContentPublishResult result in results)
        {
            if (string.IsNullOrEmpty(result.Share.Id)) continue;
            NetworkCachePublicationRecord? previous = latest.GetValueOrDefault(result.Share.Id);
            bool succeeded = result.Status == NetworkCacheContentPublishStatus.Published;
            updated[result.Share.Id] = new NetworkCachePublicationRecord
            {
                ShareId = result.Share.Id,
                SharePath = result.Share.Path,
                Fingerprint = succeeded ? fingerprint : previous?.Fingerprint ?? string.Empty,
                Sequence = succeeded ? result.IndexSequence ?? previous?.Sequence ?? 0 : previous?.Sequence ?? 0,
                Succeeded = succeeded,
                PublishedAtUtc = now
            };
        }

        return
        [
            .. valid.Where(record => !updated.ContainsKey(record.ShareId)),
            .. updated.Values
        ];
    }

    /// <summary>
    /// Returns, for every share, the sequence its next index must exceed: the highest of <paramref name="minimumSequence"/> and
    /// every recorded sequence. Giving every share the same floor keeps a share that missed publications from receiving an index
    /// older than the minimum sequence of media built meanwhile from another share's index.
    /// </summary>
    /// <param name="shares">The shares about to be published.</param>
    /// <param name="records">The records persisted so far.</param>
    /// <param name="minimumSequence">The minimum index sequence written into the media being built.</param>
    public static IReadOnlyDictionary<string, long> SequenceFloors(
        IEnumerable<NetworkCacheShare> shares,
        IReadOnlyList<NetworkCachePublicationRecord> records,
        long minimumSequence)
    {
        ArgumentNullException.ThrowIfNull(shares);
        ArgumentNullException.ThrowIfNull(records);
        long floor = Valid(records).Select(record => record.Sequence).Append(minimumSequence).Max();
        var floors = new Dictionary<string, long>(StringComparer.Ordinal);
        foreach (NetworkCacheShare share in shares)
        {
            if (!string.IsNullOrEmpty(share.Id)) floors[share.Id] = floor;
        }

        return floors;
    }

    /// <summary>Returns the records that name a share; persisted settings may hold null entries or empty ids.</summary>
    public static List<NetworkCachePublicationRecord> Valid(IEnumerable<NetworkCachePublicationRecord?>? records) =>
        records?.OfType<NetworkCachePublicationRecord>().Where(record => !string.IsNullOrEmpty(record.ShareId)).ToList() ?? [];

    /// <summary>Returns the record with the highest sequence of each share, ignoring entries <see cref="Valid"/> drops.</summary>
    internal static Dictionary<string, NetworkCachePublicationRecord> Latest(IEnumerable<NetworkCachePublicationRecord?>? records) =>
        Valid(records)
            .GroupBy(record => record.ShareId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.MaxBy(record => record.Sequence)!, StringComparer.Ordinal);
}
