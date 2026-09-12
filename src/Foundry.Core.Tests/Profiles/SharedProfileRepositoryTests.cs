// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Foundry.Core.Services.Profiles;

namespace Foundry.Core.Tests.Profiles;

public sealed class SharedProfileRepositoryTests
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Publish_CompetingWritersCannotReplaceOneAnother()
    {
        using var fixture = new RepositoryFixture();
        using var first = fixture.Open();
        using var second = fixture.Open();
        Assert.Equal(SharedProfileRepositoryStatus.Success, (await first.InitializeAsync(Cancellation)).Status);
        SharedProfileRepositoryResult initial = await first.PublishAsync([1], null, Guid.NewGuid(), Cancellation);
        Guid baseId = Assert.IsType<SharedProfileSnapshot>(initial.Snapshot).Head.RevisionId;

        SharedProfileRepositoryResult[] results = await Task.WhenAll(
            first.PublishAsync([2], baseId, Guid.NewGuid(), Cancellation),
            second.PublishAsync([3], baseId, Guid.NewGuid(), Cancellation));

        Assert.Single(results, result => result.Status == SharedProfileRepositoryStatus.Success);
        Assert.Single(results, result => result.Status is SharedProfileRepositoryStatus.Conflict or SharedProfileRepositoryStatus.Busy);
        SharedProfileSnapshot winner = Assert.IsType<SharedProfileSnapshot>((await first.LoadAsync(baseId, Cancellation)).Snapshot);
        Assert.Equal(baseId, winner.Head.ParentRevisionId);
        Assert.Contains(winner.EncryptedPayload[0], new byte[] { 2, 3 });
        Assert.Equal(SharedProfileRepositoryStatus.Conflict,
            (await second.PublishAsync([4], baseId, Guid.NewGuid(), Cancellation)).Status);
    }

    [Fact]
    public async Task Retry_RecognizesCommittedOperationAfterAnotherWriterAdvancesHead()
    {
        using var fixture = new RepositoryFixture();
        using var repository = fixture.Open();
        await repository.InitializeAsync(Cancellation);
        Guid operation = Guid.NewGuid();
        SharedProfileSnapshot initial = Assert.IsType<SharedProfileSnapshot>(
            (await repository.PublishAsync([4, 5], null, operation, Cancellation)).Snapshot);
        SharedProfileSnapshot current = Assert.IsType<SharedProfileSnapshot>(
            (await repository.PublishAsync([6], initial.Head.RevisionId, Guid.NewGuid(), Cancellation)).Snapshot);

        SharedProfileRepositoryResult retry = await repository.PublishAsync([4, 5], null, operation, Cancellation);

        Assert.Equal(SharedProfileRepositoryStatus.Success, retry.Status);
        Assert.Equal(initial.Head.RevisionId, retry.CommittedRevisionId);
        Assert.Equal(current.Head.RevisionId, retry.Snapshot!.Head.RevisionId);
        Assert.Equal(initial.Head.RevisionId, (await repository.ReconcileAsync(operation, cancellationToken: Cancellation)).CommittedRevisionId);
        Assert.Equal(SharedProfileRepositoryStatus.InvalidData, (await repository.PublishAsync([7], null, operation, Cancellation)).Status);
    }

    [Fact]
    public async Task Load_RejectsWrongKeyAndAlteredPayload()
    {
        using var fixture = new RepositoryFixture();
        using var repository = fixture.Open();
        await repository.InitializeAsync(Cancellation);
        Assert.Equal(SharedProfileRepositoryStatus.Success, (await repository.PublishAsync([1, 2, 3], null, Guid.NewGuid(), Cancellation)).Status);
        using var wrongKey = fixture.Open(key: RandomNumberGenerator.GetBytes(32));
        Assert.Equal(SharedProfileRepositoryStatus.InvalidData, (await wrongKey.LoadAsync(cancellationToken: Cancellation)).Status);

        string payload = Assert.Single(Directory.GetFiles(fixture.Root, "*.payload", SearchOption.AllDirectories));
        await File.WriteAllBytesAsync(payload, [3, 2, 1], Cancellation);

        Assert.Equal(SharedProfileRepositoryStatus.InvalidData, (await repository.LoadAsync(cancellationToken: Cancellation)).Status);
    }

    [Fact]
    public async Task Publish_RejectsRollbackAndDoesNotResurrectDeletedHead()
    {
        using var fixture = new RepositoryFixture();
        using var repository = fixture.Open();
        await repository.InitializeAsync(Cancellation);
        SharedProfileSnapshot first = (await repository.PublishAsync([1], null, Guid.NewGuid(), Cancellation)).Snapshot!;
        byte[] oldHead = await File.ReadAllBytesAsync(fixture.HeadPath, Cancellation);
        SharedProfileSnapshot second = (await repository.PublishAsync([2], first.Head.RevisionId, Guid.NewGuid(), Cancellation)).Snapshot!;
        await File.WriteAllBytesAsync(fixture.HeadPath, oldHead, Cancellation);

        Assert.Equal(SharedProfileRepositoryStatus.RollbackDetected, (await repository.LoadAsync(second.Head.RevisionId, Cancellation)).Status);
        Assert.Equal(SharedProfileRepositoryStatus.RollbackDetected,
            (await repository.PublishAsync([3], second.Head.RevisionId, Guid.NewGuid(), Cancellation)).Status);

        File.Delete(fixture.HeadPath);
        Assert.Equal(SharedProfileRepositoryStatus.Unavailable,
            (await repository.PublishAsync([3], second.Head.RevisionId, Guid.NewGuid(), Cancellation)).Status);
    }

    [Fact]
    public async Task Tombstone_PreservesHistoryAndRejectsEditAndNullBaseResurrection()
    {
        using var fixture = new RepositoryFixture();
        using var repository = fixture.Open();
        await repository.InitializeAsync(Cancellation);
        Guid initialOperation = Guid.NewGuid();
        SharedProfileSnapshot first = (await repository.PublishAsync([1], null, initialOperation, Cancellation)).Snapshot!;
        Guid deleteOperation = Guid.NewGuid();
        SharedProfileRepositoryResult deletion = await repository.TombstoneAsync(first.Head.RevisionId, deleteOperation, Cancellation);

        Assert.True(deletion.Snapshot!.Head.IsTombstone);
        Assert.Empty(deletion.Snapshot.EncryptedPayload);
        Assert.Equal(SharedProfileRepositoryStatus.Success,
            (await repository.TombstoneAsync(first.Head.RevisionId, deleteOperation, Cancellation)).Status);
        Assert.Equal(SharedProfileRepositoryStatus.Success, (await repository.ReconcileAsync(initialOperation, first.Head.RevisionId, Cancellation)).Status);
        Assert.Equal(SharedProfileRepositoryStatus.Conflict, (await repository.PublishAsync([2], null, Guid.NewGuid(), Cancellation)).Status);
        Assert.Equal(SharedProfileRepositoryStatus.Conflict, (await repository.PublishAsync([2], deletion.Snapshot.Head.RevisionId, Guid.NewGuid(), Cancellation)).Status);
    }

    [Fact]
    public async Task Reconcile_IncompleteCommitTailDoesNotTreatOrphanAsCommitted()
    {
        using var fixture = new RepositoryFixture();
        using var repository = fixture.Open();
        await repository.InitializeAsync(Cancellation);
        SharedProfileSnapshot initial = (await repository.PublishAsync([1], null, Guid.NewGuid(), Cancellation)).Snapshot!;
        Guid operation = Guid.NewGuid();
        SharedProfileSnapshot interrupted = (await repository.PublishAsync([2], initial.Head.RevisionId, operation, Cancellation)).Snapshot!;
        using (var journal = new FileStream(fixture.HeadPath, FileMode.Open, FileAccess.Write, FileShare.None))
            journal.SetLength(journal.Length - 1);

        Assert.Equal(2, Directory.GetFiles(fixture.Root, "*.payload", SearchOption.AllDirectories).Length);
        Assert.Equal(SharedProfileRepositoryStatus.RollbackDetected,
            (await repository.LoadAsync(interrupted.Head.RevisionId, Cancellation)).Status);
        Assert.Equal(SharedProfileRepositoryStatus.NotCommitted, (await repository.ReconcileAsync(operation, initial.Head.RevisionId, Cancellation)).Status);
        SharedProfileRepositoryResult retry = await repository.PublishAsync([2], initial.Head.RevisionId, operation, Cancellation);
        Assert.Equal(SharedProfileRepositoryStatus.Success, retry.Status);
        Assert.Equal(retry.Snapshot!.Head.RevisionId, (await repository.ReconcileAsync(operation, initial.Head.RevisionId, Cancellation)).CommittedRevisionId);
    }

    [Fact]
    public async Task StableLock_ReturnsBusyAndRemainsUsableAfterHandleCloses()
    {
        using var fixture = new RepositoryFixture();
        using var repository = fixture.Open();
        await repository.InitializeAsync(Cancellation);
        string lockPath = fixture.HeadPath;
        using (var held = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
        {
            Assert.Equal(SharedProfileRepositoryStatus.Busy,
                (await repository.PublishAsync([1], null, Guid.NewGuid(), Cancellation)).Status);
            Assert.Equal(SharedProfileRepositoryStatus.Busy, (await repository.LoadAsync(cancellationToken: Cancellation)).Status);
        }
        Assert.True(File.Exists(lockPath));
        Assert.Equal(SharedProfileRepositoryStatus.Success,
            (await repository.PublishAsync([1], null, Guid.NewGuid(), Cancellation)).Status);
    }

    [Fact]
    public async Task BoundedHistory_RefusesNewPublicationWithoutDeletingCommittedHistory()
    {
        using var fixture = new RepositoryFixture();
        using var repository = fixture.Open(new() { MaximumHistoryCount = 2 });
        await repository.InitializeAsync(Cancellation);
        SharedProfileSnapshot first = (await repository.PublishAsync([1], null, Guid.NewGuid(), Cancellation)).Snapshot!;
        SharedProfileSnapshot second = (await repository.PublishAsync([2], first.Head.RevisionId, Guid.NewGuid(), Cancellation)).Snapshot!;

        Assert.Equal(SharedProfileRepositoryStatus.HistoryLimitExceeded,
            (await repository.PublishAsync([3], second.Head.RevisionId, Guid.NewGuid(), Cancellation)).Status);
        Assert.Equal(second.Head.RevisionId, (await repository.LoadAsync(first.Head.RevisionId, Cancellation)).Snapshot!.Head.RevisionId);
        using var constrainedReader = fixture.Open(new() { MaximumHistoryCount = 1 });
        Assert.Equal(SharedProfileRepositoryStatus.HistoryLimitExceeded,
            (await constrainedReader.ReconcileAsync(Guid.NewGuid(), cancellationToken: Cancellation)).Status);
    }

    [Fact]
    public async Task MissingStorage_NeverReinitializesDuringOrdinaryOperationsOrExistingSession()
    {
        using var fixture = new RepositoryFixture();
        using var repository = fixture.Open();
        Assert.Equal(SharedProfileRepositoryStatus.Unavailable, (await repository.LoadAsync(cancellationToken: Cancellation)).Status);
        Assert.False(Directory.Exists(fixture.Root));
        await repository.InitializeAsync(Cancellation);
        Directory.Delete(fixture.Root, recursive: true);

        Assert.Equal(SharedProfileRepositoryStatus.Unavailable,
            (await repository.PublishAsync([1], null, Guid.NewGuid(), Cancellation)).Status);
        Assert.Equal(SharedProfileRepositoryStatus.Unavailable, (await repository.InitializeAsync(Cancellation)).Status);
        Assert.False(Directory.Exists(fixture.Root));
    }

    [Theory]
    [InlineData("formatVersion", 2, SharedProfileRepositoryStatus.UnsupportedFormat)]
    [InlineData("keyEpoch", 2, SharedProfileRepositoryStatus.InvalidData)]
    public async Task AuthenticatedManifest_RejectsUnsupportedFormatAndMismatchedEnrollment(string field, int value, SharedProfileRepositoryStatus status)
    {
        using var fixture = new RepositoryFixture();
        using var repository = fixture.Open();
        await repository.InitializeAsync(Cancellation);
        string path = Path.Combine(fixture.Root, "repository.json");
        fixture.RewriteSignedRecord(path, "repository", json => json[field] = value);

        Assert.Equal(status, (await repository.LoadAsync(cancellationToken: Cancellation)).Status);
    }

    [Fact]
    public async Task AuthenticatedRevision_RejectsIdentitySubstitutionAndBrokenAncestry()
    {
        using var fixture = new RepositoryFixture();
        using var repository = fixture.Open();
        await repository.InitializeAsync(Cancellation);
        SharedProfileSnapshot first = (await repository.PublishAsync([1], null, Guid.NewGuid(), Cancellation)).Snapshot!;
        string path = Path.Combine(fixture.ProfilePath, "revisions", first.Head.RevisionId.ToString("N") + ".json");
        byte[] original = await File.ReadAllBytesAsync(path, Cancellation);
        fixture.RewriteSignedRecord(path, "revision", json => json["revision"]!["profileId"] = Guid.NewGuid());
        Assert.Equal(SharedProfileRepositoryStatus.InvalidData, (await repository.LoadAsync(cancellationToken: Cancellation)).Status);
        await File.WriteAllBytesAsync(path, original, Cancellation);
        fixture.RewriteSignedRecord(path, "revision", json => json["revision"]!["sequence"] = 2);
        Assert.Equal(SharedProfileRepositoryStatus.InvalidData, (await repository.LoadAsync(cancellationToken: Cancellation)).Status);
    }

    [Fact]
    public async Task MissingHead_CannotBeMistakenForANewProfile()
    {
        using var fixture = new RepositoryFixture();
        using var repository = fixture.Open();
        await repository.InitializeAsync(Cancellation);
        Assert.Null((await repository.LoadAsync(cancellationToken: Cancellation)).Snapshot);
        File.Delete(fixture.HeadPath);

        Assert.Equal(SharedProfileRepositoryStatus.Unavailable, (await repository.LoadAsync(cancellationToken: Cancellation)).Status);
        Assert.Equal(SharedProfileRepositoryStatus.Unavailable,
            (await repository.PublishAsync([1], null, Guid.NewGuid(), Cancellation)).Status);
    }

    [Fact]
    public async Task Load_RejectsOversizedAndDuplicateMetadataBeforeAcceptingState()
    {
        using var fixture = new RepositoryFixture();
        using var repository = fixture.Open();
        await repository.InitializeAsync(Cancellation);
        await repository.PublishAsync([1], null, Guid.NewGuid(), Cancellation);
        byte[] original = await File.ReadAllBytesAsync(fixture.HeadPath, Cancellation);
        byte[] oversized = new byte[70_000];
        BinaryPrimitives.WriteInt32LittleEndian(oversized.AsSpan(0, 4), 70_000);
        await File.WriteAllBytesAsync(fixture.HeadPath, oversized, Cancellation);
        Assert.Equal(SharedProfileRepositoryStatus.InvalidData, (await repository.LoadAsync(cancellationToken: Cancellation)).Status);

        await File.WriteAllBytesAsync(fixture.HeadPath, original, Cancellation);
        fixture.RewriteLastJournalRecord(body => Encoding.UTF8.GetBytes("{\"content\":\"\"," + Encoding.UTF8.GetString(body)[1..]));
        Assert.Equal(SharedProfileRepositoryStatus.InvalidData, (await repository.LoadAsync(cancellationToken: Cancellation)).Status);
    }

    [Fact]
    public async Task Journal_CompleteUnauthenticatedCommitFailsClosedInsteadOfFallingBack()
    {
        using var fixture = new RepositoryFixture();
        using var repository = fixture.Open();
        await repository.InitializeAsync(Cancellation);
        SharedProfileSnapshot first = (await repository.PublishAsync([1], null, Guid.NewGuid(), Cancellation)).Snapshot!;
        await repository.PublishAsync([2], first.Head.RevisionId, Guid.NewGuid(), Cancellation);
        fixture.RewriteLastJournalRecord(body =>
        {
            JsonNode record = JsonNode.Parse(body)!;
            record["authenticationTag"] = Convert.ToBase64String(new byte[32]);
            return Encoding.UTF8.GetBytes(record.ToJsonString());
        });

        Assert.Equal(SharedProfileRepositoryStatus.InvalidData, (await repository.LoadAsync(first.Head.RevisionId, Cancellation)).Status);
        Assert.Equal(SharedProfileRepositoryStatus.InvalidData,
            (await repository.PublishAsync([3], first.Head.RevisionId, Guid.NewGuid(), Cancellation)).Status);
    }

    [Fact]
    public async Task Journal_LostHandleCannotOverwriteACommitMadeByANewOwner()
    {
        using var fixture = new RepositoryFixture();
        using var repository = fixture.Open();
        await repository.InitializeAsync(Cancellation);
        SharedProfileSnapshot first = (await repository.PublishAsync([1], null, Guid.NewGuid(), Cancellation)).Snapshot!;
        var files = new SharedProfileRepositoryFiles(fixture.Key);
        using FileStream staleHandle = files.OpenJournal(fixture.HeadPath);
        SharedProfileJournal<JsonElement> observed = files.ReadJournal<JsonElement>(staleHandle, 10, Cancellation);
        staleHandle.Dispose();
        SharedProfileSnapshot second = (await repository.PublishAsync([2], first.Head.RevisionId, Guid.NewGuid(), Cancellation)).Snapshot!;

        Assert.Throws<ObjectDisposedException>(() => files.AppendJournal(staleHandle, observed.CommittedLength, observed.Records[^1]));
        Assert.Equal(second.Head.RevisionId, (await repository.LoadAsync(second.Head.RevisionId, Cancellation)).Snapshot!.Head.RevisionId);
    }

    [Fact]
    public async Task Publish_CopiesInputAndEnforcesPayloadLimit()
    {
        using var fixture = new RepositoryFixture();
        using var repository = fixture.Open(new() { MaximumPayloadBytes = 3 });
        await repository.InitializeAsync(Cancellation);
        byte[] payload = [1, 2, 3];
        Task<SharedProfileRepositoryResult> publication = repository.PublishAsync(payload, null, Guid.NewGuid(), Cancellation);
        payload[0] = 99;
        SharedProfileSnapshot snapshot = (await publication).Snapshot!;
        Assert.Equal(new byte[] { 1, 2, 3 }, snapshot.EncryptedPayload);
        snapshot.EncryptedPayload[1] = 99;
        Assert.Equal(new byte[] { 1, 2, 3 }, (await repository.LoadAsync(cancellationToken: Cancellation)).Snapshot!.EncryptedPayload);
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => repository.PublishAsync([1, 2, 3, 4], snapshot.Head.RevisionId, Guid.NewGuid(), Cancellation));
    }

    [Fact]
    public async Task Cancellation_StopsPublicationBeforeAnySharedWrite()
    {
        using var fixture = new RepositoryFixture();
        using var repository = fixture.Open();
        await repository.InitializeAsync(Cancellation);
        using var canceled = new CancellationTokenSource();
        await canceled.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => repository.PublishAsync([1], null, Guid.NewGuid(), canceled.Token));
        Assert.Null((await repository.LoadAsync(cancellationToken: Cancellation)).Snapshot);
    }

    private sealed class RepositoryFixture : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "FoundrySharedProfileTests", Guid.NewGuid().ToString("N"));
        public Guid RepositoryId { get; } = Guid.NewGuid();
        public Guid ProfileId { get; } = Guid.NewGuid();
        public byte[] Key { get; } = RandomNumberGenerator.GetBytes(32);
        public string ProfilePath => Path.Combine(Root, "profiles", ProfileId.ToString("N"));
        public string HeadPath => Path.Combine(ProfilePath, "head.journal");

        public SharedProfileRepository Open(SharedProfileRepositoryOptions? options = null, byte[]? key = null) =>
            new(Root, RepositoryId, ProfileId, 1, key ?? Key, options);

        public void RewriteSignedRecord(string path, string purpose, Action<JsonNode> change)
        {
            JsonNode envelope = JsonNode.Parse(File.ReadAllText(path))!;
            JsonNode content = JsonNode.Parse(Convert.FromBase64String(envelope["content"]!.GetValue<string>()))!;
            change(content);
            byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(content);
            byte[] prefix = Encoding.UTF8.GetBytes("Foundry.SharedProfiles.v1/" + purpose + "\0");
            envelope["content"] = Convert.ToBase64String(bytes);
            envelope["authenticationTag"] = Convert.ToBase64String(HMACSHA256.HashData(Key.AsSpan(), [.. prefix, .. bytes]));
            File.WriteAllText(path, envelope.ToJsonString());
        }

        public void RewriteLastJournalRecord(Func<byte[], byte[]> rewrite)
        {
            byte[] journal = File.ReadAllBytes(HeadPath);
            int offset = 0;
            int last = 0;
            while (offset < journal.Length)
            {
                last = offset;
                int length = BinaryPrimitives.ReadInt32LittleEndian(journal.AsSpan(offset, 4));
                offset += length + 8;
            }
            int oldLength = BinaryPrimitives.ReadInt32LittleEndian(journal.AsSpan(last, 4));
            byte[] record = rewrite(journal.AsSpan(last + 4, oldLength).ToArray());
            byte[] rewritten = new byte[last + record.Length + 8];
            journal.AsSpan(0, last).CopyTo(rewritten);
            BinaryPrimitives.WriteInt32LittleEndian(rewritten.AsSpan(last, 4), record.Length);
            record.CopyTo(rewritten, last + 4);
            BinaryPrimitives.WriteInt32LittleEndian(rewritten.AsSpan(last + 4 + record.Length, 4), record.Length);
            File.WriteAllBytes(HeadPath, rewritten);
        }

        public void Dispose()
        {
            if (Directory.Exists(Root)) Directory.Delete(Root, recursive: true);
            CryptographicOperations.ZeroMemory(Key);
        }
    }
}
