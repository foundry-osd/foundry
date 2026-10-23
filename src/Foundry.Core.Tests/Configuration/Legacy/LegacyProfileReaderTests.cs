// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using Foundry.Core.Models.Configuration;
using Foundry.Core.Services.Configuration.Legacy;
using Foundry.Core.Tests.TestUtilities;

namespace Foundry.Core.Tests.Configuration.Legacy;

public sealed class LegacyProfileReaderTests
{
    private static readonly LegacyFixtureValues Expected = LegacyFixtures.LoadValues();
    private static string StoreRoot => LegacyFixtures.FixturePath(LegacyFixtures.LocalStore);

    [Fact]
    public void ReadPackage_ExportWithSecrets_ReturnsConfigurationSecretsAndFiles()
    {
        LegacyConfigurationSnapshot snapshot = LegacyProfileReader.ReadPackage(LegacyFixtures.Read(LegacyFixtures.ExportWithSecrets), Expected.ExportPassphrase);

        Assert.Equal("Fixture export", snapshot.DisplayName);
        Assert.Equal(Expected.WifiSsid, snapshot.Configuration.Network.Wifi.Ssid);
        Assert.Contains(snapshot.Secrets, secret => secret is { Kind: LegacySecretKind.WifiPassphrase, Key: null } && secret.Value == Expected.WifiPassphrase);
        Assert.Contains(snapshot.Secrets, secret => secret.Kind == LegacySecretKind.DeploymentProtection && secret.Value == Expected.DeploymentPassword);
        Assert.Contains(snapshot.Secrets, secret => secret.Kind == LegacySecretKind.OobeAdministrator && secret.Value == Expected.AdministratorPassword);
        Assert.Contains(snapshot.Secrets, secret => secret.Kind == LegacySecretKind.OobeAccount && secret.Key == Expected.OobeAccountId && secret.Value == Expected.OobeAccountPassword);
        Assert.Contains(snapshot.Secrets, secret => secret.Kind == LegacySecretKind.WiredCertificatePassword && secret.Value == Expected.WiredCertificatePassword);
        Assert.DoesNotContain(snapshot.Secrets, secret => secret.Kind == LegacySecretKind.DomainJoin);
        Assert.Contains(snapshot.Files, file => file.Role == LegacyFileRole.WiredCertificate && file.FileName.EndsWith(".pfx", StringComparison.Ordinal)
            && file.Content.SequenceEqual(Convert.FromBase64String(Expected.WiredCertificateBase64)));
        Assert.Contains(snapshot.Files, file => file.Role == LegacyFileRole.WiredProfile && file.FileName.EndsWith(".xml", StringComparison.Ordinal));
        Assert.Contains(snapshot.Files, file => file.Role == LegacyFileRole.Unattend && file.UnattendId == Expected.UnattendId);
        Assert.Null(snapshot.Configuration.Network.Dot1x.CertificatePath);
        Assert.False(snapshot.IsConnectionFile);
        Assert.False(snapshot.WasShared);
    }

    [Fact]
    public void ReadPackage_WrongPassphrase_ThrowsLegacyPasswordException()
    {
        Assert.Throws<LegacyPasswordException>(() =>
            LegacyProfileReader.ReadPackage(LegacyFixtures.Read(LegacyFixtures.ExportWithSecrets), "fixture-wrong-passphrase"));
    }

    [Fact]
    public void ReadPackage_TamperedPayload_ThrowsInvalidDataException()
    {
        byte[] package = LegacyFixtures.Read(LegacyFixtures.ExportWithSecrets);
        package[^1] ^= 0x01;

        Assert.Throws<InvalidDataException>(() => LegacyProfileReader.ReadPackage(package, Expected.ExportPassphrase));
    }

    [Fact]
    public void ReadPackage_ConnectionFile_SetsIsConnectionFileAndIgnoresKey()
    {
        LegacyConfigurationSnapshot snapshot = LegacyProfileReader.ReadPackage(LegacyFixtures.Read(LegacyFixtures.Connection), Expected.ConnectionPassphrase);

        Assert.True(snapshot.IsConnectionFile);
        Assert.True(snapshot.WasShared);
        Assert.Equal(Expected.WifiSsid, snapshot.Configuration.Network.Wifi.Ssid);
        Assert.Contains(snapshot.Secrets, secret => secret.Kind == LegacySecretKind.WifiPassphrase && secret.Value == Expected.WifiPassphrase);
        Assert.DoesNotContain(snapshot.Secrets, secret => secret.Value.Contains(Expected.ConnectionKeyBase64, StringComparison.Ordinal));
    }

    [Fact]
    public void ReadPackage_ExportWithoutSecrets_ReturnsNoSecretValues()
    {
        LegacyConfigurationSnapshot snapshot = LegacyProfileReader.ReadPackage(LegacyFixtures.Read(LegacyFixtures.ExportWithoutSecrets), Expected.ExportPassphrase);

        Assert.Empty(snapshot.Secrets);
        Assert.Empty(snapshot.Files);
        Assert.Equal(Expected.WifiSsid, snapshot.Configuration.Network.Wifi.Ssid);
    }

    [Fact]
    public void ListLocalProfiles_FixtureStore_ReturnsActiveAndSharedFlags()
    {
        IReadOnlyList<LegacyLocalProfile> profiles = LegacyProfileReader.ListLocalProfiles(StoreRoot);

        Assert.Equal(
            [
                new LegacyLocalProfile(Expected.ActiveLocalId, Expected.ActiveDisplayName, IsActive: true, IsShared: false),
                new LegacyLocalProfile(Expected.SharedLocalId, Expected.SharedDisplayName, IsActive: false, IsShared: true)
            ],
            profiles);
    }

    [Fact]
    public void ListLocalProfiles_InaccessibleProfile_SkipsItAndListsTheOthers()
    {
        using var store = new TemporaryDirectory();
        CopyDirectory(StoreRoot, store.Path);
        string sharedHead = Path.Combine(store.Path, "profiles", Expected.SharedLocalId.ToString("N"), "head.json");
        using var exclusive = new FileStream(sharedHead, FileMode.Open, FileAccess.Read, FileShare.None);

        LegacyLocalProfile listed = Assert.Single(LegacyProfileReader.ListLocalProfiles(store.Path));

        Assert.Equal(Expected.ActiveLocalId, listed.LocalId);
    }

    [Fact]
    public void ListLocalProfiles_MissingStore_ReturnsEmpty()
    {
        Assert.Empty(LegacyProfileReader.ListLocalProfiles(Path.Combine(StoreRoot, "missing")));
    }

    [Fact]
    public void ReadLocalProfile_Active_ReturnsDomainJoinSecret()
    {
        LegacyConfigurationSnapshot snapshot = LegacyProfileReader.ReadLocalProfile(StoreRoot, Expected.ActiveLocalId, LoadCredentials());

        Assert.Equal(Expected.ActiveDisplayName, snapshot.DisplayName);
        Assert.Contains(snapshot.Secrets, secret => secret.Kind == LegacySecretKind.DomainJoin && secret.Key == Expected.DomainAccount && secret.Value == Expected.DomainPassword);
        Assert.Contains(snapshot.Files, file => file.Role == LegacyFileRole.WiredCertificate);
        Assert.Equal(@"C:\FoundryFixture\wired-certificate.pfx", snapshot.Configuration.Network.Dot1x.CertificatePath);
        Assert.False(snapshot.WasShared);
        Assert.False(snapshot.IsConnectionFile);
    }

    [Fact]
    public void ReadLocalProfile_Enrolled_ReportsWasSharedAndKeepsLocalValues()
    {
        LegacyConfigurationSnapshot snapshot = LegacyProfileReader.ReadLocalProfile(StoreRoot, Expected.SharedLocalId, LoadCredentials());

        Assert.Equal(Expected.SharedDisplayName, snapshot.DisplayName);
        Assert.True(snapshot.WasShared);
        Assert.False(snapshot.IsConnectionFile);
        // A local revision is not portable: its per-PC telemetry values survive.
        Assert.Equal("fake-project-token", snapshot.Configuration.Telemetry.ProjectToken);
    }

    [Fact]
    public void ReadPackage_NullConfigurationSections_UsesDefaults()
    {
        string payload = $$$"""
            {"formatVersion":1,"profileId":"88888888-8888-4888-8888-888888888888","displayName":"Null sections",
             "configuration":{"schemaVersion":{{{FoundryConfigurationDocument.CurrentSchemaVersion}}},"network":null,"autopilot":null,
               "customization":{"oobe":null},"domainJoin":{"domains":null}},
             "secrets":{"entries":[{"purpose":1,"identity":"UNMATCHED","state":4,"value":"{{{Convert.ToBase64String(Encoding.UTF8.GetBytes("fake-value"))}}}"}]},
             "assets":[]}
            """;

        LegacyConfigurationSnapshot snapshot = LegacyProfileReader.ReadPackage(EncryptPackage(Encoding.UTF8.GetBytes(payload), Expected.ExportPassphrase), Expected.ExportPassphrase);

        Assert.Null(snapshot.Configuration.Network.Wifi.Ssid);
        Assert.Empty(snapshot.Configuration.Customization.Oobe.AdditionalAccounts);
        Assert.Empty(snapshot.Configuration.DomainJoin.Domains);
        Assert.Empty(snapshot.Secrets);
    }

    [Fact]
    public void ReadLocalProfile_MissingCredential_ThrowsLegacyProfileLockedException()
    {
        LegacyProfileLockedException exception = Assert.Throws<LegacyProfileLockedException>(() =>
            LegacyProfileReader.ReadLocalProfile(StoreRoot, Expected.ActiveLocalId, new FakeCredentialStore()));

        Assert.Equal(Expected.ActiveLocalId, exception.LocalId);
    }

    [Fact]
    public void IsLegacyPackage_NewFormatJson_ReturnsFalse()
    {
        Assert.False(LegacyProfileReader.IsLegacyPackage(Encoding.UTF8.GetBytes("{\"format\":\"foundry-configuration\",\"version\":1}")));
        Assert.True(LegacyProfileReader.IsLegacyPackage(LegacyFixtures.Read(LegacyFixtures.ExportWithSecrets)));
    }

    [Fact]
    public void ReadPackage_ExportWithDriverSets_KeepsCustomDriversSectionWithoutContent()
    {
        LegacyConfigurationSnapshot snapshot = LegacyProfileReader.ReadPackage(LegacyFixtures.Read(LegacyFixtures.ExportWithSecrets), Expected.ExportPassphrase);

        Assert.True(snapshot.Configuration.CustomDrivers.IsEnabled);
        Assert.Equal(Expected.DriverSetId, Assert.Single(snapshot.Configuration.CustomDrivers.Sets).Id);
        Assert.Equal(Expected.DriverSetId, snapshot.Configuration.CustomDrivers.DefaultSetId);
        Assert.Equal(3, snapshot.Files.Count);
    }

    private static FakeCredentialStore LoadCredentials() =>
        FakeCredentialStore.LoadFixture(StoreRoot, LegacyFixtures.FixturePath(LegacyFixtures.LocalStoreCredentials));

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (string file in Directory.GetFiles(source)) File.Copy(file, Path.Combine(destination, Path.GetFileName(file)));
        foreach (string directory in Directory.GetDirectories(source)) CopyDirectory(directory, Path.Combine(destination, Path.GetFileName(directory)));
    }

    /// <summary>Writes a FNDRYPRF v1 passphrase package around a hand-written payload, as the retired exporter did.</summary>
    private static byte[] EncryptPackage(byte[] payload, string passphrase)
    {
        byte[] package = new byte[90 + 28 + payload.Length];
        "FNDRYPRF"u8.CopyTo(package);
        package[8] = 1;
        package[9] = 1;
        BinaryPrimitives.WriteInt32LittleEndian(package.AsSpan(10, 4), 600_000);
        RandomNumberGenerator.Fill(package.AsSpan(14, 16));
        byte[] key = Rfc2898DeriveBytes.Pbkdf2(passphrase, package[14..30], 600_000, HashAlgorithmName.SHA256, 32);
        byte[] dataKey = RandomNumberGenerator.GetBytes(32);
        Seal(dataKey, key, package.AsSpan(30, 60), package.AsSpan(0, 30));
        Seal(payload, dataKey, package.AsSpan(90), package.AsSpan(0, 90));
        return package;

        static void Seal(ReadOnlySpan<byte> plaintext, byte[] key, Span<byte> destination, ReadOnlySpan<byte> associatedData)
        {
            RandomNumberGenerator.Fill(destination[..12]);
            using AesGcm aes = new(key, 16);
            aes.Encrypt(destination[..12], plaintext, destination[28..], destination.Slice(12, 16), associatedData);
        }
    }
}
