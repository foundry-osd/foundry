// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Security.Cryptography;
using System.Text;
using Foundry.Core.Services.Profiles;

namespace Foundry.Core.Tests.Profiles;

public sealed partial class SharedProfileRepositoryTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public async Task Initialize_InterruptedOwnedStagingRetriesWithoutReplacingPublishedStorage(int stage)
    {
        using var fixture = new RepositoryFixture();
        string staging = InitializationPath(fixture);
        Directory.CreateDirectory(staging);
        if (stage >= 1) File.WriteAllBytes(Path.Combine(staging, "repository.json"), [1, 2]);
        if (stage >= 2)
        {
            Directory.CreateDirectory(Path.Combine(staging, "profile", "revisions"));
            File.WriteAllBytes(Path.Combine(staging, "profile", "head.journal"), [1, 2]);
        }
        if (stage >= 3)
        {
            var files = new SharedProfileRepositoryFiles(fixture.Key);
            files.WriteSigned(Path.Combine(fixture.Root, "repository.json"), "repository", new { FormatVersion = 1, fixture.RepositoryId, KeyEpoch = 1 });
        }
        using var repository = fixture.Open();

        Assert.Equal(SharedProfileRepositoryStatus.Success, (await repository.InitializeAsync(Cancellation)).Status);
        SharedProfileRepositoryResult published = await repository.PublishAsync([1, 2, 3], null, Guid.NewGuid(), Cancellation);

        Assert.Equal(SharedProfileRepositoryStatus.Success, published.Status);
        using var reopened = fixture.Open();
        Assert.Equal(published.Snapshot!.Head.RevisionId,
            (await reopened.LoadAsync(published.CommittedRevisionId, Cancellation)).Snapshot!.Head.RevisionId);
        Assert.False(Directory.Exists(staging));
    }

    [Theory]
    [InlineData("head.journal")]
    [InlineData("repository.json")]
    public async Task Initialize_MissingEstablishedMetadataCannotBeRecoveredFromStaging(string missingFile)
    {
        using var fixture = new RepositoryFixture();
        using (var repository = fixture.Open())
        {
            await repository.InitializeAsync(Cancellation);
            await repository.PublishAsync([4], null, Guid.NewGuid(), Cancellation);
        }
        string missingPath = missingFile == "head.journal" ? fixture.HeadPath : Path.Combine(fixture.Root, missingFile);
        File.Delete(missingPath);
        Directory.CreateDirectory(InitializationPath(fixture));
        using var reopened = fixture.Open();

        Assert.Equal(missingFile == "head.journal" ? SharedProfileRepositoryStatus.Unavailable : SharedProfileRepositoryStatus.FolderNotEmpty,
            (await reopened.InitializeAsync(Cancellation)).Status);
        Assert.False(File.Exists(missingPath));
        Assert.Single(Directory.GetFiles(fixture.ProfilePath, "*.payload", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task Initialize_StagingWithUnrelatedContentsIsPreserved()
    {
        using var fixture = new RepositoryFixture();
        string staging = InitializationPath(fixture);
        Directory.CreateDirectory(staging);
        string unrelated = Path.Combine(staging, "notes.txt");
        File.WriteAllText(unrelated, "Keep this file");
        using var repository = fixture.Open();

        Assert.Equal(SharedProfileRepositoryStatus.FolderNotEmpty, (await repository.InitializeAsync(Cancellation)).Status);
        Assert.Equal("Keep this file", File.ReadAllText(unrelated));
        Assert.False(File.Exists(Path.Combine(fixture.Root, "repository.json")));
    }

    [Fact]
    public async Task Initialize_FirstManifestWriteFailureAndCancellationLeaveRetryableStaging()
    {
        using var fixture = new RepositoryFixture();
        string staging = InitializationPath(fixture);
        Directory.CreateDirectory(staging);
        string stagedManifest = Path.Combine(staging, "repository.json");
        File.WriteAllBytes(stagedManifest, [1, 2]);
        using var repository = fixture.Open();
        using (var held = new FileStream(stagedManifest, FileMode.Open, FileAccess.Read, FileShare.Read))
            Assert.Equal(SharedProfileRepositoryStatus.Unavailable, (await repository.InitializeAsync(Cancellation)).Status);
        Assert.False(Directory.Exists(fixture.ProfilePath));
        Assert.False(File.Exists(Path.Combine(fixture.Root, "repository.json")));

        using var canceled = new CancellationTokenSource();
        await canceled.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => repository.InitializeAsync(canceled.Token));
        Assert.Equal(new byte[] { 1, 2 }, File.ReadAllBytes(stagedManifest));
        using var reopened = fixture.Open();
        Assert.Equal(SharedProfileRepositoryStatus.Success, (await reopened.InitializeAsync(Cancellation)).Status);
        Assert.False(Directory.Exists(staging));
    }

    [Fact]
    public async Task Initialize_PublicationResponseLossAndFailedCleanupCannotReplaceCommittedRevision()
    {
        using var fixture = new RepositoryFixture();
        SharedProfileRepositoryResult published;
        using (var repository = fixture.Open())
        {
            await repository.InitializeAsync(Cancellation);
            published = await repository.PublishAsync([4], null, Guid.NewGuid(), Cancellation);
        }
        string staging = InitializationPath(fixture);
        Directory.CreateDirectory(staging);
        string stagedManifest = Path.Combine(staging, "repository.json");
        File.WriteAllBytes(stagedManifest, [1, 2]);
        using var reopened = fixture.Open();
        using (var held = new FileStream(stagedManifest, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            Assert.Equal(SharedProfileRepositoryStatus.Success, (await reopened.InitializeAsync(Cancellation)).Status);
            Assert.True(Directory.Exists(staging));
        }

        Assert.Equal(SharedProfileRepositoryStatus.Success, (await reopened.InitializeAsync(Cancellation)).Status);
        Assert.False(Directory.Exists(staging));
        SharedProfileRepositoryResult loaded = await reopened.LoadAsync(published.CommittedRevisionId, Cancellation);
        Assert.Equal(published.CommittedRevisionId, loaded.Snapshot!.Head.RevisionId);
        Assert.Equal(new byte[] { 4 }, loaded.Snapshot.EncryptedPayload);
    }

    [Fact]
    public async Task Initialize_AnotherEnrollmentsStagingIsNotClaimedOrModified()
    {
        using var fixture = new RepositoryFixture();
        string staging = InitializationPath(fixture);
        Directory.CreateDirectory(staging);
        string stagedManifest = Path.Combine(staging, "repository.json");
        File.WriteAllBytes(stagedManifest, [1, 2]);
        using var wrongKey = fixture.Open(key: RandomNumberGenerator.GetBytes(32));

        Assert.Equal(SharedProfileRepositoryStatus.FolderNotEmpty, (await wrongKey.InitializeAsync(Cancellation)).Status);
        Assert.Equal(new byte[] { 1, 2 }, File.ReadAllBytes(stagedManifest));
        Assert.False(File.Exists(Path.Combine(fixture.Root, "repository.json")));
    }

    private static string InitializationPath(RepositoryFixture fixture)
    {
        byte[] identity = Encoding.UTF8.GetBytes($"Foundry.SharedProfiles.v1/initialize/{fixture.RepositoryId:N}/{fixture.ProfileId:N}/1");
        return Path.Combine(fixture.Root, ".initializing-" + Convert.ToHexString(HMACSHA256.HashData(fixture.Key, identity)));
    }
}
