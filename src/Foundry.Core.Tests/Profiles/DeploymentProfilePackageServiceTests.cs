// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Security.Cryptography;
using System.Text;
using Foundry.Core.Models.Configuration;
using Foundry.Core.Models.Profiles;
using Foundry.Core.Services.Profiles;
using Foundry.Telemetry;

namespace Foundry.Core.Tests.Profiles;

public sealed class DeploymentProfilePackageServiceTests
{
    private readonly DeploymentProfilePackageService _service = new();

    [Fact]
    public void ExportImport_PreservesSelectedSecretsAndExactAssetBytes()
    {
        byte[] content = Encoding.UTF8.GetBytes("<?xml version=\"1.0\"?>\r\n<unattend />\r\n");
        DeploymentProfileDocument profile = CreateProfile() with
        {
            Assets = [new() { Id = "answer", Kind = ProfileAssetKind.Unattend, RelativePath = "unattend/answer.xml", State = ProfileValueState.Present, Content = content, Sha256 = Convert.ToHexString(SHA256.HashData(content)) }],
            Secrets = new() { Entries = [new() { Purpose = ProfileSecretPurpose.WifiPassphrase, Identity = "wifi-1", State = ProfileValueState.Present, Value = Encoding.UTF8.GetBytes("confidential-wifi") }] }
        };

        byte[] package = _service.Export(profile, "a long export passphrase");
        DeploymentProfileDocument imported = _service.Import(package, "a long export passphrase");

        Assert.Equal(profile.ProfileId, imported.ProfileId);
        Assert.Equal(content, Assert.Single(imported.Assets).Content);
        Assert.Equal("confidential-wifi", Encoding.UTF8.GetString(Assert.Single(imported.Secrets.Entries).Value!));
        Assert.DoesNotContain("confidential-wifi", Encoding.UTF8.GetString(package));
        Assert.NotEqual(package, _service.Export(profile, "a long export passphrase"));
    }

    [Fact]
    public void ExportImport_DropsMachinePathsAndInlineSecretsWithoutMutatingSource()
    {
        DeploymentProfileDocument profile = CreateProfile() with
        {
            Configuration = new()
            {
                General = new() { IsoOutputPath = "C:/private.iso", CustomDriverDirectoryPath = "C:/drivers", IncludeDellDrivers = true },
                Network = new() { Wifi = new() { Ssid = "office", Passphrase = "private", CertificatePath = "C:/cert.pfx", EnterpriseProfileTemplatePath = "C:/wifi.xml" } },
                Telemetry = new() { InstallId = "private-install", ProjectToken = "private-token" }
            }
        };

        DeploymentProfileDocument imported = _service.Import(_service.Export(profile, "password"), "password");

        Assert.Null(imported.Configuration.General.IsoOutputPath);
        Assert.Null(imported.Configuration.General.CustomDriverDirectoryPath);
        Assert.True(imported.Configuration.General.IncludeDellDrivers);
        Assert.Equal("office", imported.Configuration.Network.Wifi.Ssid);
        Assert.Null(imported.Configuration.Network.Wifi.Passphrase);
        Assert.Null(imported.Configuration.Network.Wifi.CertificatePath);
        Assert.Null(imported.Configuration.Network.Wifi.EnterpriseProfileTemplatePath);
        Assert.Empty(imported.Configuration.Telemetry.InstallId);
        Assert.Equal("private", profile.Configuration.Network.Wifi.Passphrase);
    }

    [Fact]
    public void Import_RejectsWrongPasswordTamperingAndTruncation()
    {
        byte[] package = _service.Export(CreateProfile(), "password");
        Assert.ThrowsAny<CryptographicException>(() => _service.Import(package, "wrong"));
        package[^1] ^= 1;
        Assert.ThrowsAny<CryptographicException>(() => _service.Import(package, "password"));
        Assert.Throws<InvalidDataException>(() => _service.Import(package.AsSpan(0, 20), "password"));
    }

    [Fact]
    public void Encrypt_RequiresCorrectStoragePurposeAndKey()
    {
        byte[] key = RandomNumberGenerator.GetBytes(32);
        byte[] package = _service.Encrypt(CreateProfile(), key, ProfilePackagePurpose.LocalStorage);
        Assert.Equal("Office", _service.Decrypt(package, key, ProfilePackagePurpose.LocalStorage).DisplayName);
        Assert.Throws<InvalidDataException>(() => _service.Decrypt(package, key, ProfilePackagePurpose.SharedRevision));
        Assert.ThrowsAny<CryptographicException>(() => _service.Decrypt(package, RandomNumberGenerator.GetBytes(32), ProfilePackagePurpose.LocalStorage));
    }

    [Theory]
    [InlineData(ProfileValueState.Omitted)]
    [InlineData(ProfileValueState.Unavailable)]
    [InlineData(ProfileValueState.Deleted)]
    [InlineData(ProfileValueState.Blank)]
    public void EncryptDecrypt_PreservesExplicitSecretStates(ProfileValueState state)
    {
        byte[] key = new byte[32];
        DeploymentProfileDocument profile = CreateProfile() with { Secrets = new() { Entries = [new() { Purpose = ProfileSecretPurpose.AdministratorPassword, Identity = "administrator", State = state }] } };
        Assert.Equal(state, Assert.Single(_service.Decrypt(_service.Encrypt(profile, key, ProfilePackagePurpose.LocalStorage), key, ProfilePackagePurpose.LocalStorage).Secrets.Entries).State);
    }

    [Theory]
    [InlineData("../answer.xml")]
    [InlineData("C:/answer.xml")]
    [InlineData("folder/../answer.xml")]
    [InlineData("folder\\answer.xml")]
    [InlineData("folder/CON.xml")]
    public void Export_RejectsUnsafeAssetPaths(string path)
    {
        DeploymentProfileDocument profile = CreateProfile() with { Assets = [new() { Id = "asset", Kind = ProfileAssetKind.Unattend, RelativePath = path }] };
        Assert.Throws<InvalidDataException>(() => _service.Export(profile, "password"));
    }

    [Fact]
    public void Export_RejectsFutureSchemaAndAssetDigestMismatch()
    {
        Assert.Throws<InvalidDataException>(() => _service.Export(CreateProfile() with { Configuration = new() { SchemaVersion = int.MaxValue } }, "password"));
        Assert.Throws<InvalidDataException>(() => _service.Export(CreateProfile() with { FormatVersion = 2 }, "password"));
        DeploymentProfileDocument profile = CreateProfile() with { Assets = [new() { Id = "asset", Kind = ProfileAssetKind.Unattend, RelativePath = "answer.xml", State = ProfileValueState.Present, Content = [1, 2], Sha256 = new string('0', 64) }] };
        Assert.Throws<InvalidDataException>(() => _service.Export(profile, "password"));
    }

    [Fact]
    public void EncryptDecrypt_AuthenticatesExternalRevisionContext()
    {
        byte[] key = new byte[32];
        byte[] package = _service.Encrypt(CreateProfile(), key, ProfilePackagePurpose.SharedRevision, "revision-a"u8);
        Assert.Equal("Office", _service.Decrypt(package, key, ProfilePackagePurpose.SharedRevision, "revision-a"u8).DisplayName);
        Assert.ThrowsAny<CryptographicException>(() => _service.Decrypt(package, key, ProfilePackagePurpose.SharedRevision, "revision-b"u8));
    }

    [Fact]
    public void LocalStorage_RetainsHostSettingsWhileSharedRevisionRemovesThem()
    {
        byte[] key = new byte[32];
        DeploymentProfileDocument profile = CreateProfile() with
        {
            Configuration = new()
            {
                General = new() { IsoOutputPath = "C:/media.iso", CustomDriverDirectoryPath = "C:/drivers" },
                Network = new() { Dot1x = new() { CertificatePath = "C:/certificate.pfx" } },
                Telemetry = new() { InstallId = "local-install", IsEnabled = true }
            }
        };
        DeploymentProfileDocument local = _service.Decrypt(_service.Encrypt(profile, key, ProfilePackagePurpose.LocalStorage), key, ProfilePackagePurpose.LocalStorage);
        DeploymentProfileDocument shared = _service.Decrypt(_service.Encrypt(profile, key, ProfilePackagePurpose.SharedRevision), key, ProfilePackagePurpose.SharedRevision);
        Assert.Equal("C:/media.iso", local.Configuration.General.IsoOutputPath);
        Assert.Equal("C:/drivers", local.Configuration.General.CustomDriverDirectoryPath);
        Assert.Equal("C:/certificate.pfx", local.Configuration.Network.Dot1x.CertificatePath);
        Assert.Equal("local-install", local.Configuration.Telemetry.InstallId);
        Assert.Null(shared.Configuration.General.IsoOutputPath);
        Assert.Null(shared.Configuration.Network.Dot1x.CertificatePath);
        Assert.Empty(shared.Configuration.Telemetry.InstallId);
    }

    [Theory]
    [InlineData(9)]
    [InlineData(10)]
    [InlineData(13)]
    public void Import_RejectsChangedPurposeAndKdfCostBeforeDerivation(int offset)
    {
        byte[] package = _service.Export(CreateProfile(), "password");
        package[offset] ^= 0x7f;
        Assert.Throws<InvalidDataException>(() => _service.Import(package, "password"));
    }

    [Theory]
    [InlineData(14)]
    [InlineData(30)]
    [InlineData(42)]
    [InlineData(58)]
    [InlineData(90)]
    [InlineData(102)]
    public void Decrypt_AuthenticatesSaltNoncesWrappedKeyAndTags(int offset)
    {
        byte[] key = new byte[32];
        byte[] package = _service.Encrypt(CreateProfile(), key, ProfilePackagePurpose.LocalStorage);
        package[offset] ^= 1;
        Assert.ThrowsAny<CryptographicException>(() => _service.Decrypt(package, key, ProfilePackagePurpose.LocalStorage));
    }

    [Theory]
    [InlineData("duplicate")]
    [InlineData("unknown-property")]
    [InlineData("missing-state")]
    [InlineData("invalid-state")]
    [InlineData("credential-target")]
    public void Decrypt_RejectsAuthenticatedMalformedPayload(string mutation)
    {
        byte[] key = new byte[32];
        DeploymentProfileDocument profile = CreateProfile() with { Secrets = new() { Entries = [new() { Identity = "identity", State = ProfileValueState.Omitted }] } };
        byte[] package = _service.Encrypt(profile, key, ProfilePackagePurpose.LocalStorage);
        byte[] invalid = RewriteAuthenticatedPayload(package, key, json => mutation switch
        {
            "duplicate" => json.Replace("\"formatVersion\":1", "\"formatVersion\":1,\"FormatVersion\":1", StringComparison.Ordinal),
            "unknown-property" => json.Replace("\"displayName\":", "\"nativeTarget\":\"arbitrary\",\"displayName\":", StringComparison.Ordinal),
            "missing-state" => json.Replace("\"state\":0,", string.Empty, StringComparison.Ordinal),
            "invalid-state" => json.Replace("\"state\":0", "\"state\":999", StringComparison.Ordinal),
            "credential-target" => json.Replace("\"identity\":\"identity\"", "\"identity\":\"FoundryOSD/Proxy\"", StringComparison.Ordinal),
            _ => throw new InvalidOperationException()
        });
        Assert.Throws<InvalidDataException>(() => _service.Decrypt(invalid, key, ProfilePackagePurpose.LocalStorage));
    }

    [Fact]
    public void Decrypt_DistinguishesFutureFormatsFromCorruptData()
    {
        byte[] key = new byte[32];
        byte[] package = _service.Encrypt(CreateProfile(), key, ProfilePackagePurpose.LocalStorage);
        byte[] futureSchema = RewriteAuthenticatedPayload(package, key, json => json.Replace(
            $"\"schemaVersion\":{FoundryConfigurationDocument.CurrentSchemaVersion}", "\"schemaVersion\":2147483647", StringComparison.Ordinal));
        byte[] futurePayload = RewriteAuthenticatedPayload(package, key, json => json.Replace("\"formatVersion\":1", "\"formatVersion\":2", StringComparison.Ordinal));
        Assert.Throws<NotSupportedException>(() => _service.Decrypt(futureSchema, key, ProfilePackagePurpose.LocalStorage));
        Assert.Throws<NotSupportedException>(() => _service.Decrypt(futurePayload, key, ProfilePackagePurpose.LocalStorage));
        package[8]++;
        Assert.Throws<NotSupportedException>(() => _service.Decrypt(package, key, ProfilePackagePurpose.LocalStorage));
    }

    [Fact]
    public void Export_RejectsAmbiguousAssetPathsAndSecretStates()
    {
        DeploymentProfileAsset asset = new() { Id = "a", RelativePath = "assets/a.xml", Kind = ProfileAssetKind.Unattend };
        Assert.Throws<InvalidDataException>(() => _service.Export(CreateProfile() with { Assets = [asset, asset with { Id = "b", RelativePath = "ASSETS/A.XML" }] }, "password"));
        DeploymentProfileSecret secret = new() { Identity = "a", State = ProfileValueState.Unavailable, Value = [1] };
        Assert.Throws<InvalidDataException>(() => _service.Export(CreateProfile() with { Secrets = new() { Entries = [secret] } }, "password"));
    }

    [Theory]
    [InlineData(ProfileAssetKind.WiredProfile)]
    [InlineData(ProfileAssetKind.WifiProfile)]
    [InlineData(ProfileAssetKind.WiredCertificate)]
    [InlineData(ProfileAssetKind.WifiCertificate)]
    [InlineData(ProfileAssetKind.AutopilotCertificate)]
    public void Encrypt_RejectsAmbiguousSingletonAssetsBeforeActivation(ProfileAssetKind kind)
    {
        DeploymentProfileAsset asset = new() { Id = "first", Kind = kind, RelativePath = "assets/first.bin" };
        DeploymentProfileDocument profile = CreateProfile() with
        {
            Assets = [asset, asset with { Id = "second", RelativePath = "assets/second.bin" }]
        };

        Assert.Throws<InvalidDataException>(() => _service.Encrypt(profile, new byte[32], ProfilePackagePurpose.SharedRevision));
    }

    [Fact]
    public void EncryptDecrypt_PreservesMultipleAnswerFileAssets()
    {
        DeploymentProfileAsset asset = new() { Id = "first", Kind = ProfileAssetKind.Unattend, RelativePath = "assets/first.xml" };
        DeploymentProfileDocument profile = CreateProfile() with
        {
            Assets = [asset, asset with { Id = "second", RelativePath = "assets/second.xml" }]
        };
        byte[] key = new byte[32];

        DeploymentProfileDocument imported = _service.Decrypt(_service.Encrypt(profile, key, ProfilePackagePurpose.SharedRevision), key, ProfilePackagePurpose.SharedRevision);

        Assert.Equal(2, imported.Assets.Count);
    }

    [Fact]
    public void ImportExport_RejectsOversizedPackagesAssetsAndSecretCollections()
    {
        Assert.Throws<InvalidDataException>(() => _service.Import(new byte[(16 * 1024 * 1024) + 119], "password"));
        byte[] bytes = new byte[(4 * 1024 * 1024) + 1];
        DeploymentProfileAsset asset = new() { Id = "a", RelativePath = "a.xml", State = ProfileValueState.Present, Content = bytes, Sha256 = Convert.ToHexString(SHA256.HashData(bytes)) };
        Assert.Throws<InvalidDataException>(() => _service.Export(CreateProfile() with { Assets = [asset] }, "password"));
        DeploymentProfileSecret[] secrets = Enumerable.Range(0, 129).Select(index => new DeploymentProfileSecret { Identity = $"identity-{index}" }).ToArray();
        Assert.Throws<InvalidDataException>(() => _service.Export(CreateProfile() with { Secrets = new() { Entries = secrets } }, "password"));
    }

    [Fact]
    public void Export_RejectsAssetWhoseDigestDisagreesWithAuthoringSource()
    {
        byte[] content = [1, 2, 3];
        string id = Guid.NewGuid().ToString("N");
        DeploymentProfileDocument profile = CreateProfile() with
        {
            Configuration = new() { Unattend = new() { Files = [new() { Id = id, DisplayName = "Answer", ContentHash = new string('0', 64) }] } },
            Assets = [new() { Id = id, Kind = ProfileAssetKind.Unattend, RelativePath = "answer.xml", State = ProfileValueState.Present, Content = content, Sha256 = Convert.ToHexString(SHA256.HashData(content)) }]
        };
        Assert.Throws<InvalidDataException>(() => _service.Export(profile, "password"));
    }

    [Fact]
    public void Export_RejectsNullRequiredConfigurationSections()
    {
        DeploymentProfileDocument profile = CreateProfile() with { Configuration = new() { Customization = new() { MachineNaming = null! } } };
        Assert.Throws<InvalidDataException>(() => _service.Export(profile, "password"));
    }
    [Theory]
    [InlineData("naming-components")]
    [InlineData("naming-entry")]
    [InlineData("appx-packages")]
    [InlineData("appx-entry")]
    [InlineData("autopilot-entry")]
    public void Export_RejectsConfigurationCollectionsThatCannotBeActivated(string section)
    {
        FoundryConfigurationDocument configuration = section switch
        {
            "naming-components" => new() { Customization = new() { MachineNaming = new() { Components = null! } } },
            "naming-entry" => new() { Customization = new() { MachineNaming = new() { Components = [null!] } } },
            "appx-packages" => new() { Customization = new() { AppxRemoval = new() { PackageNames = null! } } },
            "appx-entry" => new() { Customization = new() { AppxRemoval = new() { PackageNames = [null!] } } },
            "autopilot-entry" => new() { Autopilot = new() { Profiles = [null!] } },
            _ => throw new InvalidOperationException()
        };
        Assert.Throws<InvalidDataException>(() => _service.Export(CreateProfile() with { Configuration = configuration }, "password"));
    }

    [Fact]
    public void Export_RejectsCollidingAutopilotFolders()
    {
        AutopilotProfileSettings first = CreateAutopilotProfile();
        DeploymentProfileDocument profile = CreateProfile() with { Configuration = new() { Autopilot = new() { Profiles = [first, first with { Id = "second", FolderName = "AUTOPILOT" }] } } };
        Assert.Throws<InvalidDataException>(() => _service.Export(profile, "password"));
    }

    [Theory]
    [InlineData("../Autopilot")]
    [InlineData("folder/Autopilot")]
    [InlineData("CON.txt")]
    [InlineData("Autopilot.")]
    public void Export_RejectsUnsafeInlineAutopilotFolder(string folder)
    {
        DeploymentProfileDocument profile = CreateProfile() with { Configuration = new() { Autopilot = new() { Profiles = [CreateAutopilotProfile() with { FolderName = folder }] } } };
        Assert.Throws<InvalidDataException>(() => _service.Export(profile, "password"));
    }

    [Fact]
    public void ExportImport_PreservesSafeUnicodeAutopilotFolderAndInlineJson()
    {
        AutopilotProfileSettings autopilot = CreateAutopilotProfile() with { FolderName = "Équipe_Paris" };
        DeploymentProfileDocument profile = CreateProfile() with { Configuration = new() { Autopilot = new() { Profiles = [autopilot] } } };
        AutopilotProfileSettings imported = Assert.Single(_service.Import(_service.Export(profile, "password"), "password").Configuration.Autopilot.Profiles);
        Assert.Equal(autopilot.FolderName, imported.FolderName);
        Assert.Equal(autopilot.JsonContent, imported.JsonContent);
    }

    [Fact]
    public void Export_RejectsInvalidUtf8SecretBytes()
    {
        DeploymentProfileSecret secret = new() { Identity = "identity", State = ProfileValueState.Present, Value = [0xff] };
        Assert.Throws<InvalidDataException>(() => _service.Export(CreateProfile() with { Secrets = new() { Entries = [secret] } }, "password"));
    }

    private static AutopilotProfileSettings CreateAutopilotProfile() => new()
    {
        Id = "first",
        DisplayName = "Autopilot",
        FolderName = "Autopilot",
        Source = "Imported",
        ImportedAtUtc = DateTimeOffset.UnixEpoch,
        JsonContent = "{\"CloudAssignedTenantId\":\"test-tenant\"}"
    };
    private static byte[] RewriteAuthenticatedPayload(byte[] package, byte[] wrappingKey, Func<string, string> mutate)
    {
        byte[] dataKey = new byte[32];
        byte[] plaintext = new byte[package.Length - 118];
        using var wrapping = new AesGcm(wrappingKey, 16);
        wrapping.Decrypt(package.AsSpan(30, 12), package.AsSpan(58, 32), package.AsSpan(42, 16), dataKey, package.AsSpan(0, 30));
        using var data = new AesGcm(dataKey, 16);
        data.Decrypt(package.AsSpan(90, 12), package.AsSpan(118), package.AsSpan(102, 16), plaintext, package.AsSpan(0, 90));
        byte[] changed = Encoding.UTF8.GetBytes(mutate(Encoding.UTF8.GetString(plaintext)));
        byte[] result = new byte[118 + changed.Length];
        package.AsSpan(0, 118).CopyTo(result);
        RandomNumberGenerator.Fill(result.AsSpan(90, 12));
        data.Encrypt(result.AsSpan(90, 12), changed, result.AsSpan(118), result.AsSpan(102, 16), result.AsSpan(0, 90));
        CryptographicOperations.ZeroMemory(dataKey);
        CryptographicOperations.ZeroMemory(plaintext);
        CryptographicOperations.ZeroMemory(changed);
        return result;
    }
    private static DeploymentProfileDocument CreateProfile() => new() { ProfileId = Guid.NewGuid(), DisplayName = "Office" };
}
