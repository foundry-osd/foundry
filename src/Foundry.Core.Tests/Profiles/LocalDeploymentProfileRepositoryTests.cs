// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.ComponentModel;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Foundry.Core.Models.Configuration;
using Foundry.Core.Models.Profiles;
using Foundry.Core.Services.Packages;
using Foundry.Core.Services.Profiles;
using Foundry.Utilities.Security;

namespace Foundry.Core.Tests.Profiles;

public sealed class LocalDeploymentProfileRepositoryTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "Foundry.LocalProfiles.Tests", Guid.NewGuid().ToString("N"));
    private readonly FakeCredentials credentials = new();
    private readonly Guid localId = Guid.NewGuid();

    [Fact]
    public async Task ListAsync_WhenContentionEnds_ReturnsCommittedProfiles()
    {
        var repository = CreateRepository();
        repository.Save(localId, CreateProfile(), true, null);
        using var competingLease = OpenRepositoryLock();

        Task<IReadOnlyList<LocalProfileDescriptor>> pending = repository.ListAsync(CancellationToken.None);
        Assert.False(pending.IsCompleted);
        competingLease.Dispose();

        LocalProfileDescriptor listed = Assert.Single(await pending);
        Assert.Equal(localId, listed.LocalId);
        Assert.Equal("Synthetic profile", listed.DisplayName);
        using var availableLease = OpenRepositoryLock();
    }

    [Fact]
    public async Task ReadAsync_WhenContentionEnds_ReturnsDecryptedRevision()
    {
        var repository = CreateRepository();
        repository.Save(localId, CreateProfile(), true, null);
        using var competingLease = OpenRepositoryLock();

        Task<LocalProfileSnapshot> pending = repository.ReadAsync(localId, CancellationToken.None);
        Assert.False(pending.IsCompleted);
        competingLease.Dispose();

        using LocalProfileSnapshot snapshot = await pending;
        Assert.Equal(localId, snapshot.Descriptor.LocalId);
        Assert.Equal("synthetic-secret-keep-exact", Encoding.UTF8.GetString(Assert.Single(snapshot.Profile.Secrets.Entries).Value!));
        using var availableLease = OpenRepositoryLock();
    }

    [Fact]
    public async Task GetActiveAsync_WhenContentionEnds_RestoresSelectedProfile()
    {
        var repository = CreateRepository();
        repository.Save(localId, CreateProfile(), true, null);
        repository.SetActive(localId);
        using var competingLease = OpenRepositoryLock();

        Task<Guid?> pending = repository.GetActiveAsync(CancellationToken.None);
        Assert.False(pending.IsCompleted);
        competingLease.Dispose();

        Assert.Equal(localId, await pending);
        using var availableLease = OpenRepositoryLock();
    }

    [Fact]
    public async Task ListAsync_WhenContentionPersists_ThrowsSharingViolationAfterBudget()
    {
        var repository = CreateRepository();
        repository.Save(localId, CreateProfile(), true, null);
        using var competingLease = OpenRepositoryLock();
        var elapsed = Stopwatch.StartNew();

        IOException exception = await Assert.ThrowsAsync<IOException>(() => repository.ListAsync(CancellationToken.None));

        Assert.Equal(32, exception.HResult & 0xffff);
        Assert.InRange(elapsed.Elapsed, TimeSpan.FromSeconds(1.9), TimeSpan.FromSeconds(5));
        Assert.Throws<IOException>(() => OpenRepositoryLock());
        competingLease.Dispose();
        Assert.Single(await repository.ListAsync(CancellationToken.None));
    }

    [Fact]
    public async Task ListAsync_WhenCanceledDuringContention_LeavesCompetingLeaseIntact()
    {
        var repository = CreateRepository();
        repository.Save(localId, CreateProfile(), true, null);
        using var competingLease = OpenRepositoryLock();
        using var cancellation = new CancellationTokenSource();

        Task<IReadOnlyList<LocalProfileDescriptor>> pending = repository.ListAsync(cancellation.Token);
        Assert.False(pending.IsCompleted);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);

        Assert.Throws<IOException>(() => OpenRepositoryLock());
        competingLease.Dispose();
        using var availableLease = OpenRepositoryLock();
    }

    [Fact]
    public async Task ListAsync_WhenAlreadyCanceled_DoesNotCreateRepository()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => CreateRepository().ListAsync(cancellation.Token));

        Assert.False(Directory.Exists(root));
    }

    [Fact]
    public async Task ReadAsync_WhenCanceledAfterAcquiringLease_CompletesAndReleasesLease()
    {
        var repository = CreateRepository();
        repository.Save(localId, CreateProfile(), true, null);
        using var cancellation = new CancellationTokenSource();
        credentials.BeforeRead = () =>
        {
            cancellation.Cancel();
            Assert.Throws<IOException>(() => repository.List());
        };

        using LocalProfileSnapshot snapshot = await repository.ReadAsync(localId, cancellation.Token);

        Assert.Equal("Synthetic profile", snapshot.Profile.DisplayName);
        using var availableLease = OpenRepositoryLock();
    }

    [Fact]
    public async Task ReadAsync_WhenBodyHasSharingViolation_DoesNotRetryAndReleasesLease()
    {
        var repository = CreateRepository();
        repository.Save(localId, CreateProfile(), true, null);
        var failure = new IOException("Credential read failed", unchecked((int)0x80070020));
        int reads = 0;
        credentials.BeforeRead = () => { reads++; throw failure; };

        IOException exception = await Assert.ThrowsAsync<IOException>(() => repository.ReadAsync(localId, CancellationToken.None));

        Assert.Same(failure, exception);
        Assert.Equal(1, reads);
        using var availableLease = OpenRepositoryLock();
    }

    [Fact]
    public async Task ListAsync_WhenRootIsAFile_FailsWithoutWaitingForContentionBudget()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(root)!);
        File.WriteAllText(root, "not a directory");
        try
        {
            Task<IReadOnlyList<LocalProfileDescriptor>> pending = CreateRepository().ListAsync(CancellationToken.None);
            Assert.True(pending.IsCompleted);
            await Assert.ThrowsAsync<IOException>(() => pending);
        }
        finally { File.Delete(root); }
    }

    private FileStream OpenRepositoryLock() => new(Path.Combine(root, ".repository.lock"), FileMode.Open, FileAccess.ReadWrite, FileShare.None);

    [Fact]
    public void SaveRead_EncryptsOwnedSecretsAndKeepsLocalAndSharedIdentitiesSeparate()
    {
        var repository = CreateRepository();
        DeploymentProfileDocument profile = CreateProfile();

        LocalProfileDescriptor descriptor = repository.Save(localId, profile, true, null);
        using LocalProfileSnapshot read = CreateRepository().Read(localId);

        Assert.Equal(localId, descriptor.LocalId);
        Assert.Equal(profile.ProfileId, descriptor.ProfileId);
        Assert.NotEqual(descriptor.LocalId, descriptor.ProfileId);
        Assert.Equal("synthetic-secret-keep-exact", Encoding.UTF8.GetString(Assert.Single(read.Profile.Secrets.Entries).Value!));
        Assert.Equal(descriptor.Revision, read.Descriptor.Revision);
        foreach (string file in Directory.GetFiles(root, "*", SearchOption.AllDirectories))
        {
            Assert.DoesNotContain("synthetic-secret-keep-exact", Encoding.UTF8.GetString(File.ReadAllBytes(file)));
        }
        Assert.Single(credentials.Values);
    }

    [Fact]
    public void Save_WithoutRemembering_OmitsSecretAndAssetBytesButPreservesLocalSourcePathsAndBlankState()
    {
        var repository = CreateRepository();
        DeploymentProfileDocument profile = CreateProfile();
        byte[] content = [1, 2, 3];
        profile = profile with
        {
            Configuration = profile.Configuration with { Network = new() { Wifi = new() { CertificatePath = "C:/synthetic/certificate.pfx" } } },
            Secrets = new() { Entries = [.. profile.Secrets.Entries, new() { Purpose = ProfileSecretPurpose.AdministratorPassword, Identity = "administrator", State = ProfileValueState.Blank }] },
            Assets = [new() { Id = "certificate", Kind = ProfileAssetKind.WifiCertificate, RelativePath = "wifi/certificate.pfx", State = ProfileValueState.Present, Content = content, Sha256 = Convert.ToHexString(SHA256.HashData(content)) }]
        };

        repository.Save(localId, profile, false, null);
        using LocalProfileSnapshot read = repository.Read(localId);

        Assert.Equal(ProfileValueState.Omitted, read.Profile.Secrets.Entries[0].State);
        Assert.Null(read.Profile.Secrets.Entries[0].Value);
        Assert.Equal(ProfileValueState.Blank, read.Profile.Secrets.Entries[1].State);
        Assert.Equal(ProfileValueState.Omitted, Assert.Single(read.Profile.Assets).State);
        Assert.Null(read.Profile.Assets[0].Content);
        Assert.Equal("C:/synthetic/certificate.pfx", read.Profile.Configuration.Network.Wifi.CertificatePath);
        Assert.NotNull(profile.Secrets.Entries[0].Value);
        Assert.Equal(content, profile.Assets[0].Content);
    }

    [Fact]
    public void Save_WithStaleExpectedRevision_PreservesConcurrentWriter()
    {
        var first = CreateRepository();
        var second = CreateRepository();
        DeploymentProfileDocument profile = CreateProfile();
        LocalProfileDescriptor original = first.Save(localId, profile, true, null);
        LocalProfileDescriptor updated = second.Save(localId, profile with { DisplayName = "Newer" }, true, original.Revision);

        LocalProfileConflictException conflict = Assert.Throws<LocalProfileConflictException>(() =>
            first.Save(localId, profile with { DisplayName = "Stale" }, true, original.Revision));

        Assert.Equal(updated.Revision, conflict.CurrentRevision);
        using LocalProfileSnapshot read = first.Read(localId);
        Assert.Equal("Newer", read.Profile.DisplayName);
        Assert.Single(credentials.Values);
    }

    [Fact]
    public void MissingKey_LocksReadAndSaveWithoutReplacingCommittedProfile()
    {
        var repository = CreateRepository();
        DeploymentProfileDocument profile = CreateProfile();
        LocalProfileDescriptor descriptor = repository.Save(localId, profile, true, null);
        credentials.Clear();

        Assert.Throws<LocalProfileLockedException>(() => repository.Read(localId));
        Assert.Throws<LocalProfileLockedException>(() => repository.Save(localId, profile, true, descriptor.Revision));

        Assert.Equal(descriptor.Revision, Assert.Single(repository.List()).Revision);
        Assert.Empty(credentials.Values);
    }

    [Fact]
    public void FailedCredentialWrite_PreservesPreviousRevisionAndCleansAmbiguousCandidate()
    {
        var repository = CreateRepository();
        DeploymentProfileDocument profile = CreateProfile();
        LocalProfileDescriptor descriptor = repository.Save(localId, profile, true, null);
        credentials.ThrowAfterNextWrite = true;

        Assert.Throws<Win32Exception>(() => repository.Save(localId, profile with { DisplayName = "Failed" }, true, descriptor.Revision));

        using LocalProfileSnapshot read = repository.Read(localId);
        Assert.Equal(descriptor.Revision, read.Descriptor.Revision);
        Assert.Equal("Synthetic profile", read.Profile.DisplayName);
        Assert.Single(credentials.Values);
    }

    [Fact]
    public void FailedHeadPublication_PreservesPreviousRevisionAndRetiresCandidateKey()
    {
        var repository = CreateRepository();
        DeploymentProfileDocument profile = CreateProfile();
        LocalProfileDescriptor descriptor = repository.Save(localId, profile, true, null);
        FileStream? lockedHead = null;
        credentials.AfterWrite = () => lockedHead = new FileStream(HeadPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        try
        {
            Exception? failure = Record.Exception(() => repository.Save(localId, profile with { DisplayName = "Failed" }, true, descriptor.Revision));
            Assert.True(failure is IOException or UnauthorizedAccessException);
        }
        finally
        {
            lockedHead?.Dispose();
            credentials.AfterWrite = null;
        }

        using LocalProfileSnapshot read = repository.Read(localId);
        Assert.Equal(descriptor.Revision, read.Descriptor.Revision);
        Assert.Single(credentials.Values);
    }

    [Fact]
    public void Save_DisablingRetentionRetiresPriorLocalAndSharedKeys()
    {
        var repository = CreateRepository();
        DeploymentProfileDocument profile = CreateProfile();
        LocalProfileEnrollment enrollment = CreateEnrollment(profile.ProfileId);
        LocalProfileDescriptor original = repository.Save(localId, profile, true, null, enrollment, new byte[32]);
        string[] priorTargets = credentials.Values.Keys.ToArray();

        LocalProfileDescriptor forgotten = repository.Save(localId, profile, false, original.Revision,
            enrollment with { IsEnabled = false, RememberSharedKey = false });

        Assert.False(forgotten.RememberSecrets);
        Assert.False(forgotten.Enrollment!.IsEnabled);
        Assert.False(forgotten.Enrollment.RememberSharedKey);
        Assert.Null(repository.ReadSharedKey(localId));
        Assert.All(priorTargets, target => Assert.False(credentials.Values.ContainsKey(target)));
        Assert.Single(credentials.Values);
        using LocalProfileSnapshot read = CreateRepository().Read(localId);
        Assert.Null(Assert.Single(read.Profile.Secrets.Entries).Value);
    }

    [Fact]
    public void InterruptedCleanup_ReportsPendingAndRecoversWithoutLosingCommittedRevision()
    {
        var repository = CreateRepository();
        DeploymentProfileDocument profile = CreateProfile();
        LocalProfileDescriptor original = repository.Save(localId, profile, true, null);
        credentials.FailDelete = true;

        LocalProfileDescriptor forgotten = repository.Save(localId, profile, false, original.Revision);

        Assert.True(forgotten.CleanupPending);
        Assert.NotEqual(original.Revision, forgotten.Revision);
        credentials.FailDelete = false;
        LocalProfileDescriptor recovered = Assert.Single(CreateRepository().List());
        Assert.Equal(forgotten.Revision, recovered.Revision);
        Assert.False(recovered.CleanupPending);
        Assert.Single(credentials.Values);
    }

    [Fact]
    public void EnrollmentKey_PreservesMatchingTupleAndRequiresNewKeyForChangedEpoch()
    {
        var repository = CreateRepository();
        DeploymentProfileDocument profile = CreateProfile();
        LocalProfileEnrollment enrollment = CreateEnrollment(profile.ProfileId);
        byte[] key = Enumerable.Repeat((byte)0x73, 32).ToArray();
        LocalProfileDescriptor first = repository.Save(localId, profile, false, null, enrollment, key);
        LocalProfileDescriptor second = repository.Save(localId, profile, false, first.Revision, enrollment);
        using WindowsCredential? read = repository.ReadSharedKey(localId);
        Assert.NotNull(read);
        Assert.Equal(key, read.Secret);

        Assert.Throws<ArgumentException>(() => repository.Save(localId, profile, false, second.Revision, enrollment with { KeyEpoch = 2 }));
        Assert.Equal(second.Revision, Assert.Single(repository.List()).Revision);
    }

    [Fact]
    public void Delete_UpdatesActiveSelectionAndLeavesOtherProfilesUntouched()
    {
        var repository = CreateRepository();
        LocalProfileDescriptor first = repository.Save(localId, CreateProfile(), true, null);
        Guid otherId = Guid.NewGuid();
        LocalProfileDescriptor other = repository.Save(otherId, CreateProfile(), false, null);
        repository.SetActive(localId);

        Assert.False(repository.Delete(localId, first.Revision));

        Assert.Null(repository.GetActive());
        Assert.Equal(otherId, Assert.Single(repository.List()).LocalId);
        using LocalProfileSnapshot read = repository.Read(otherId);
        Assert.Equal(other.Revision, read.Descriptor.Revision);
    }

    [Fact]
    public void FailedDeletion_PreservesActiveSelectionAndCommittedRevision()
    {
        var repository = CreateRepository();
        LocalProfileDescriptor original = repository.Save(localId, CreateProfile(), true, null);
        repository.SetActive(localId);
        using (var lockedHead = new FileStream(HeadPath, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            Assert.Throws<IOException>(() => repository.Delete(localId, original.Revision));
        }

        var reopened = CreateRepository();
        Assert.Equal(original.Revision, Assert.Single(reopened.List()).Revision);
        Assert.Equal(localId, reopened.GetActive());
        using LocalProfileSnapshot read = reopened.Read(localId);
        Assert.Equal(original.Revision, read.Descriptor.Revision);
        Assert.Single(credentials.Values);
    }

    [Fact]
    public void Delete_WhenActivePointerCleanupFails_CommitsDeletionAndRecoversWithoutChangingNewSelection()
    {
        var repository = CreateRepository();
        LocalProfileDescriptor original = repository.Save(localId, CreateProfile(), true, null);
        Guid otherId = Guid.NewGuid();
        repository.Save(otherId, CreateProfile(), false, null);
        repository.SetActive(localId);
        using (var lockedActive = new FileStream(Path.Combine(root, "active.json"), FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            Assert.True(repository.Delete(localId, original.Revision));
            Assert.Null(repository.GetActive());
        }

        repository.SetActive(otherId);
        Assert.Equal(otherId, Assert.Single(CreateRepository().List()).LocalId);
        Assert.Equal(otherId, repository.GetActive());
        Assert.Single(credentials.Values);
    }

    [Fact]
    public void FutureHeadVersion_IsNotOverwrittenOrRemoved()
    {
        var repository = CreateRepository();
        DeploymentProfileDocument profile = CreateProfile();
        LocalProfileDescriptor descriptor = repository.Save(localId, profile, true, null);
        JsonObject head = JsonNode.Parse(File.ReadAllText(HeadPath))!.AsObject();
        head["version"] = 999;
        File.WriteAllText(HeadPath, head.ToJsonString());
        byte[] before = File.ReadAllBytes(HeadPath);

        Assert.Throws<InvalidDataException>(() => repository.Save(localId, profile, true, descriptor.Revision));
        Assert.Throws<InvalidDataException>(() => repository.Delete(localId, descriptor.Revision));
        Assert.Equal(before, File.ReadAllBytes(HeadPath));
    }

    [Fact]
    public void ModifiedEnrollment_FailsAuthenticationBeforeItCanBeSaved()
    {
        var repository = CreateRepository();
        DeploymentProfileDocument profile = CreateProfile();
        LocalProfileEnrollment enrollment = CreateEnrollment(profile.ProfileId);
        LocalProfileDescriptor descriptor = repository.Save(localId, profile, true, null, enrollment, new byte[32]);
        JsonObject head = JsonNode.Parse(File.ReadAllText(HeadPath))!.AsObject();
        head["enrollment"]!["rootPath"] = "C:/other-share";
        File.WriteAllText(HeadPath, head.ToJsonString());

        Assert.ThrowsAny<CryptographicException>(() => repository.Read(localId));
        Assert.ThrowsAny<CryptographicException>(() => repository.Save(localId, profile, true, descriptor.Revision, enrollment));
        Assert.Equal(2, credentials.Values.Count);
    }

    [Fact]
    public void MissingSharedKey_IsLockedWhileNativeReadFailureRemainsDistinct()
    {
        var repository = CreateRepository();
        DeploymentProfileDocument profile = CreateProfile();
        repository.Save(localId, profile, true, null, CreateEnrollment(profile.ProfileId), new byte[32]);
        credentials.Delete(Assert.Single(credentials.Values.Keys, target => target.Contains("/Shared/", StringComparison.Ordinal)));

        using LocalProfileSnapshot readable = repository.Read(localId);
        Assert.Throws<LocalProfileLockedException>(() => repository.ReadSharedKey(localId));
        credentials.FailRead = true;
        Assert.Throws<Win32Exception>(() => repository.Read(localId));
    }

    [Fact]
    public void FailedFirstSave_RecoversOrphanKeyBeforeLaterCreation()
    {
        var repository = CreateRepository();
        DeploymentProfileDocument profile = CreateProfile();
        credentials.ThrowAfterNextWrite = true;
        credentials.FailDelete = true;

        Assert.Throws<Win32Exception>(() => repository.Save(localId, profile, true, null));
        Assert.Single(credentials.Values);
        Assert.Throws<IOException>(() => repository.Save(localId, profile, true, null));
        credentials.FailDelete = false;
        Assert.Empty(CreateRepository().List());
        Assert.Empty(credentials.Values);
        LocalProfileDescriptor created = repository.Save(localId, profile, true, null);
        using LocalProfileSnapshot read = repository.Read(localId);
        Assert.Equal(created.Revision, read.Descriptor.Revision);
    }

    [Fact]
    public void MismatchedRecoveryKeyReference_CannotRetireTheCommittedSharedKey()
    {
        var repository = CreateRepository();
        DeploymentProfileDocument profile = CreateProfile();
        LocalProfileEnrollment enrollment = CreateEnrollment(profile.ProfileId);
        LocalProfileDescriptor original = repository.Save(localId, profile, true, null, enrollment, new byte[32]);
        credentials.ThrowAfterNextWrite = true;
        credentials.FailDelete = true;
        Assert.Throws<Win32Exception>(() => repository.Save(localId, profile, true, original.Revision, enrollment));
        string journalPath = Path.Combine(Path.GetDirectoryName(HeadPath)!, "journal.json");
        JsonObject journal = JsonNode.Parse(File.ReadAllText(journalPath))!.AsObject();
        journal["previousSharedKeyRevision"] = Guid.NewGuid().ToString();
        File.WriteAllText(journalPath, journal.ToJsonString());
        credentials.FailDelete = false;
        int count = credentials.Values.Count;

        Assert.Throws<InvalidDataException>(() => repository.List());
        Assert.Equal(count, credentials.Values.Count);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SaveRead_ConnectionPublicationStateSurvivesRestartAndLegacyMetadataOmitsFalse(bool pending)
    {
        DeploymentProfileDocument profile = CreateProfile();
        LocalProfileEnrollment enrollment = CreateEnrollment(profile.ProfileId) with { PendingConnectionFile = pending };
        CreateRepository().Save(localId, profile, true, null, enrollment, new byte[32]);
        JsonObject metadata = JsonNode.Parse(File.ReadAllText(HeadPath))!.AsObject();
        Assert.Equal(pending, metadata["enrollment"]!.AsObject().ContainsKey("pendingConnectionFile"));

        using LocalProfileSnapshot restored = CreateRepository().Read(localId);

        Assert.Equal(pending, restored.Descriptor.Enrollment!.PendingConnectionFile);
        Assert.Equal("synthetic-secret-keep-exact", Encoding.UTF8.GetString(Assert.Single(restored.Profile.Secrets.Entries).Value!));
        Assert.Equal(enrollment.RepositoryId, restored.Descriptor.Enrollment.RepositoryId);
    }

    [Fact]
    public void Read_ConnectionPublicationStateCannotBeChangedOutsideAuthenticatedRevision()
    {
        DeploymentProfileDocument profile = CreateProfile();
        CreateRepository().Save(localId, profile, true, null, CreateEnrollment(profile.ProfileId), new byte[32]);
        JsonObject metadata = JsonNode.Parse(File.ReadAllText(HeadPath))!.AsObject();
        metadata["enrollment"]!["pendingConnectionFile"] = true;
        File.WriteAllText(HeadPath, metadata.ToJsonString());

        Assert.ThrowsAny<CryptographicException>(() => CreateRepository().Read(localId));
    }

    [Fact]
    public void PackageReferences_IncludeInactiveProfilesAndDisabledActions()
    {
        var repository = CreateRepository();
        string activeHash = new('a', 64);
        string inactiveHash = new('b', 64);
        repository.Save(localId, CreatePackageProfile(activeHash, true), false, null);
        repository.SetActive(localId);
        repository.Save(Guid.NewGuid(), CreatePackageProfile(inactiveHash, false), false, null);

        using LocalProfilePackageReferenceLease references = repository.AcquirePostInstallationPackageReferences();

        Assert.Equal(2, references.ContentHashes.Count);
        Assert.Contains(activeHash, references.ContentHashes);
        Assert.True(references.ContentHashes.Contains(inactiveHash.ToUpperInvariant()));
    }

    [Fact]
    public void PackageReferences_HoldRepositoryLockUntilDisposed()
    {
        var repository = CreateRepository();
        DeploymentProfileDocument profile = CreatePackageProfile(new string('a', 64), true);
        LocalProfileDescriptor descriptor = repository.Save(localId, profile, false, null);

        using (LocalProfilePackageReferenceLease references = repository.AcquirePostInstallationPackageReferences())
        {
            Assert.Throws<IOException>(() => CreateRepository().Save(localId, profile, false, descriptor.Revision));
            Assert.Throws<IOException>(() => CreateRepository().SetActive(localId));
        }

        LocalProfileDescriptor saved = CreateRepository().Save(localId, profile, false, descriptor.Revision);
        Assert.NotEqual(descriptor.Revision, saved.Revision);
    }

    [Fact]
    public async Task PackageReferences_ProtectSharedCacheUntilLastSavedActionIsRemoved()
    {
        CancellationToken cancellation = TestContext.Current.CancellationToken;
        Directory.CreateDirectory(root);
        string source = Path.Combine(root, "installer.exe");
        await File.WriteAllTextAsync(source, "synthetic installer", cancellation);
        string libraryRoot = Path.Combine(root, "library");
        var library = new PreOobePackageLibraryService(libraryRoot);
        PreOobePackageReference package = await library.ImportAsync(source, cancellation);
        string cachedDirectory = Path.Combine(libraryRoot, "content", package.ContentHash);
        string cachedFile = Path.Combine(cachedDirectory, "files", "installer.exe");
        var repository = CreateRepository();
        DeploymentProfileDocument active = CreatePackageProfile(package.ContentHash, true);
        active = active with
        {
            Configuration = active.Configuration with
            {
                PreOobe = active.Configuration.PreOobe with
                {
                    Actions = [active.Configuration.PreOobe.Actions[0] with { Package = package }]
                }
            }
        };
        DeploymentProfileDocument inactive = active with
        {
            ProfileId = Guid.NewGuid(),
            Configuration = active.Configuration with
            {
                PreOobe = active.Configuration.PreOobe with
                {
                    IsEnabled = false,
                    Actions = [active.Configuration.PreOobe.Actions[0] with { IsEnabled = false }]
                }
            }
        };
        LocalProfileDescriptor first = repository.Save(localId, active, false, null);
        repository.SetActive(localId);
        Guid inactiveId = Guid.NewGuid();
        LocalProfileDescriptor other = repository.Save(inactiveId, inactive, false, null);
        repository.Save(localId, active with { Configuration = active.Configuration with { PreOobe = new() } }, false, first.Revision);

        using (LocalProfilePackageReferenceLease references = repository.AcquirePostInstallationPackageReferences())
            await Assert.ThrowsAsync<InvalidOperationException>(() => library.DeleteAsync(package.ContentHash, references.ContentHashes, cancellation));

        Assert.True(library.IsAvailable(package));
        Assert.Equal("synthetic installer", await File.ReadAllTextAsync(cachedFile, cancellation));
        repository.Save(inactiveId, inactive with { Configuration = inactive.Configuration with { PreOobe = new() } }, false, other.Revision);

        using (LocalProfilePackageReferenceLease references = repository.AcquirePostInstallationPackageReferences())
            await library.DeleteAsync(package.ContentHash, references.ContentHashes, cancellation);

        Assert.False(Directory.Exists(cachedDirectory));
        Assert.False(library.IsAvailable(package));
        Assert.Equal("synthetic installer", await File.ReadAllTextAsync(source, cancellation));
    }

    [Theory]
    [InlineData("missing-key")]
    [InlineData("corrupt-head")]
    [InlineData("locked-revision")]
    public void PackageReferences_UnreadableProfileFailsClosedAndReleasesLock(string failure)
    {
        var repository = CreateRepository();
        LocalProfileDescriptor descriptor = repository.Save(localId, CreatePackageProfile(new string('a', 64), false), false, null);
        FileStream? lockedRevision = null;
        try
        {
            if (failure == "missing-key") credentials.Clear();
            else if (failure == "corrupt-head") File.WriteAllText(HeadPath, "invalid");
            else lockedRevision = new FileStream(Path.Combine(Path.GetDirectoryName(HeadPath)!, "revisions", descriptor.Revision.ToString("N") + ".profile"),
                FileMode.Open, FileAccess.Read, FileShare.None);

            Exception? exception = Record.Exception(() => repository.AcquirePostInstallationPackageReferences());
            Assert.True(exception is IOException or InvalidDataException);
            using var availableLock = new FileStream(Path.Combine(root, ".repository.lock"), FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        }
        finally { lockedRevision?.Dispose(); }
    }

    [Fact]
    public void PackageReferences_PendingRecoveryFailsClosedUntilCleanupSucceeds()
    {
        var repository = CreateRepository();
        DeploymentProfileDocument profile = CreatePackageProfile(new string('a', 64), true);
        LocalProfileDescriptor descriptor = repository.Save(localId, profile, false, null);
        credentials.FailDelete = true;
        Assert.True(repository.Save(localId, profile, false, descriptor.Revision).CleanupPending);

        Assert.Throws<IOException>(() => repository.AcquirePostInstallationPackageReferences());

        credentials.FailDelete = false;
        using LocalProfilePackageReferenceLease references = repository.AcquirePostInstallationPackageReferences();
        Assert.Single(references.ContentHashes);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PackageReferences_UnknownProfileEntryFailsClosedAndReleasesLock(bool isFile)
    {
        var repository = CreateRepository();
        string directory = Path.Combine(root, "profiles");
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "unrecognized");
        if (isFile) File.WriteAllText(path, "unknown");
        else Directory.CreateDirectory(path);

        Assert.Throws<InvalidDataException>(() => repository.AcquirePostInstallationPackageReferences());

        using var availableLock = new FileStream(Path.Combine(root, ".repository.lock"), FileMode.Open, FileAccess.ReadWrite, FileShare.None);
    }

    [Fact]
    public void PackageReferences_MissingHeadWithRemainingRevisionFailsClosed()
    {
        var repository = CreateRepository();
        repository.Save(localId, CreatePackageProfile(new string('a', 64), true), false, null);
        File.Delete(HeadPath);

        Assert.Throws<InvalidDataException>(() => repository.AcquirePostInstallationPackageReferences());

        using var availableLock = new FileStream(Path.Combine(root, ".repository.lock"), FileMode.Open, FileAccess.ReadWrite, FileShare.None);
    }

    [Fact]
    public void PackageReferences_DeletedProfilesDoNotRetainReferences()
    {
        var repository = CreateRepository();
        LocalProfileDescriptor descriptor = repository.Save(localId, CreatePackageProfile(new string('a', 64), true), false, null);
        Assert.False(repository.Delete(localId, descriptor.Revision));

        using LocalProfilePackageReferenceLease references = repository.AcquirePostInstallationPackageReferences();

        Assert.Empty(references.ContentHashes);
    }

    private static DeploymentProfileDocument CreatePackageProfile(string hash, bool enabled) => new()
    {
        ProfileId = Guid.NewGuid(),
        DisplayName = "Package profile",
        Configuration = new()
        {
            PreOobe = new()
            {
                IsEnabled = enabled,
                Actions = [PreOobeActionSettings.Create(PreOobeActionKind.Command, "Run command") with
                {
                    IsEnabled = enabled,
                    Command = "echo test",
                    Package = new() { ContentHash = hash, DisplayName = "Package", FileCount = 1, Length = 1 }
                }]
            }
        }
    };

    private string HeadPath => Path.Combine(root, "profiles", localId.ToString("N"), "head.json");
    private LocalDeploymentProfileRepository CreateRepository() => new(root, credentials, new DeploymentProfilePackageService());
    private static DeploymentProfileDocument CreateProfile() => new()
    {
        ProfileId = Guid.NewGuid(),
        DisplayName = "Synthetic profile",
        Secrets = new() { Entries = [new() { Purpose = ProfileSecretPurpose.WifiPassphrase, Identity = "wifi", State = ProfileValueState.Present, Value = Encoding.UTF8.GetBytes("synthetic-secret-keep-exact") }] }
    };
    private static LocalProfileEnrollment CreateEnrollment(Guid profileId) => new()
    {
        RootPath = "C:/synthetic-share",
        RepositoryId = Guid.NewGuid(),
        ProfileId = profileId,
        KeyEpoch = 1,
        IsEnabled = true,
        RememberSharedKey = true
    };

    public void Dispose()
    {
        credentials.Clear();
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private sealed class FakeCredentials : IWindowsCredentialStore
    {
        public Dictionary<string, byte[]> Values { get; } = new(StringComparer.OrdinalIgnoreCase);
        public bool ThrowAfterNextWrite { get; set; }
        public bool FailDelete { get; set; }
        public bool FailRead { get; set; }
        public Action? AfterWrite { get; set; }
        public Action? BeforeRead { get; set; }
        public WindowsCredential? Read(string target)
        {
            BeforeRead?.Invoke();
            if (FailRead)
            {
                throw new Win32Exception(5);
            }
            return Values.TryGetValue(target, out byte[]? value) ? new(value.ToArray()) : null;
        }
        public void Write(string target, ReadOnlySpan<byte> secret, string? userName = null)
        {
            Values[target] = secret.ToArray();
            AfterWrite?.Invoke();
            if (ThrowAfterNextWrite)
            {
                ThrowAfterNextWrite = false;
                throw new Win32Exception(5);
            }
        }
        public void Delete(string target)
        {
            if (FailDelete)
            {
                throw new Win32Exception(5);
            }
            if (Values.Remove(target, out byte[]? value))
            {
                CryptographicOperations.ZeroMemory(value);
            }
        }
        public void Clear()
        {
            foreach (byte[] value in Values.Values)
            {
                CryptographicOperations.ZeroMemory(value);
            }
            Values.Clear();
        }
    }
}
