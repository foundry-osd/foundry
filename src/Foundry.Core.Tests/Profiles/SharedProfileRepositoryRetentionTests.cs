// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Foundry.Core.Services.Profiles;

namespace Foundry.Core.Tests.Profiles;

public sealed partial class SharedProfileRepositoryTests
{
    [Theory]
    [InlineData("revisionId")]
    [InlineData("operationId")]
    public async Task Retention_DuplicateAuthenticatedIdentitiesFailClosedWithoutCleanup(string field)
    {
        using var fixture = new RepositoryFixture();
        using var repository = fixture.Open();
        await repository.InitializeAsync(Cancellation);
        Guid firstOperation = Guid.NewGuid();
        SharedProfileSnapshot first = (await repository.PublishAsync([1], null, firstOperation, Cancellation)).Snapshot!;
        SharedProfileSnapshot second = (await repository.PublishAsync([2], first.Head.RevisionId, Guid.NewGuid(), Cancellation)).Snapshot!;
        fixture.RewriteLastJournalRecord(bytes =>
        {
            JsonNode envelope = JsonNode.Parse(bytes)!;
            JsonNode body = JsonNode.Parse(Convert.FromBase64String(envelope["content"]!.GetValue<string>()))!;
            body["revision"]![field] = field == "revisionId" ? first.Head.RevisionId : firstOperation;
            byte[] content = JsonSerializer.SerializeToUtf8Bytes(body);
            byte[] prefix = Encoding.UTF8.GetBytes("Foundry.SharedProfiles.v1/head\0");
            envelope["content"] = Convert.ToBase64String(content);
            envelope["authenticationTag"] = Convert.ToBase64String(HMACSHA256.HashData(fixture.Key.AsSpan(), [.. prefix, .. content]));
            return JsonSerializer.SerializeToUtf8Bytes(envelope);
        });
        using var constrained = fixture.Open(new() { RetainedPayloadCount = 1 });

        Assert.Equal(SharedProfileRepositoryStatus.InvalidData, (await constrained.LoadAsync(first.Head.RevisionId, Cancellation)).Status);
        Assert.Equal(SharedProfileRepositoryStatus.InvalidData,
            (await constrained.PublishAsync([3], second.Head.RevisionId, Guid.NewGuid(), Cancellation)).Status);
        Assert.Equal(2, Directory.GetFiles(fixture.ProfilePath, "*.payload", SearchOption.AllDirectories).Length);
    }

    [Fact]
    public async Task Retention_ExistingHistoryBeyond4096RemainsWritableAndReconcilesOldOperations()
    {
        using var fixture = new RepositoryFixture();
        using var repository = fixture.Open();
        await repository.InitializeAsync(Cancellation);
        var files = new SharedProfileRepositoryFiles(fixture.Key);
        Guid firstOperation = Guid.NewGuid();
        Guid? firstId = null;
        Guid? previousId = null;
        string payloadHash = Convert.ToHexString(SHA256.HashData(new byte[] { 1 }));
        using (FileStream journal = files.OpenJournal(fixture.HeadPath))
        {
            for (int sequence = 1; sequence <= 4096; sequence++)
            {
                Cancellation.ThrowIfCancellationRequested();
                var revision = new SharedProfileRevision(fixture.RepositoryId, fixture.ProfileId, Guid.NewGuid(), previousId,
                    sequence == 1 ? firstOperation : Guid.NewGuid(), sequence, 1, false);
                string path = Path.Combine(fixture.ProfilePath, "revisions", revision.RevisionId.ToString("N"));
                files.WriteSigned(path + ".json", "revision", new { FormatVersion = 1, Revision = revision, PayloadHash = payloadHash, PayloadLength = 1 });
                if (sequence > 4096 - 20) files.WriteBytes(path + ".payload", [1]);
                files.AppendJournal(journal, journal.Length, new
                {
                    FormatVersion = 1,
                    fixture.RepositoryId,
                    fixture.ProfileId,
                    KeyEpoch = 1,
                    Revision = revision
                });
                firstId ??= revision.RevisionId;
                previousId = revision.RevisionId;
            }
        }

        SharedProfileRepositoryResult next = await repository.PublishAsync([2], previousId, Guid.NewGuid(), Cancellation);

        Assert.Equal(SharedProfileRepositoryStatus.Success, next.Status);
        Assert.Equal(4097, next.Snapshot!.Head.Sequence);
        Assert.Equal(20, Directory.GetFiles(fixture.ProfilePath, "*.payload", SearchOption.AllDirectories).Length);
        Assert.Equal(4097, Directory.GetFiles(Path.Combine(fixture.ProfilePath, "revisions"), "*.json").Length);
        using var reopened = fixture.Open();
        Assert.Equal(next.Snapshot.Head.RevisionId, (await reopened.LoadAsync(firstId, Cancellation)).Snapshot!.Head.RevisionId);
        Assert.Equal(firstId, (await reopened.ReconcileAsync(firstOperation, firstId, Cancellation)).CommittedRevisionId);
        Assert.Equal(SharedProfileRepositoryStatus.RollbackDetected, (await reopened.LoadAsync(Guid.NewGuid(), Cancellation)).Status);
        SharedProfileRepositoryResult retry = await reopened.PublishAsync([1], null, firstOperation, Cancellation);
        Assert.Equal(SharedProfileRepositoryStatus.Success, retry.Status);
        Assert.Equal(firstId, retry.CommittedRevisionId);
        Assert.Equal(4097, retry.Snapshot!.Head.Sequence);
    }

    [Fact]
    public async Task Retention_LockedOldPayloadDoesNotFailCommitAndCleanupRetriesLater()
    {
        using var fixture = new RepositoryFixture();
        using var repository = fixture.Open(new() { RetainedPayloadCount = 1 });
        await repository.InitializeAsync(Cancellation);
        SharedProfileSnapshot first = (await repository.PublishAsync([1], null, Guid.NewGuid(), Cancellation)).Snapshot!;
        string oldPayload = Path.Combine(fixture.ProfilePath, "revisions", first.Head.RevisionId.ToString("N") + ".payload");
        SharedProfileRepositoryResult second;
        using (var held = new FileStream(oldPayload, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            second = await repository.PublishAsync([2], first.Head.RevisionId, Guid.NewGuid(), Cancellation);
            Assert.Equal(SharedProfileRepositoryStatus.Success, second.Status);
            Assert.True(File.Exists(oldPayload));
            Assert.Equal(second.Snapshot!.Head.RevisionId, (await repository.LoadAsync(first.Head.RevisionId, Cancellation)).Snapshot!.Head.RevisionId);
        }

        Assert.Equal(SharedProfileRepositoryStatus.Success,
            (await repository.PublishAsync([3], second.Snapshot!.Head.RevisionId, Guid.NewGuid(), Cancellation)).Status);
        Assert.False(File.Exists(oldPayload));
        Assert.Single(Directory.GetFiles(fixture.ProfilePath, "*.payload", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task Retention_PrunesOnlyAuthenticatedCommittedPayloadFiles()
    {
        using var fixture = new RepositoryFixture();
        using var repository = fixture.Open(new() { RetainedPayloadCount = 1 });
        await repository.InitializeAsync(Cancellation);
        SharedProfileSnapshot first = (await repository.PublishAsync([1], null, Guid.NewGuid(), Cancellation)).Snapshot!;
        string revisions = Path.Combine(fixture.ProfilePath, "revisions");
        string orphan = Path.Combine(revisions, Guid.NewGuid().ToString("N") + ".payload");
        string unrelated = Path.Combine(revisions, "notes.payload");
        File.WriteAllBytes(orphan, [7]);
        File.WriteAllBytes(unrelated, [8]);

        Assert.Equal(SharedProfileRepositoryStatus.Success,
            (await repository.PublishAsync([2], first.Head.RevisionId, Guid.NewGuid(), Cancellation)).Status);

        Assert.Equal(new byte[] { 7 }, File.ReadAllBytes(orphan));
        Assert.Equal(new byte[] { 8 }, File.ReadAllBytes(unrelated));
        Assert.False(File.Exists(Path.Combine(revisions, first.Head.RevisionId.ToString("N") + ".payload")));
        Assert.True(File.Exists(Path.Combine(revisions, first.Head.RevisionId.ToString("N") + ".json")));
    }

    [Fact]
    public async Task Retention_ConflictDoesNotDeleteExistingPayloads()
    {
        using var fixture = new RepositoryFixture();
        using var repository = fixture.Open();
        await repository.InitializeAsync(Cancellation);
        SharedProfileSnapshot first = (await repository.PublishAsync([1], null, Guid.NewGuid(), Cancellation)).Snapshot!;
        await repository.PublishAsync([2], first.Head.RevisionId, Guid.NewGuid(), Cancellation);
        using var constrained = fixture.Open(new() { RetainedPayloadCount = 1 });

        Assert.Equal(SharedProfileRepositoryStatus.Conflict,
            (await constrained.PublishAsync([3], first.Head.RevisionId, Guid.NewGuid(), Cancellation)).Status);
        Assert.Equal(2, Directory.GetFiles(fixture.ProfilePath, "*.payload", SearchOption.AllDirectories).Length);
    }

    [Fact]
    public async Task Retention_TombstonePreservesLatestPayloadAndOldOperationRecovery()
    {
        using var fixture = new RepositoryFixture();
        using var repository = fixture.Open(new() { RetainedPayloadCount = 1 });
        await repository.InitializeAsync(Cancellation);
        Guid firstOperation = Guid.NewGuid();
        SharedProfileSnapshot first = (await repository.PublishAsync([1], null, firstOperation, Cancellation)).Snapshot!;
        SharedProfileSnapshot second = (await repository.PublishAsync([2], first.Head.RevisionId, Guid.NewGuid(), Cancellation)).Snapshot!;

        SharedProfileRepositoryResult deleted = await repository.TombstoneAsync(second.Head.RevisionId, Guid.NewGuid(), Cancellation);

        Assert.Equal(SharedProfileRepositoryStatus.Success, deleted.Status);
        Assert.True(deleted.Snapshot!.Head.IsTombstone);
        string retained = Assert.Single(Directory.GetFiles(fixture.ProfilePath, "*.payload", SearchOption.AllDirectories));
        Assert.Equal(second.Head.RevisionId.ToString("N") + ".payload", Path.GetFileName(retained));
        Assert.Equal(first.Head.RevisionId, (await repository.ReconcileAsync(firstOperation, first.Head.RevisionId, Cancellation)).CommittedRevisionId);
        Assert.Equal(SharedProfileRepositoryStatus.Conflict,
            (await repository.PublishAsync([3], deleted.Snapshot.Head.RevisionId, Guid.NewGuid(), Cancellation)).Status);
    }
}
