// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Security.Cryptography;
using System.Text.Json;
using Foundry.Core.Models.Configuration;
using Foundry.Core.Services.Secrets;
using Foundry.Core.Services.Sharing;
using Foundry.Core.Services.WinPe;
using Foundry.Core.Tests.TestUtilities;

namespace Foundry.Core.Tests.Sharing;

public sealed class ConfigurationSharingEngineFollowerTests : IDisposable
{
    private const string Password = "publication-password";
    private const string ReferenceName = "REFERENCE-PC";

    private readonly TemporaryDirectory directory = new();
    private readonly FakeSharingHost host = new();
    private readonly InMemoryContentSource referenceContent = new();
    private readonly PublicationSigningKey referenceKey = PublicationSigningKey.Create();
    private readonly byte[] masterKey = RandomNumberGenerator.GetBytes(32);
    private readonly Guid publisherId = Guid.NewGuid();
    private readonly SharingLibraries followerLibraries;
    private long lastSequence;

    public ConfigurationSharingEngineFollowerTests()
    {
        followerLibraries = new SharingLibraries(Path.Combine(directory.Path, "follower"));
        Directory.CreateDirectory(Folder);
    }

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private string Folder => Path.Combine(directory.Path, "publication");

    private string StatePath => Path.Combine(directory.Path, "Configuration", "sharing.json");

    [Fact]
    public async Task Follow_AppliesConfigurationKeepsPerPcValuesPinsKey()
    {
        host.CurrentConfiguration = new FoundryConfigurationDocument
        {
            General = new GeneralSettings { IsoOutputPath = @"D:\Media\local.iso", CustomDriverDirectoryPath = @"D:\WinPE\Drivers" },
            CustomDrivers = new CustomDriverSetsSettings { IsEnabled = false, Sets = [new CustomDriverSetReference { Id = "local", DisplayName = "Local" }], DefaultSetId = "local" }
        };
        // Excluded, so the set needs no content: the section itself is what the follower takes from the publication.
        CustomDriverSetsSettings publishedDrivers = new()
        {
            IsEnabled = true,
            Sets = [new CustomDriverSetReference { Id = "network", DisplayName = "Network drivers", IsIncluded = false }],
            DefaultSetId = "network"
        };
        await PublishAsync(WithArchitecture(WinPeArchitecture.Arm64) with { CustomDrivers = publishedDrivers });
        ConfigurationSharingEngine engine = CreateEngine();

        PublicationReadResult preview = await engine.PreviewFollowAsync(Folder, Password, Cancellation);
        SharingStatus status = await engine.FollowAsync(Folder, Password, preview, Cancellation);

        Assert.Equal(SharingStatus.Updated, status);
        Assert.Equal(SharingRole.Follower, engine.Role);
        GeneralSettings general = host.CurrentConfiguration.General;
        Assert.Equal(WinPeArchitecture.Arm64, general.Architecture);
        Assert.Equal(@"D:\Media\local.iso", general.IsoOutputPath);
        Assert.Equal(@"D:\WinPE\Drivers", general.CustomDriverDirectoryPath);
        CustomDriverSetsSettings drivers = host.CurrentConfiguration.CustomDrivers;
        Assert.True(drivers.IsEnabled);
        Assert.Equal("network", drivers.DefaultSetId);
        Assert.Equal(["network"], drivers.Sets.Select(set => set.Id));
        Assert.Equal(ReferenceName, host.ManagedBy);
        Assert.Equal(Password, host.Vault[SecretIds.PublicationPassword]);
        SharingState persisted = CreateEngine().State;
        Assert.Equal(SharingRole.Follower, persisted.Role);
        Assert.Equal(Folder, persisted.Folder);
        Assert.Equal(referenceKey.PublicKey, persisted.PinnedPublicKey);
        Assert.Equal(1, persisted.AppliedSequence);
        Assert.Equal(referenceKey.KeyId, engine.PinnedKeyId);
    }

    [Fact]
    public async Task Check_NoChange_ReturnsUpToDate()
    {
        ConfigurationSharingEngine engine = await FollowAsync(WithArchitecture(WinPeArchitecture.X64));

        SharingStatus status = await engine.CheckAsync(Cancellation);

        Assert.Equal(SharingStatus.UpToDate, status);
        Assert.Single(host.Applied);
        Assert.Equal(FixedTimeProvider.Now, engine.LastSuccessUtc);
    }

    [Fact]
    public async Task Check_NewSequence_AppliesAndReturnsUpdated()
    {
        ConfigurationSharingEngine engine = await FollowAsync(WithArchitecture(WinPeArchitecture.X64));
        await PublishAsync(WithArchitecture(WinPeArchitecture.Arm64));

        SharingStatus status = await engine.CheckAsync(Cancellation);

        Assert.Equal(SharingStatus.Updated, status);
        Assert.Equal(2, host.Applied.Count);
        Assert.Equal(WinPeArchitecture.Arm64, host.CurrentConfiguration.General.Architecture);
        Assert.Equal(2, CreateEngine().State.AppliedSequence);
        Assert.Equal(SharingStatus.UpToDate, await engine.CheckAsync(Cancellation));
    }

    [Fact]
    public async Task Check_PublicationWithoutPasswords_KeepsLocalPasswords()
    {
        host.Vault[SecretIds.WifiPassphrase] = "local-passphrase";
        ConfigurationSharingEngine engine = await FollowAsync(WithArchitecture(WinPeArchitecture.X64));
        await PublishAsync(WithArchitecture(WinPeArchitecture.Arm64));

        Assert.Equal(SharingStatus.Updated, await engine.CheckAsync(Cancellation));

        Assert.Equal("local-passphrase", host.Vault[SecretIds.WifiPassphrase]);
        Assert.False(engine.State.IncludePasswords);
    }

    [Fact]
    public async Task Check_PublicationWithPasswords_OverwritesVaultEntries()
    {
        host.Vault[SecretIds.WifiPassphrase] = "local-passphrase";
        host.Vault[SecretIds.OobeAdministrator] = "local-administrator";
        ConfigurationSharingEngine engine = await FollowAsync(WithArchitecture(WinPeArchitecture.X64));
        host.Vault[SecretIds.SigningKey] = "local-signing-key";
        await PublishAsync(WithArchitecture(WinPeArchitecture.Arm64), new Dictionary<string, string>
        {
            [SecretIds.WifiPassphrase] = "published-passphrase",
            // A publication never overwrites the sharing ids of the follower.
            [SecretIds.PublicationPassword] = "published-publication-password",
            [SecretIds.SigningKey] = "published-signing-key"
        });

        Assert.Equal(SharingStatus.Updated, await engine.CheckAsync(Cancellation));

        Assert.Equal("published-passphrase", host.Vault[SecretIds.WifiPassphrase]);
        Assert.Equal("local-administrator", host.Vault[SecretIds.OobeAdministrator]);
        Assert.Equal(Password, host.Vault[SecretIds.PublicationPassword]);
        Assert.Equal("local-signing-key", host.Vault[SecretIds.SigningKey]);
        Assert.True(engine.State.IncludePasswords);
    }

    [Fact]
    public async Task Check_MissingPublicationPassword_ReturnsPasswordsNeeded()
    {
        await FollowAsync(WithArchitecture(WinPeArchitecture.X64));
        await PublishAsync(WithArchitecture(WinPeArchitecture.Arm64));
        // DPAPI reset, then a restart: the vault reads as empty and unreadable.
        host.Vault.Clear();
        host.IsVaultAvailable = false;
        ConfigurationSharingEngine engine = CreateEngine();

        SharingStatus status = await engine.CheckAsync(Cancellation);

        Assert.Equal(SharingStatus.PasswordsNeeded, status);
        Assert.Single(host.Applied);
        Assert.Equal(SharingRole.Follower, engine.Role);

        // The publication password is requested again: a wrong one is not kept, the right one applies the current version.
        Assert.Equal(SharingStatus.WrongPassword, await engine.SetPublicationPasswordAsync("not-the-publication-password", Cancellation));
        Assert.False(host.Vault.ContainsKey(SecretIds.PublicationPassword));
        Assert.Equal(SharingStatus.Updated, await engine.SetPublicationPasswordAsync(Password, Cancellation));
        Assert.Equal(Password, host.Vault[SecretIds.PublicationPassword]);
        Assert.Equal(WinPeArchitecture.Arm64, host.CurrentConfiguration.General.Architecture);
    }

    [Fact]
    public async Task Check_RequiredPasswordMissing_ReturnsPasswordsNeeded()
    {
        FoundryConfigurationDocument protectedMedia = new() { General = new GeneralSettings { DeploymentProtection = new DeploymentProtectionSettings { IsEnabled = true } } };

        ConfigurationSharingEngine engine = await FollowAsync(protectedMedia, SharingStatus.PasswordsNeeded);
        Assert.Equal(SharingStatus.PasswordsNeeded, await engine.CheckAsync(Cancellation));

        host.Vault[SecretIds.DeploymentProtection] = "media-password";
        Assert.Equal(SharingStatus.UpToDate, await engine.CheckAsync(Cancellation));
    }

    [Fact]
    public async Task Check_ForeignSigner_ReturnsUnknownPublisherWithoutApplying()
    {
        ConfigurationSharingEngine engine = await FollowAsync(WithArchitecture(WinPeArchitecture.X64));
        using PublicationSigningKey other = PublicationSigningKey.Create();
        await PublishAsync(WithArchitecture(WinPeArchitecture.Arm64), signer: other, takeOver: true);

        SharingStatus status = await engine.CheckAsync(Cancellation);

        Assert.Equal(SharingStatus.UnknownPublisher, status);
        Assert.Single(host.Applied);
        Assert.Equal(WinPeArchitecture.X64, host.CurrentConfiguration.General.Architecture);
        SharingPublisherCandidate candidate = Assert.IsType<SharingPublisherCandidate>(engine.Candidate);
        Assert.Equal(other.KeyId, candidate.KeyId);
        Assert.Equal(2, candidate.Sequence);
        Assert.Equal(referenceKey.PublicKey, engine.State.PinnedPublicKey);
        Assert.Equal(referenceKey.PublicKey, CreateEngine().State.PinnedPublicKey);
    }

    [Fact]
    public async Task AcceptNewPublisher_PinsNewKeyAndApplies()
    {
        ConfigurationSharingEngine engine = await FollowAsync(WithArchitecture(WinPeArchitecture.X64));
        using PublicationSigningKey other = PublicationSigningKey.Create();
        await PublishAsync(WithArchitecture(WinPeArchitecture.Arm64), signer: other, takeOver: true);
        Assert.Equal(SharingStatus.UnknownPublisher, await engine.CheckAsync(Cancellation));

        SharingStatus status = await engine.AcceptNewPublisherAsync(Cancellation);

        Assert.Equal(SharingStatus.Updated, status);
        Assert.Equal(WinPeArchitecture.Arm64, host.CurrentConfiguration.General.Architecture);
        Assert.Null(engine.Candidate);
        SharingState persisted = CreateEngine().State;
        Assert.Equal(other.PublicKey, persisted.PinnedPublicKey);
        Assert.Equal(2, persisted.AppliedSequence);
        Assert.Equal(SharingStatus.UpToDate, await engine.CheckAsync(Cancellation));
    }

    [Fact]
    public async Task Check_ContentPending_ReturnsContentDownloadingThenUpToDate()
    {
        ConfigurationSharingEngine engine = await FollowAsync(WithArchitecture(WinPeArchitecture.X64));
        AuthoringFileReference file = referenceContent.Add("<profile />", "profile.xml");
        await PublishAsync(WithFile(file));

        SharingStatus status = await engine.CheckAsync(Cancellation);

        Assert.Equal(SharingStatus.ContentDownloading, status);
        await engine.WaitForContentAsync(Cancellation);
        Assert.True(followerLibraries.Files.IsAvailable(file));
        Assert.Equal(SharingStatus.UpToDate, engine.Status);
        Assert.False(engine.IsContentFetching);
        Assert.Equal(SharingStatus.UpToDate, await engine.CheckAsync(Cancellation));
        Assert.Empty(engine.FailedContent);
        Assert.False(engine.IsContentStalled);
    }

    [Fact]
    public async Task Check_ContentNeverArrives_FlagsStalledAfterBoundedChecks()
    {
        AuthoringFileReference file = referenceContent.Add("<profile />", "profile.xml");
        await PublishAsync(WithFile(file));
        // Damaged for good, it reads exactly like content a reset is still encrypting again.
        File.Delete(Path.Combine(Folder, "content", file.ContentHash + ".bin"));
        ConfigurationSharingEngine engine = CreateEngine();
        Assert.Equal(SharingStatus.ContentDownloading, await engine.FollowAsync(Folder, Password, await engine.PreviewFollowAsync(Folder, Password, Cancellation),
            Cancellation));
        await engine.WaitForContentAsync(Cancellation);

        for (int fetch = 2; fetch <= ConfigurationSharingEngine.StalledContentFetchCount; fetch++)
        {
            Assert.False(engine.IsContentStalled);
            Assert.Equal(SharingStatus.ContentDownloading, await engine.CheckAsync(Cancellation));
            await engine.WaitForContentAsync(Cancellation);
        }

        Assert.True(engine.IsContentStalled);
        Assert.Equal(SharingStatus.ContentDownloading, engine.Status);

        await PublishAsync(WithFile(file));
        Assert.Equal(SharingStatus.ContentDownloading, await engine.CheckAsync(Cancellation));
        await engine.WaitForContentAsync(Cancellation);
        Assert.False(engine.IsContentStalled);
        Assert.Equal(SharingStatus.UpToDate, engine.Status);
    }

    [Fact]
    public async Task Check_TamperedManifest_RetriedOnceBeforeReported()
    {
        ConfigurationSharingEngine engine = await FollowAsync(WithArchitecture(WinPeArchitecture.X64));
        await PublishAsync(WithArchitecture(WinPeArchitecture.Arm64));
        // A cloud folder that delivered half of the manifest reads exactly like a modified one.
        string manifest = CurrentManifestPath();
        byte[] bytes = await File.ReadAllBytesAsync(manifest, Cancellation);
        await File.WriteAllBytesAsync(manifest, bytes[..(bytes.Length / 2)], Cancellation);

        Assert.Equal(SharingStatus.Checking, await engine.CheckAsync(Cancellation));
        Assert.Equal(SharingStatus.Tampered, await engine.CheckAsync(Cancellation));
        Assert.Single(host.Applied);

        await PublishAsync(WithArchitecture(WinPeArchitecture.Arm64));
        Assert.Equal(SharingStatus.Updated, await engine.CheckAsync(Cancellation));
    }

    [Fact]
    public async Task StopFollowing_ClearsManagedAndKeepsConfiguration()
    {
        ConfigurationSharingEngine engine = await FollowAsync(WithArchitecture(WinPeArchitecture.Arm64));

        await engine.StopFollowingAsync(forgetPassword: true);

        Assert.Equal(SharingRole.None, engine.Role);
        Assert.Equal(SharingStatus.Idle, engine.Status);
        Assert.Equal(1, host.ClearManagedCount);
        Assert.Null(host.ManagedBy);
        Assert.Equal(WinPeArchitecture.Arm64, host.CurrentConfiguration.General.Architecture);
        Assert.False(host.Vault.ContainsKey(SecretIds.PublicationPassword));
        SharingState persisted = CreateEngine().State;
        Assert.Equal(SharingRole.None, persisted.Role);
        Assert.Null(persisted.PinnedPublicKey);
        Assert.Equal(SharingStatus.Idle, await engine.CheckAsync(Cancellation));
    }

    [Fact]
    public async Task Check_CandidatePreviewTampered_RetriedOnceBeforeReported()
    {
        ConfigurationSharingEngine engine = await FollowAsync(WithArchitecture(WinPeArchitecture.X64));
        using PublicationSigningKey other = PublicationSigningKey.Create();
        using PublicationSigningKey claimed = PublicationSigningKey.Create();
        // Signed by another key, but the payload names a third one: the preview cannot authenticate it.
        byte[] payload = JsonSerializer.SerializeToUtf8Bytes(ForeignPayload(2, claimed), ConfigurationPackageJson.SerializerOptions);
        await PlaceManifestAsync(ConfigurationPackageCodec.Seal(payload, Password, other), 2);

        Assert.Equal(SharingStatus.Checking, await engine.CheckAsync(Cancellation));
        Assert.Equal(SharingStatus.Tampered, await engine.CheckAsync(Cancellation));
        Assert.Null(engine.Candidate);
        Assert.Single(host.Applied);
    }

    [Fact]
    public async Task Check_ForeignEnvelopeWrongPassword_CreatesNoCandidate()
    {
        ConfigurationSharingEngine engine = await FollowAsync(WithArchitecture(WinPeArchitecture.X64));
        using PublicationSigningKey other = PublicationSigningKey.Create();
        await PlaceManifestAsync(ConfigurationPackageCodec.Write(ForeignPayload(2, other), "another-publication-password", other), 2);

        SharingStatus status = await engine.CheckAsync(Cancellation);

        Assert.Equal(SharingStatus.WrongPassword, status);
        Assert.Null(engine.Candidate);
        Assert.Single(host.Applied);
        Assert.Equal(referenceKey.PublicKey, engine.State.PinnedPublicKey);
        await Assert.ThrowsAsync<InvalidOperationException>(() => engine.AcceptNewPublisherAsync(Cancellation));
    }

    [Fact]
    public async Task AcceptNewPublisher_LowerSequence_ReturnsRollbackRejected()
    {
        ConfigurationSharingEngine engine = await FollowAsync(WithArchitecture(WinPeArchitecture.X64));
        await PublishAsync(WithArchitecture(WinPeArchitecture.Arm64));
        Assert.Equal(SharingStatus.Updated, await engine.CheckAsync(Cancellation));
        using PublicationSigningKey other = PublicationSigningKey.Create();
        await PlaceManifestAsync(ConfigurationPackageCodec.Write(ForeignPayload(3, other), Password, other), 3);
        Assert.Equal(SharingStatus.UnknownPublisher, await engine.CheckAsync(Cancellation));
        // Before the user accepts, the folder points at an older manifest of the same new key.
        await PlaceManifestAsync(ConfigurationPackageCodec.Write(ForeignPayload(1, other), Password, other), 1);

        SharingStatus status = await engine.AcceptNewPublisherAsync(Cancellation);

        Assert.Equal(SharingStatus.RollbackRejected, status);
        Assert.Equal(referenceKey.PublicKey, engine.State.PinnedPublicKey);
        Assert.Equal(2, engine.State.AppliedSequence);
        Assert.Equal(2, host.Applied.Count);
    }

    [Fact]
    public async Task Check_ContentFailsImport_ReportsFailedWithoutActiveFetch()
    {
        ConfigurationSharingEngine engine = await FollowAsync(WithArchitecture(WinPeArchitecture.X64));
        AuthoringFileReference file = referenceContent.AddCorrupted("<profile />", "profile.xml");
        await PublishAsync(WithFile(file));

        Assert.Equal(SharingStatus.ContentDownloading, await engine.CheckAsync(Cancellation));
        await engine.WaitForContentAsync(Cancellation);

        Assert.Equal(file.ContentHash, Assert.Single(engine.FailedContent).Hash);
        Assert.False(engine.IsContentFetching);
        Assert.Equal(SharingStatus.ContentDownloading, engine.Status);

        // It fails the same way until the reference publishes again, so the next check does not download it again.
        Assert.Equal(SharingStatus.ContentDownloading, await engine.CheckAsync(Cancellation));
        Assert.False(engine.IsContentFetching);
        Assert.False(followerLibraries.Files.IsAvailable(file));
    }

    public void Dispose()
    {
        referenceKey.Dispose();
        directory.Dispose();
    }

    private ConfigurationPackagePayload ForeignPayload(long sequence, PublicationSigningKey publisher) => new()
    {
        Kind = PackageKind.Publication,
        Sequence = sequence,
        Publisher = new PackagePublisher(Guid.NewGuid(), "OTHER-PC", publisher.KeyId, publisher.PublicKey),
        Configuration = WithArchitecture(WinPeArchitecture.Arm64),
        MasterKey = masterKey
    };

    /// <summary>Writes a manifest as a folder writer would and points <c>current.json</c> at it.</summary>
    private async Task PlaceManifestAsync(byte[] envelope, long sequence)
    {
        string manifest = PublicationFolder.NewManifest(sequence);
        await File.WriteAllBytesAsync(PublicationFolder.ManifestPath(Folder, manifest), envelope, Cancellation);
        await PublicationFolder.WritePointerAsync(Folder, manifest, Cancellation);
    }

    private static FoundryConfigurationDocument WithArchitecture(WinPeArchitecture architecture) =>
        new() { General = new GeneralSettings { Architecture = architecture } };

    private static FoundryConfigurationDocument WithFile(AuthoringFileReference file) => new()
    {
        Network = new NetworkSettings { Dot1x = new Dot1xSettings { IsEnabled = true, ProfileTemplate = file } }
    };

    private ConfigurationSharingEngine CreateEngine() => new(host, StatePath, new InMemoryContentSource(), followerLibraries.Sync(), new FixedTimeProvider());

    private async Task<ConfigurationSharingEngine> FollowAsync(FoundryConfigurationDocument configuration, SharingStatus expected = SharingStatus.Updated)
    {
        await PublishAsync(configuration);
        ConfigurationSharingEngine engine = CreateEngine();
        PublicationReadResult preview = await engine.PreviewFollowAsync(Folder, Password, Cancellation);
        Assert.Equal(PublicationReadStatus.Ok, preview.Status);
        Assert.Equal(expected, await engine.FollowAsync(Folder, Password, preview, Cancellation));
        return engine;
    }

    /// <summary>Publishes the next version as the reference PC (or as <paramref name="signer"/>, taking the folder over).</summary>
    private async Task PublishAsync(FoundryConfigurationDocument configuration, IReadOnlyDictionary<string, string>? secrets = null,
        PublicationSigningKey? signer = null, bool takeOver = false)
    {
        PublicationRequest request = new(Folder, configuration, PortableConfiguration.ListContents(configuration), secrets, signer ?? referenceKey, masterKey,
            Password, publisherId, ReferenceName, lastSequence, takeOver);
        PublishResult result = await PublicationWriter.PublishAsync(request, referenceContent, null, Cancellation);
        Assert.Equal(PublishStatus.Published, result.Status);
        lastSequence = result.Sequence;
    }

    private string CurrentManifestPath() => Directory.EnumerateFiles(Path.Combine(Folder, "manifests"))
        .Single(path => PublicationFolder.TryParseManifestFileName(Path.GetFileName(path), out long sequence) && sequence == lastSequence);
}
