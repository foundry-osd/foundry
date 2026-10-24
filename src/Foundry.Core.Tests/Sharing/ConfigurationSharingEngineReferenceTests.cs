// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Security.Cryptography;
using Foundry.Core.Models.Configuration;
using Foundry.Core.Services.Secrets;
using Foundry.Core.Services.Sharing;
using Foundry.Core.Services.WinPe;
using Foundry.Core.Tests.TestUtilities;

namespace Foundry.Core.Tests.Sharing;

public sealed class ConfigurationSharingEngineReferenceTests : IDisposable
{
    private const string Password = "publication-password";
    private const string NewPassword = "another-publication-password";

    private readonly TemporaryDirectory directory = new();
    private readonly FakeSharingHost host = new();
    private readonly InMemoryContentSource source = new();

    public ConfigurationSharingEngineReferenceTests()
    {
        Directory.CreateDirectory(Folder);
    }

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private string Folder => Path.Combine(directory.Path, "publication");

    private string StatePath => Path.Combine(directory.Path, "Configuration", "sharing.json");

    [Fact]
    public async Task StartPublishing_ReadyConfiguration_PublishesSequenceOne()
    {
        host.CurrentConfiguration = new FoundryConfigurationDocument { General = new GeneralSettings { IsoOutputPath = @"D:\Media\foundry.iso" } };
        host.Vault[SecretIds.WifiPassphrase] = "wifi-passphrase";
        ConfigurationSharingEngine engine = CreateEngine();

        SharingStatus status = await engine.StartPublishingAsync(Folder, Password, includePasswords: true, Cancellation);

        Assert.Equal(SharingStatus.Published, status);
        Assert.Equal(SharingStatus.Published, engine.Status);
        Assert.Equal(SharingRole.Reference, engine.Role);
        Assert.Equal(FixedTimeProvider.Now, engine.LastSuccessUtc);
        PublicationReadResult read = await PublicationReader.PreviewAsync(Folder, Password, Cancellation);
        Assert.Equal(PublicationReadStatus.Ok, read.Status);
        Assert.Equal(1, read.Payload!.Sequence);
        Assert.Null(read.Payload.Configuration.General.IsoOutputPath);
        Assert.Equal("wifi-passphrase", read.Payload.Secrets![SecretIds.WifiPassphrase]);
        Assert.DoesNotContain(read.Payload.Secrets.Keys, SecretIds.IsSharingInternal);
        using (PublicationSigningKey signer = PublicationSigningKey.Import(Convert.FromBase64String(host.Vault[SecretIds.SigningKey])))
        {
            Assert.Equal(signer.KeyId, read.SignerKeyId);
        }

        Assert.Equal(Convert.FromBase64String(host.Vault[SecretIds.MasterKey]), read.Payload.MasterKey);
        Assert.Equal(Password, host.Vault[SecretIds.PublicationPassword]);
        SharingState persisted = CreateEngine().State;
        Assert.Equal(SharingRole.Reference, persisted.Role);
        Assert.Equal(Folder, persisted.Folder);
        Assert.True(persisted.IncludePasswords);
        Assert.Equal(1, persisted.LastPublishedSequence);
    }

    [Fact]
    public async Task OnChanged_NotReady_ReturnsNotPublishedIncompleteAndKeepsPrevious()
    {
        ConfigurationSharingEngine engine = await StartAsync();
        host.IsReadyToBuild = false;
        host.CurrentConfiguration = WithArchitecture(WinPeArchitecture.Arm64);

        SharingStatus status = await engine.OnConfigurationChangedAsync(Cancellation);

        Assert.Equal(SharingStatus.NotPublishedIncomplete, status);
        PublicationReadResult read = await PublicationReader.PreviewAsync(Folder, Password, Cancellation);
        Assert.Equal(1, read.Payload!.Sequence);
        Assert.Equal(WinPeArchitecture.X64, read.Payload.Configuration.General.Architecture);
        Assert.Equal(1, engine.State.LastPublishedSequence);
    }

    [Fact]
    public async Task OnChanged_ContentMissing_ReturnsNotPublishedIncomplete()
    {
        ConfigurationSharingEngine engine = await StartAsync();
        AuthoringFileReference file = source.Add("<profile />", "profile.xml");
        host.CurrentConfiguration = WithFile(file);
        host.ContentAvailable = _ => false;

        SharingStatus status = await engine.OnConfigurationChangedAsync(Cancellation);

        Assert.Equal(SharingStatus.NotPublishedIncomplete, status);
        Assert.Equal(0, source.OpenCount);
        Assert.Equal(1, (await PublicationReader.PreviewAsync(Folder, Password, Cancellation)).Payload!.Sequence);
    }

    [Fact]
    public async Task OnChanged_DuringPublish_QueuesOnePublishWithLatestConfiguration()
    {
        ConfigurationSharingEngine engine = await StartAsync();
        AuthoringFileReference file = source.Add("<large image />", "image.xml");
        host.CurrentConfiguration = WithFile(file);
        source.Gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        Task<SharingStatus> first = engine.OnConfigurationChangedAsync(Cancellation);
        await source.Opened.Task.WaitAsync(Cancellation);
        Assert.Equal(SharingStatus.Publishing, engine.Status);
        host.CurrentConfiguration = WithFile(file) with { General = new GeneralSettings { Architecture = WinPeArchitecture.Arm64 } };
        Task<SharingStatus> second = engine.OnConfigurationChangedAsync(Cancellation);
        Task<SharingStatus> third = engine.OnConfigurationChangedAsync(Cancellation);
        Assert.False(first.IsCompleted);
        source.Gate.SetResult();

        SharingStatus[] statuses = await Task.WhenAll(first, second, third);

        Assert.All(statuses, status => Assert.Equal(SharingStatus.Published, status));
        Assert.Equal(3, engine.State.LastPublishedSequence);
        PublicationReadResult current = await PublicationReader.PreviewAsync(Folder, Password, Cancellation);
        Assert.Equal(3, current.Payload!.Sequence);
        Assert.Equal(WinPeArchitecture.Arm64, current.Payload.Configuration.General.Architecture);
        // The publish that was running activated the configuration it started with, with its content.
        ConfigurationPackagePayload previous = ReadManifest(2);
        Assert.Equal(WinPeArchitecture.X64, previous.Configuration.General.Architecture);
        Assert.Equal(file.ContentHash, Assert.Single(previous.Contents).Hash);
        Assert.Equal(1, source.OpenCount);
    }

    [Fact]
    public async Task StartPublishing_ForeignFolder_ReturnsUnknownPublisher()
    {
        using PublicationSigningKey other = PublicationSigningKey.Create();
        await PublishForeignAsync(other, 2);
        ConfigurationSharingEngine engine = CreateEngine();

        SharingStatus status = await engine.StartPublishingAsync(Folder, Password, includePasswords: false, Cancellation);

        Assert.Equal(SharingStatus.UnknownPublisher, status);
        Assert.Equal(SharingRole.None, engine.Role);
        Assert.False(File.Exists(StatePath));
        PublicationReadResult read = await PublicationReader.PreviewAsync(Folder, Password, Cancellation);
        Assert.Equal(other.KeyId, read.SignerKeyId);
        Assert.Equal(2, read.Payload!.Sequence);
    }

    [Fact]
    public async Task TakeOver_WithPassword_ContinuesSequenceWithNewKey()
    {
        using PublicationSigningKey other = PublicationSigningKey.Create();
        await PublishForeignAsync(other, 2);
        ConfigurationSharingEngine engine = CreateEngine();
        Assert.Equal(SharingStatus.UnknownPublisher, await engine.StartPublishingAsync(Folder, Password, includePasswords: false, Cancellation));

        SharingStatus status = await engine.TakeOverAsync(Folder, Password, includePasswords: false, Cancellation);

        Assert.Equal(SharingStatus.Published, status);
        Assert.Equal(SharingRole.Reference, engine.Role);
        PublicationReadResult read = await PublicationReader.PreviewAsync(Folder, Password, Cancellation);
        Assert.Equal(3, read.Payload!.Sequence);
        Assert.NotEqual(other.KeyId, read.SignerKeyId);
        using PublicationSigningKey own = PublicationSigningKey.Import(Convert.FromBase64String(host.Vault[SecretIds.SigningKey]));
        Assert.Equal(own.KeyId, read.SignerKeyId);
        Assert.Equal(3, CreateEngine().State.LastPublishedSequence);
        Assert.Equal(SharingStatus.Published, await engine.OnConfigurationChangedAsync(Cancellation));
        Assert.Equal(4, engine.State.LastPublishedSequence);
    }

    [Fact]
    public async Task IncludePasswordsFalse_PublishesNoSecrets()
    {
        host.Vault[SecretIds.WifiPassphrase] = "wifi-passphrase";
        ConfigurationSharingEngine engine = CreateEngine();

        Assert.Equal(SharingStatus.Published, await engine.StartPublishingAsync(Folder, Password, includePasswords: false, Cancellation));
        Assert.Equal(SharingStatus.Published, await engine.OnConfigurationChangedAsync(Cancellation));

        PublicationReadResult read = await PublicationReader.PreviewAsync(Folder, Password, Cancellation);
        Assert.Equal(2, read.Payload!.Sequence);
        Assert.Null(read.Payload.Secrets);
    }

    [Fact]
    public async Task ChangePassword_OldPasswordNoLongerReads()
    {
        ConfigurationSharingEngine engine = await StartAsync();

        SharingStatus status = await engine.ChangePasswordAsync(NewPassword, Cancellation);

        Assert.Equal(SharingStatus.Published, status);
        Assert.Equal(PublicationReadStatus.WrongPassword, (await PublicationReader.PreviewAsync(Folder, Password, Cancellation)).Status);
        PublicationReadResult read = await PublicationReader.PreviewAsync(Folder, NewPassword, Cancellation);
        Assert.Equal(PublicationReadStatus.Ok, read.Status);
        Assert.Equal(1, read.Payload!.Sequence);
        Assert.Equal(NewPassword, host.Vault[SecretIds.PublicationPassword]);
        Assert.False(host.Vault.ContainsKey(SecretIds.PreviousPublicationPassword));
        Assert.Equal(SharingStatus.Published, await engine.OnConfigurationChangedAsync(Cancellation));
        Assert.Equal(2, (await PublicationReader.PreviewAsync(Folder, NewPassword, Cancellation)).Payload!.Sequence);
    }

    [Fact]
    public async Task Reset_NewMasterKeyReencryptsContent()
    {
        AuthoringFileReference file = source.Add("<profile />", "profile.xml");
        host.CurrentConfiguration = WithFile(file);
        ConfigurationSharingEngine engine = await StartAsync(host.CurrentConfiguration);
        byte[] oldMasterKey = Convert.FromBase64String(host.Vault[SecretIds.MasterKey]);
        string contentPath = Path.Combine(Folder, "content", file.ContentHash + ".bin");
        string moved = Folder + ".offline";

        // The folder goes offline while the reset rewrites the manifest: the reset is interrupted before anything changed there.
        Directory.Move(Folder, moved);
        Assert.Equal(SharingStatus.FolderUnavailable, await engine.ResetPublicationAsync(NewPassword, Cancellation));
        Directory.Move(moved, Folder);
        Assert.Equal(PublicationReadStatus.Ok, (await PublicationReader.PreviewAsync(Folder, Password, Cancellation)).Status);

        // The next change (or Retry) completes it: rewrap under the new password, then a publish under the new master key.
        SharingStatus status = await engine.OnConfigurationChangedAsync(Cancellation);

        Assert.Equal(SharingStatus.Published, status);
        byte[] newMasterKey = Convert.FromBase64String(host.Vault[SecretIds.MasterKey]);
        Assert.NotEqual(oldMasterKey, newMasterKey);
        Assert.Equal(PublicationReadStatus.WrongPassword, (await PublicationReader.PreviewAsync(Folder, Password, Cancellation)).Status);
        PublicationReadResult read = await PublicationReader.PreviewAsync(Folder, NewPassword, Cancellation);
        Assert.Equal(2, read.Payload!.Sequence);
        Assert.Equal(newMasterKey, read.Payload.MasterKey);
        Assert.True(ContentTransfer.IsComplete(contentPath, ContentTransfer.DeriveContentKey(newMasterKey, file.ContentHash)));
        Assert.False(ContentTransfer.IsComplete(contentPath, ContentTransfer.DeriveContentKey(oldMasterKey, file.ContentHash)));
        Assert.False(host.Vault.ContainsKey(SecretIds.PreviousPublicationPassword));
        Assert.Equal(2, engine.State.LastPublishedSequence);
    }

    [Fact]
    public async Task TakeOver_UsesPublicationRequestTakeOver()
    {
        using PublicationSigningKey other = PublicationSigningKey.Create();
        string foreignManifest = await PublishForeignAsync(other, 1);
        ConfigurationSharingEngine engine = CreateEngine();

        // The writer's take over opens the foreign manifest with the password given, so a wrong one changes nothing.
        Assert.Equal(SharingStatus.WrongPassword, await engine.TakeOverAsync(Folder, NewPassword, includePasswords: false, Cancellation));
        Assert.Equal(SharingRole.None, engine.Role);

        Assert.Equal(SharingStatus.Published, await engine.TakeOverAsync(Folder, Password, includePasswords: false, Cancellation));

        // A take over keeps the foreign manifest as the previous one, which only PublicationRequest.TakeOver does.
        Assert.True(File.Exists(foreignManifest));
        Assert.Equal(2, (await PublicationReader.PreviewAsync(Folder, Password, Cancellation)).Payload!.Sequence);
    }

    [Fact]
    public async Task StartPublishing_WeakPassword_Throws()
    {
        ConfigurationSharingEngine engine = CreateEngine();

        await Assert.ThrowsAsync<ArgumentException>(() => engine.StartPublishingAsync(Folder, "short", includePasswords: false, Cancellation));

        Assert.Equal(SharingRole.None, engine.Role);
        Assert.Empty(Directory.EnumerateFileSystemEntries(Folder));
        Assert.Empty(host.Vault);
    }

    [Fact]
    public async Task OnChanged_VaultUnavailable_ReturnsPasswordsNeededWithoutNewKeys()
    {
        ConfigurationSharingEngine engine = await StartAsync();
        string oldKeyId = (await PublicationReader.PreviewAsync(Folder, Password, Cancellation)).SignerKeyId!;

        // DPAPI reset: the vault reads as empty and unreadable.
        host.Vault.Clear();
        host.IsVaultAvailable = false;
        Assert.Equal(SharingStatus.PasswordsNeeded, await engine.OnConfigurationChangedAsync(Cancellation));
        Assert.Equal(SharingStatus.PasswordsNeeded, await engine.ChangePasswordAsync(NewPassword, Cancellation));
        Assert.Empty(host.Vault);

        // Once the vault can be written again the keys are still gone: no new identity is created silently.
        host.IsVaultAvailable = true;
        Assert.Equal(SharingStatus.PasswordsNeeded, await engine.OnConfigurationChangedAsync(Cancellation));
        Assert.Empty(host.Vault);
        Assert.Equal(1, (await PublicationReader.PreviewAsync(Folder, Password, Cancellation)).Payload!.Sequence);

        // The explicit take over with the current password is the recovery: a new key continues the sequence.
        Assert.Equal(SharingStatus.Published, await engine.TakeOverAsync(Folder, Password, includePasswords: false, Cancellation));
        PublicationReadResult read = await PublicationReader.PreviewAsync(Folder, Password, Cancellation);
        Assert.Equal(2, read.Payload!.Sequence);
        Assert.NotEqual(oldKeyId, read.SignerKeyId);
    }

    public void Dispose()
    {
        directory.Dispose();
    }

    private static FoundryConfigurationDocument WithArchitecture(WinPeArchitecture architecture) =>
        new() { General = new GeneralSettings { Architecture = architecture } };

    private static FoundryConfigurationDocument WithFile(AuthoringFileReference file) => new()
    {
        Network = new NetworkSettings { Dot1x = new Dot1xSettings { IsEnabled = true, ProfileTemplate = file } }
    };

    private ConfigurationSharingEngine CreateEngine() =>
        new(host, StatePath, source, new SharingLibraries(Path.Combine(directory.Path, "libraries")).Sync(), new FixedTimeProvider());

    private async Task<ConfigurationSharingEngine> StartAsync(FoundryConfigurationDocument? configuration = null)
    {
        host.CurrentConfiguration = configuration ?? WithArchitecture(WinPeArchitecture.X64);
        ConfigurationSharingEngine engine = CreateEngine();
        Assert.Equal(SharingStatus.Published, await engine.StartPublishingAsync(Folder, Password, includePasswords: false, Cancellation));
        return engine;
    }

    /// <summary>Publishes <paramref name="count"/> versions signed by another PC and returns the path of the last manifest.</summary>
    private async Task<string> PublishForeignAsync(PublicationSigningKey other, int count)
    {
        byte[] masterKey = RandomNumberGenerator.GetBytes(32);
        for (int sequence = 0; sequence < count; sequence++)
        {
            PublicationRequest request = new(Folder, new FoundryConfigurationDocument(), [], null, other, masterKey, Password, Guid.NewGuid(), "OTHER-PC",
                LastPublishedSequence: sequence);
            Assert.Equal(PublishStatus.Published, (await PublicationWriter.PublishAsync(request, source, null, Cancellation)).Status);
        }

        return ManifestPath(count);
    }

    private string ManifestPath(long sequence) => Directory.EnumerateFiles(Path.Combine(Folder, "manifests"))
        .Single(path => PublicationFolder.TryParseManifestFileName(Path.GetFileName(path), out long found) && found == sequence);

    private ConfigurationPackagePayload ReadManifest(long sequence)
    {
        PackageReadResult read = ConfigurationPackageCodec.Read(File.ReadAllBytes(ManifestPath(sequence)), Password);
        Assert.Equal(PackageReadStatus.Ok, read.Status);
        return read.Payload!;
    }
}
