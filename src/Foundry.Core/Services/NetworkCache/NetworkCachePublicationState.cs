// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Foundry.Core.Models.Configuration;
using Foundry.Core.Services.Configuration;

namespace Foundry.Core.Services.NetworkCache;

/// <summary>
/// Remembers what Foundry OSD last published to one share: the content fingerprint it covered and whether the publication
/// finished. Persisted with the Foundry app settings, so it never holds secrets.
/// </summary>
public sealed record NetworkCachePublicationRecord
{
    /// <summary>Gets the id of the share, which is how a record finds its share when the path is edited in case or trailing separator.</summary>
    public string ShareId { get; init; } = string.Empty;

    /// <summary>Gets the UNC path the content was published to, to notice a share id that now points elsewhere.</summary>
    public string SharePath { get; init; } = string.Empty;

    /// <summary>Gets the <see cref="NetworkCachePublicationState.Fingerprint"/> of the content that was published.</summary>
    public string Fingerprint { get; init; } = string.Empty;

    /// <summary>Gets the publication counter of the signed index; the latest record of a share has the highest value.</summary>
    public long Sequence { get; init; }

    /// <summary>Gets whether the publication finished; a failed attempt leaves the share due again.</summary>
    public bool Succeeded { get; init; }

    /// <summary>Gets when the publication ended.</summary>
    public DateTimeOffset PublishedAtUtc { get; init; }
}

/// <summary>Whether the network cache shares have the latest changes of this PC.</summary>
/// <param name="IsUpToDate">Whether every share has the latest content and index.</param>
/// <param name="FailedShares">The shares, in authoring order, whose latest publication attempt failed.</param>
public sealed record NetworkCachePublicationStatus(bool IsUpToDate, IReadOnlyList<NetworkCacheShare> FailedShares)
{
    /// <summary>Gets the status of shares that have the latest changes.</summary>
    public static NetworkCachePublicationStatus UpToDate { get; } = new(true, []);

    /// <summary>Gets the shares, in authoring order, that do not have the latest changes; the failed shares are among them.</summary>
    public IReadOnlyList<NetworkCacheShare> DueShares { get; init; } = [];

    /// <summary>Gets the shares, in authoring order, that received the latest changes but no longer hold them.</summary>
    public IReadOnlyList<NetworkCacheShare> MissingContentShares { get; init; } = [];

    /// <summary>
    /// Gets the shares, in authoring order, that hold no usable publication of this PC: never published to, or found without the
    /// published content. Devices that use them stop at startup; the other due shares still serve an older publication.
    /// </summary>
    public IReadOnlyList<NetworkCacheShare> NotPublishedShares { get; init; } = [];

    /// <summary>Gets the worst state of the shares.</summary>
    public NetworkCachePublicationLevel Level => NotPublishedShares.Count > 0 ? NetworkCachePublicationLevel.NotPublished
        : IsUpToDate ? NetworkCachePublicationLevel.UpToDate
        : NetworkCachePublicationLevel.Outdated;

    /// <summary>Returns the state of one share.</summary>
    /// <param name="shareId">The id of a configured share.</param>
    public NetworkCachePublicationLevel LevelOf(string shareId) =>
        NotPublishedShares.Any(share => string.Equals(share.Id, shareId, StringComparison.Ordinal)) ? NetworkCachePublicationLevel.NotPublished
        : DueShares.Any(share => string.Equals(share.Id, shareId, StringComparison.Ordinal)) ? NetworkCachePublicationLevel.Outdated
        : NetworkCachePublicationLevel.UpToDate;
}

/// <summary>How far a share, or the shares as a whole, are from the latest changes of this PC, from best to worst.</summary>
public enum NetworkCachePublicationLevel
{
    /// <summary>The latest changes are published.</summary>
    UpToDate,

    /// <summary>
    /// An older publication is in place: devices started from media built before the latest publication deploy with it until
    /// the next publication; media built since then requires the newer index and stops at startup on this share.
    /// </summary>
    Outdated,

    /// <summary>No usable publication is in place; devices that use the share stop at startup.</summary>
    NotPublished
}

/// <summary>
/// Decides which shares need a publication: those whose last successful publication does not cover the current content.
/// Pure logic; the caller owns persistence and the publication itself.
/// </summary>
public static class NetworkCachePublicationState
{
    /// <summary>
    /// Returns the lower-case hex SHA-256 of everything that decides the published index and the manifests it binds: the
    /// authoring references and defaults of custom images, custom driver sets and post-installation actions (they decide the
    /// content-derived manifests) and the Deploy content sections mapped from them (they decide the index sections). Network,
    /// share and every other setting leave it unchanged.
    /// </summary>
    /// <param name="configuration">The validated Foundry configuration to be published.</param>
    public static string Fingerprint(FoundryConfigurationDocument configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        string canonicalJson = JsonSerializer.Serialize(
            new
            {
                configuration.CustomImages,
                configuration.CustomDrivers,
                configuration.PreOobe,
                Deploy = DeployConfigurationGenerator.MapContentSections(configuration)
            },
            ConfigurationJsonDefaults.SerializerOptions);
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonicalJson)));
    }

    /// <summary>
    /// Returns the shares, in authoring order, that have no successful record for the current fingerprint. A record belongs
    /// to a share by <see cref="NetworkCachePublicationRecord.ShareId"/> and must also name the same location, compared
    /// without case and trailing separators, so a share id edited to another server is published again. Only the record with
    /// the highest sequence counts for a share. Nothing is due while the network cache is inactive.
    /// </summary>
    /// <param name="configuration">The validated Foundry configuration to be published.</param>
    /// <param name="records">What was published so far.</param>
    public static IReadOnlyList<NetworkCacheShare> SharesNeedingPublication(
        FoundryConfigurationDocument configuration,
        IReadOnlyList<NetworkCachePublicationRecord> records)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(records);
        if (!NetworkCacheContentPublisher.IsActive(configuration.NetworkCache)) return [];

        string fingerprint = Fingerprint(configuration);
        Dictionary<string, NetworkCachePublicationRecord> latest = NetworkCachePublicationRecords.Latest(records);
        return configuration.NetworkCache.Shares
            .Where(share => !latest.TryGetValue(share.Id, out NetworkCachePublicationRecord? record)
                || !record.Succeeded
                || !string.Equals(record.Fingerprint, fingerprint, StringComparison.Ordinal)
                || !SamePath(record.SharePath, share.Path))
            .ToArray();
    }

    /// <summary>
    /// Returns whether the shares have the latest changes of this PC, the publication state the UI shows. A PC that does not
    /// publish the index (a follower) is always up to date, because its shares are published by the reference PC. Otherwise every
    /// share must have a successful record for the current fingerprint; the shares whose latest attempt failed are named, while a
    /// share that only misses newer changes is not. A share found without its published content is due and named too. A due
    /// share that never received an index, or lost its content, is not published; the others are outdated.
    /// </summary>
    /// <param name="configuration">The current Foundry configuration.</param>
    /// <param name="records">What was published so far.</param>
    /// <param name="canPublishIndex">Whether this PC signs and writes the index.</param>
    /// <param name="sharesMissingContent">Ids of the shares whose last check found the published index or content missing.</param>
    public static NetworkCachePublicationStatus Evaluate(
        FoundryConfigurationDocument configuration,
        IReadOnlyList<NetworkCachePublicationRecord> records,
        bool canPublishIndex,
        IReadOnlyCollection<string>? sharesMissingContent = null)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(records);
        if (!canPublishIndex) return NetworkCachePublicationStatus.UpToDate;

        IReadOnlyList<NetworkCacheShare> stale = SharesNeedingPublication(configuration, records);
        NetworkCacheShare[] lost = sharesMissingContent is { Count: > 0 } && NetworkCacheContentPublisher.IsActive(configuration.NetworkCache)
            ? configuration.NetworkCache.Shares.Where(share => sharesMissingContent.Contains(share.Id)).ToArray()
            : [];
        NetworkCacheShare[] due = configuration.NetworkCache.Shares.Where(share => stale.Contains(share) || lost.Contains(share)).ToArray();
        if (due.Length == 0) return NetworkCachePublicationStatus.UpToDate;

        Dictionary<string, NetworkCachePublicationRecord> latest = NetworkCachePublicationRecords.Latest(records);
        // A failed attempt keeps the sequence of the last index written, so a share that never received one has none.
        bool HasPublication(NetworkCacheShare share) =>
            latest.TryGetValue(share.Id, out NetworkCachePublicationRecord? record) && record.Sequence > 0 && SamePath(record.SharePath, share.Path);
        return new NetworkCachePublicationStatus(false, due.Where(share => latest.TryGetValue(share.Id, out var record) && !record.Succeeded).ToArray())
        {
            DueShares = due,
            MissingContentShares = lost,
            NotPublishedShares = due.Where(share => lost.Contains(share) || !HasPublication(share)).ToArray()
        };
    }

    private static bool SamePath(string? left, string? right) =>
        string.Equals((left ?? string.Empty).TrimEnd('\\'), (right ?? string.Empty).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);
}
