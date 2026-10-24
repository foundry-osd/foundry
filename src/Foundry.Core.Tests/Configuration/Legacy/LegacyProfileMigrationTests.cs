// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Security.Cryptography;
using Foundry.Core.Models.Configuration;
using Foundry.Core.Services.Configuration.Legacy;
using Foundry.Core.Services.Files;
using Foundry.Core.Services.Secrets;
using Foundry.Core.Tests.TestUtilities;

namespace Foundry.Core.Tests.Configuration.Legacy;

public sealed class LegacyProfileMigrationTests : IDisposable
{
    private static readonly LegacyFixtureValues Expected = LegacyFixtures.LoadValues();
    private readonly TemporaryDirectory temp = new();
    private readonly AuthoringFileLibrary library;

    public LegacyProfileMigrationTests()
    {
        library = new AuthoringFileLibrary(Path.Combine(temp.Path, "Files"));
    }

    private static string StoreRoot => LegacyFixtures.FixturePath(LegacyFixtures.LocalStore);

    private static string WiredCertificateHash => Convert.ToHexStringLower(SHA256.HashData(Convert.FromBase64String(Expected.WiredCertificateBase64)));

    [Fact]
    public void Migrate_ActiveProfile_MapsAllSecretsToVaultIds()
    {
        LegacyConfigurationSnapshot profile = ReadProfile(Expected.ActiveLocalId);

        LegacyMigrationResult result = LegacyProfileMigration.Migrate(profile.Configuration, profile, activeProfileLocked: false, Import);

        Assert.Equal(new Dictionary<string, string>
        {
            [SecretIds.DeploymentProtection] = Expected.DeploymentPassword,
            [SecretIds.WifiPassphrase] = Expected.WifiPassphrase,
            [SecretIds.Dot1xCertificate(WiredCertificateHash)] = Expected.WiredCertificatePassword,
            [SecretIds.OobeAdministrator] = Expected.AdministratorPassword,
            [SecretIds.OobeAccount(Expected.OobeAccountId)] = Expected.OobeAccountPassword,
            [SecretIds.DomainAccount(Expected.DomainAccount)] = Expected.DomainPassword,
        }, result.VaultEntries);
        Assert.Equal(WiredCertificateHash, result.Configuration.Network.Dot1x.Certificate?.ContentHash);
        Assert.Empty(result.Notices);
    }

    [Fact]
    public void Migrate_JsonNewerThanProfile_KeepsJsonSettings()
    {
        LegacyConfigurationSnapshot profile = ReadProfile(Expected.ActiveLocalId);
        FoundryConfigurationDocument json = profile.Configuration with
        {
            Network = profile.Configuration.Network with { Wifi = profile.Configuration.Network.Wifi with { Ssid = "RenamedAfterLastProfileSave" } },
            Customization = profile.Configuration.Customization with
            {
                Oobe = profile.Configuration.Customization.Oobe with { SkipLicenseTerms = !profile.Configuration.Customization.Oobe.SkipLicenseTerms }
            }
        };

        LegacyMigrationResult result = LegacyProfileMigration.Migrate(json, profile, activeProfileLocked: false, Import);

        Assert.Equal("RenamedAfterLastProfileSave", result.Configuration.Network.Wifi.Ssid);
        Assert.Equal(json.Customization.Oobe.SkipLicenseTerms, result.Configuration.Customization.Oobe.SkipLicenseTerms);
        // The passphrase belonged to the network the profile named, not to the one the newer settings name.
        Assert.False(result.VaultEntries.ContainsKey(SecretIds.WifiPassphrase));
        Assert.Contains(MigrationNotice.PasswordsNeeded, result.Notices);
    }

    [Fact]
    public void Migrate_ProfileFileFillsMissingReference()
    {
        LegacyConfigurationSnapshot profile = ReadProfile(Expected.ActiveLocalId);

        LegacyMigrationResult result = LegacyProfileMigration.Migrate(profile.Configuration, profile, activeProfileLocked: false, Import);

        AuthoringFileReference certificate = Assert.IsType<AuthoringFileReference>(result.Configuration.Network.Dot1x.Certificate);
        Assert.Equal(Convert.FromBase64String(Expected.WiredCertificateBase64), library.ReadVerified(certificate));
        Assert.NotNull(result.Configuration.Network.Dot1x.ProfileTemplate);
        UnattendFileSettings answerFile = Assert.Single(result.Configuration.Unattend.Files);
        Assert.Equal(answerFile.ContentHash, answerFile.File?.ContentHash, ignoreCase: true);
    }

    [Fact]
    public void Migrate_ProfileFileReplacesReferenceImportedFromPath()
    {
        LegacyConfigurationSnapshot profile = ReadProfile(Expected.ActiveLocalId);
        AuthoringFileReference pathImport = library.Import("bytes found at the legacy host path"u8, "machine.pfx");
        FoundryConfigurationDocument json = profile.Configuration with
        {
            Network = profile.Configuration.Network with { Dot1x = profile.Configuration.Network.Dot1x with { Certificate = pathImport } }
        };

        LegacyMigrationResult result = LegacyProfileMigration.Migrate(json, profile, activeProfileLocked: false, Import);

        Assert.Equal(WiredCertificateHash, result.Configuration.Network.Dot1x.Certificate?.ContentHash);
        Assert.Equal(Expected.WiredCertificatePassword, result.VaultEntries[SecretIds.Dot1xCertificate(WiredCertificateHash)]);
        Assert.False(result.VaultEntries.ContainsKey(SecretIds.Dot1xCertificate(pathImport.ContentHash)));
    }

    [Fact]
    public void Migrate_Locked_ReturnsPasswordsNeeded()
    {
        var current = new FoundryConfigurationDocument();

        LegacyMigrationResult result = LegacyProfileMigration.Migrate(current, activeProfile: null, activeProfileLocked: true, Import);

        Assert.Same(current, result.Configuration);
        Assert.Empty(result.VaultEntries);
        Assert.Equal(new[] { MigrationNotice.PasswordsNeeded }, result.Notices.ToArray());
    }

    [Fact]
    public void Migrate_SharedProfile_ReturnsLegacySyncRetired()
    {
        LegacyConfigurationSnapshot profile = ReadProfile(Expected.SharedLocalId);

        LegacyMigrationResult result = LegacyProfileMigration.Migrate(profile.Configuration, profile, activeProfileLocked: false, Import);

        Assert.Contains(MigrationNotice.LegacySyncRetired, result.Notices);
        Assert.DoesNotContain(MigrationNotice.PasswordsNeeded, result.Notices);
    }

    [Fact]
    public void Migrate_NoProfile_ReturnsCurrentUnchanged()
    {
        var current = new FoundryConfigurationDocument();

        LegacyMigrationResult result = LegacyProfileMigration.Migrate(current, activeProfile: null, activeProfileLocked: false, Import);

        Assert.Same(current, result.Configuration);
        Assert.Empty(result.VaultEntries);
        Assert.Empty(result.Notices);
    }

    [Fact]
    public void Migrate_RequiredFileStillMissing_ReturnsFilesToReselect()
    {
        var current = new FoundryConfigurationDocument
        {
            Autopilot = new AutopilotSettings { IsEnabled = true, ProvisioningMode = AutopilotProvisioningMode.HardwareHashUpload }
        };

        LegacyMigrationResult result = LegacyProfileMigration.Migrate(current, activeProfile: null, activeProfileLocked: false, Import);

        Assert.Equal(new[] { MigrationNotice.FilesToReselect }, result.Notices.ToArray());
    }

    [Fact]
    public void DeleteStaging_RemovesWorkingCopiesAndKeepsTheProfileStore()
    {
        string root = Path.Combine(temp.Path, "Profiles");
        string head = Path.Combine(root, "profiles", Expected.ActiveLocalId.ToString("N"), "head.json");
        string stagedCertificate = Path.Combine(root, "Staging", "build-1", "certificate.pfx");
        Directory.CreateDirectory(Path.GetDirectoryName(head)!);
        Directory.CreateDirectory(Path.GetDirectoryName(stagedCertificate)!);
        File.WriteAllText(head, "{}");
        File.WriteAllText(stagedCertificate, "decrypted copy");
        File.SetAttributes(stagedCertificate, FileAttributes.ReadOnly);

        Assert.True(LegacyProfileMigration.DeleteStaging(root));

        Assert.False(Directory.Exists(Path.Combine(root, "Staging")));
        Assert.True(File.Exists(head));
        Assert.False(LegacyProfileMigration.DeleteStaging(root));
    }

    public void Dispose() => temp.Dispose();

    private AuthoringFileReference Import(LegacyFile file) => library.Import(file.Content, file.FileName);

    private static LegacyConfigurationSnapshot ReadProfile(Guid localId) => LegacyProfileReader.ReadLocalProfile(
        StoreRoot, localId, FakeCredentialStore.LoadFixture(StoreRoot, LegacyFixtures.FixturePath(LegacyFixtures.LocalStoreCredentials)));
}
