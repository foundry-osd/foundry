// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.ComponentModel;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Foundry.Core.Models.Profiles;
using Foundry.Core.Services.Profiles;
using Foundry.Utilities.Security;

namespace Foundry.Core.Tests.Profiles;

public sealed class LocalDeploymentProfileRepositoryTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "Foundry.LocalProfiles.Tests", Guid.NewGuid().ToString("N"));
    private readonly FakeCredentials credentials = new();
    private readonly Guid localId = Guid.NewGuid();

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
    public void Forget_CommitsSettingsOnlyDisablesEnrollmentAndRetiresAllOldKeys()
    {
        var repository = CreateRepository();
        DeploymentProfileDocument profile = CreateProfile();
        LocalProfileEnrollment enrollment = CreateEnrollment(profile.ProfileId);
        LocalProfileDescriptor original = repository.Save(localId, profile, true, null, enrollment, new byte[32]);
        string[] priorTargets = credentials.Values.Keys.ToArray();

        LocalProfileDescriptor forgotten = repository.Forget(localId, original.Revision);

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

        LocalProfileDescriptor forgotten = repository.Forget(localId, original.Revision);

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
        public WindowsCredential? Read(string target)
        {
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
