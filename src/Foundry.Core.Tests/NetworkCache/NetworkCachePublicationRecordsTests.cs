// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Models.Configuration;
using Foundry.Core.Services.NetworkCache;

namespace Foundry.Core.Tests.NetworkCache;

public sealed class NetworkCachePublicationRecordsTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 27, 12, 0, 0, TimeSpan.Zero);
    private static readonly NetworkCacheShare ShareA = new() { Id = "a", Path = @"\\server\a" };
    private static readonly NetworkCacheShare ShareB = new() { Id = "b", Path = @"\\server\b" };

    [Fact]
    public void Apply_PublishedIndex_RecordsItsSequenceAndFingerprint()
    {
        NetworkCachePublicationRecord previous = Record("a", sequence: 3, succeeded: true, fingerprint: "old");

        IReadOnlyList<NetworkCachePublicationRecord> records = NetworkCachePublicationRecords.Apply(
            [previous], [Result(ShareA, NetworkCacheContentPublishStatus.Published, indexSequence: 4)], "new", Now);

        NetworkCachePublicationRecord record = Assert.Single(records);
        Assert.Equal(new NetworkCachePublicationRecord
        {
            ShareId = "a",
            SharePath = ShareA.Path,
            Fingerprint = "new",
            Sequence = 4,
            Succeeded = true,
            PublishedAtUtc = Now
        }, record);
    }

    [Theory]
    [InlineData(NetworkCacheContentPublishStatus.Failed)]
    [InlineData(NetworkCacheContentPublishStatus.Unreachable)]
    public void Apply_FailedShare_KeepsTheLastPublishedSequenceAndIsNotSucceeded(NetworkCacheContentPublishStatus status)
    {
        NetworkCachePublicationRecord previous = Record("a", sequence: 3, succeeded: true, fingerprint: "old");

        IReadOnlyList<NetworkCachePublicationRecord> records = NetworkCachePublicationRecords.Apply(
            [previous], [Result(ShareA, status, indexSequence: null)], "new", Now);

        NetworkCachePublicationRecord record = Assert.Single(records);
        Assert.False(record.Succeeded);
        Assert.Equal(3, record.Sequence);
        Assert.Equal("old", record.Fingerprint);
        Assert.Equal(Now, record.PublishedAtUtc);
    }

    [Fact]
    public void Apply_FailedShareWithoutPreviousRecord_RecordsSequenceZeroAndNoFingerprint()
    {
        IReadOnlyList<NetworkCachePublicationRecord> records = NetworkCachePublicationRecords.Apply(
            [], [Result(ShareA, NetworkCacheContentPublishStatus.Failed, indexSequence: null)], "new", Now);

        NetworkCachePublicationRecord record = Assert.Single(records);
        Assert.False(record.Succeeded);
        Assert.Equal(0, record.Sequence);
        Assert.Equal(string.Empty, record.Fingerprint);
        Assert.Equal(ShareA.Path, record.SharePath);
    }

    [Fact]
    public void Apply_ContentOnlyPublication_SucceedsWithTheSequenceUnchanged()
    {
        NetworkCachePublicationRecord previous = Record("a", sequence: 2, succeeded: false, fingerprint: "old");

        IReadOnlyList<NetworkCachePublicationRecord> records = NetworkCachePublicationRecords.Apply(
            [previous], [Result(ShareA, NetworkCacheContentPublishStatus.Published, indexSequence: null)], "new", Now);

        NetworkCachePublicationRecord record = Assert.Single(records);
        Assert.True(record.Succeeded);
        Assert.Equal(2, record.Sequence);
        Assert.Equal("new", record.Fingerprint);
    }

    [Fact]
    public void Apply_KeepsOneRecordPerShareAndLeavesOtherSharesUntouched()
    {
        NetworkCachePublicationRecord otherShare = Record("b", sequence: 7, succeeded: true, fingerprint: "old");

        IReadOnlyList<NetworkCachePublicationRecord> records = NetworkCachePublicationRecords.Apply(
            [Record("a", 1, true, "old"), Record("a", 2, false, "old"), otherShare],
            [Result(ShareA, NetworkCacheContentPublishStatus.Published, indexSequence: 3)],
            "new",
            Now);

        Assert.Equal(2, records.Count);
        Assert.Contains(otherShare, records);
        Assert.Equal(3, Assert.Single(records, record => record.ShareId == "a").Sequence);
    }

    [Fact]
    public void Apply_DropsRecordsWithoutShareId()
    {
        IReadOnlyList<NetworkCachePublicationRecord> records = NetworkCachePublicationRecords.Apply(
            [Record("", 5, true, "old"), null!], [Result(ShareA, NetworkCacheContentPublishStatus.Published, indexSequence: 1)], "new", Now);

        Assert.Equal("a", Assert.Single(records).ShareId);
    }

    [Fact]
    public void SequenceFloors_GivesEveryShareTheHighestKnownSequence()
    {
        IReadOnlyDictionary<string, long> floors = NetworkCachePublicationRecords.SequenceFloors(
            [ShareA, ShareB], [Record("a", 4, true, "x"), Record("removed", 9, true, "x")], minimumSequence: 6);

        Assert.Equal(9, floors["a"]);
        Assert.Equal(9, floors["b"]);
    }

    [Fact]
    public void SequenceFloors_WithoutRecords_UsesTheMinimumSequence()
    {
        IReadOnlyDictionary<string, long> floors = NetworkCachePublicationRecords.SequenceFloors([ShareA, ShareB], [], minimumSequence: 6);

        Assert.Equal(6, floors["a"]);
        Assert.Equal(6, floors["b"]);
    }

    private static NetworkCachePublicationRecord Record(string shareId, long sequence, bool succeeded, string fingerprint) => new()
    {
        ShareId = shareId,
        SharePath = $@"\\server\{shareId}",
        Fingerprint = fingerprint,
        Sequence = sequence,
        Succeeded = succeeded,
        PublishedAtUtc = Now.AddDays(-1)
    };

    private static NetworkCacheContentPublishResult Result(NetworkCacheShare share, NetworkCacheContentPublishStatus status, long? indexSequence) =>
        new(share, status, null, null, indexSequence);
}
