// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Foundry.Core.Models.Configuration;
using Foundry.Core.Services.Sharing;
using Foundry.Core.Services.WinPe;

namespace Foundry.Core.Tests.Sharing;

public sealed class ConfigurationPackageCodecTests
{
    private const string Passphrase = "correct-horse-battery";
    private const string FileHash = "0f343b0931126a20f133d67c2b018a3b5e4f0b1f0b1f0b1f0b1f0b1f0b1f0b1f";

    [Fact]
    public void Write_Read_RoundTrip_PreservesConfigurationSecretsFilesAndContents()
    {
        byte[] file = [1, 2, 3, 4];
        ConfigurationPackagePayload payload = CreateExport() with
        {
            Configuration = new FoundryConfigurationDocument
            {
                SchemaVersion = ConfigurationSchemaVersions.FoundryCurrent - 1,
                General = new GeneralSettings { Architecture = WinPeArchitecture.Arm64, WinPeLanguage = "fr-FR" }
            },
            Secrets = new Dictionary<string, string> { ["wifi/passphrase"] = "wifi-secret" },
            Contents = [new PackageContent(FileHash, SharedContentKind.File, file.Length, "profile.xml")],
            Files = new Dictionary<string, byte[]> { [FileHash] = file }
        };

        PackageReadResult result = ConfigurationPackageCodec.Read(ConfigurationPackageCodec.Write(payload, Passphrase), Passphrase);

        Assert.Equal(PackageReadStatus.Ok, result.Status);
        Assert.Null(result.SignerKeyId);
        ConfigurationPackagePayload read = Assert.IsType<ConfigurationPackagePayload>(result.Payload);
        Assert.Equal(PackageKind.Export, read.Kind);
        Assert.Equal(payload.CreatedUtc, read.CreatedUtc);
        Assert.Equal(payload.Sequence, read.Sequence);
        Assert.Equal(ConfigurationSchemaVersions.FoundryCurrent, read.Configuration.SchemaVersion);
        Assert.Equal(WinPeArchitecture.Arm64, read.Configuration.General.Architecture);
        Assert.Equal("fr-FR", read.Configuration.General.WinPeLanguage);
        Assert.Equal("wifi-secret", Assert.Contains("wifi/passphrase", read.Secrets!));
        Assert.Equal(payload.Contents, read.Contents);
        Assert.Equal(file, Assert.Contains(FileHash, read.Files!));
        Assert.Null(read.MasterKey);
        Assert.Null(read.Publisher);
    }

    [Fact]
    public void Read_WrongPassphrase_ReturnsWrongPassword()
    {
        byte[] package = ConfigurationPackageCodec.Write(CreateExport(), Passphrase);

        PackageReadResult result = ConfigurationPackageCodec.Read(package, "another-passphrase");

        Assert.Equal(PackageReadStatus.WrongPassword, result.Status);
        Assert.Null(result.Payload);
    }

    [Fact]
    public void Read_FlippedCiphertextByte_ReturnsTampered()
    {
        using PublicationSigningKey signer = PublicationSigningKey.Create();
        byte[] package = Edit(ConfigurationPackageCodec.Write(CreatePublication(signer), Passphrase, signer), FlipCiphertextByte);

        PackageReadResult result = ConfigurationPackageCodec.Read(package, Passphrase, signer.PublicKey);

        Assert.Equal(PackageReadStatus.Tampered, result.Status);
        Assert.Null(result.Payload);
    }

    [Fact]
    public void Read_FlippedCiphertextByteWithoutSignature_ReturnsWrongPassword()
    {
        // AES-GCM has one tag: without a signature, a modified ciphertext cannot be told apart from a wrong passphrase.
        byte[] package = Edit(ConfigurationPackageCodec.Write(CreateExport(), Passphrase), FlipCiphertextByte);

        Assert.Equal(PackageReadStatus.WrongPassword, ConfigurationPackageCodec.Read(package, Passphrase).Status);
    }

    [Fact]
    public void Read_ChangedIterationsInEnvelope_ReturnsTampered()
    {
        using PublicationSigningKey signer = PublicationSigningKey.Create();
        byte[] package = Edit(
            ConfigurationPackageCodec.Write(CreatePublication(signer), Passphrase, signer),
            envelope => envelope["kdf"]!["iterations"] = 600_001);

        Assert.Equal(PackageReadStatus.Tampered, ConfigurationPackageCodec.Read(package, Passphrase, signer.PublicKey).Status);
    }

    [Theory]
    [InlineData(599_999)]
    [InlineData(10_000_001)]
    public void Read_IterationsBelowMinimumOrAboveMaximum_ReturnsInvalid(int iterations)
    {
        byte[] package = Edit(ConfigurationPackageCodec.Write(CreateExport(), Passphrase), envelope => envelope["kdf"]!["iterations"] = iterations);

        Assert.Equal(PackageReadStatus.Invalid, ConfigurationPackageCodec.Read(package, Passphrase).Status);
    }

    [Fact]
    public void Read_UnknownPayloadMember_IsIgnored()
    {
        JsonObject payload = JsonSerializer.SerializeToNode(CreateExport(), ConfigurationPackageJson.SerializerOptions)!.AsObject();
        payload["addedByNewerFoundry"] = new JsonObject { ["value"] = 1 };
        byte[] package = ConfigurationPackageCodec.Seal(Encoding.UTF8.GetBytes(payload.ToJsonString()), Passphrase, signer: null);

        PackageReadResult result = ConfigurationPackageCodec.Read(package, Passphrase);

        Assert.Equal(PackageReadStatus.Ok, result.Status);
        Assert.Equal(42, result.Payload!.Sequence);
    }

    [Fact]
    public void Read_NewerEnvelopeVersion_ReturnsUnsupported()
    {
        byte[] package = Edit(ConfigurationPackageCodec.Write(CreateExport(), Passphrase), envelope => envelope["version"] = 2);

        Assert.Equal(PackageReadStatus.Unsupported, ConfigurationPackageCodec.Read(package, Passphrase).Status);
    }

    [Fact]
    public void Read_NewerConfigurationSchema_ReturnsUnsupported()
    {
        ConfigurationPackagePayload payload = CreateExport() with
        {
            Configuration = new FoundryConfigurationDocument { SchemaVersion = ConfigurationSchemaVersions.FoundryCurrent + 1 }
        };

        PackageReadResult result = ConfigurationPackageCodec.Read(ConfigurationPackageCodec.Write(payload, Passphrase), Passphrase);

        Assert.Equal(PackageReadStatus.Unsupported, result.Status);
        Assert.Null(result.Payload);
    }

    [Fact]
    public void Read_SignedWithExpectedKey_ReturnsOkWithKeyId()
    {
        using PublicationSigningKey signer = PublicationSigningKey.Create();
        byte[] masterKey = new byte[32];
        ConfigurationPackagePayload payload = CreatePublication(signer) with { MasterKey = masterKey };

        byte[] package = ConfigurationPackageCodec.Write(payload, Passphrase, signer);

        PackageReadResult result = ConfigurationPackageCodec.Read(package, Passphrase, signer.PublicKey);

        Assert.Equal(PackageReadStatus.Ok, result.Status);
        Assert.Equal(signer.KeyId, result.SignerKeyId);
        Assert.Equal(PackageKind.Publication, result.Payload!.Kind);
        Assert.Equal(signer.PublicKey, result.Payload.Publisher!.PublicKey);
        Assert.Equal(masterKey, result.Payload.MasterKey);
        PackageReadResult unpinned = ConfigurationPackageCodec.Read(package, Passphrase);
        Assert.Equal(PackageReadStatus.Ok, unpinned.Status);
        Assert.Equal(signer.KeyId, unpinned.SignerKeyId);
    }

    [Fact]
    public void Read_SignedWithOtherKey_ReturnsInvalidSignatureWithFoundKeyId()
    {
        using PublicationSigningKey signer = PublicationSigningKey.Create();
        using PublicationSigningKey pinned = PublicationSigningKey.Create();
        byte[] package = ConfigurationPackageCodec.Write(CreatePublication(signer), Passphrase, signer);

        PackageReadResult result = ConfigurationPackageCodec.Read(package, Passphrase, pinned.PublicKey);

        Assert.Equal(PackageReadStatus.InvalidSignature, result.Status);
        Assert.Equal(signer.KeyId, result.SignerKeyId);
        Assert.Null(result.Payload);
    }

    [Fact]
    public void Read_UnsignedWhenKeyExpected_ReturnsInvalidSignature()
    {
        using PublicationSigningKey pinned = PublicationSigningKey.Create();
        byte[] package = ConfigurationPackageCodec.Write(CreateExport(), Passphrase);

        PackageReadResult result = ConfigurationPackageCodec.Read(package, Passphrase, pinned.PublicKey);

        Assert.Equal(PackageReadStatus.InvalidSignature, result.Status);
        Assert.Null(result.SignerKeyId);
    }

    [Fact]
    public void Write_WeakPassphrase_Throws()
    {
        Assert.Throws<ArgumentException>(() => ConfigurationPackageCodec.Write(CreateExport(), "short"));
        Assert.Throws<ArgumentException>(() => ConfigurationPackageCodec.Write(CreateExport(), new string('x', 16)));
    }

    [Fact]
    public void IsPackage_LegacyMagic_ReturnsFalse()
    {
        byte[] legacy = [.. "FNDRYPRF"u8, 1, 1, 0, 0, 0, 0];

        Assert.False(ConfigurationPackageCodec.IsPackage(legacy));
        Assert.True(ConfigurationPackageCodec.IsPackage(ConfigurationPackageCodec.Write(CreateExport(), Passphrase)));
    }

    private static ConfigurationPackagePayload CreateExport() => new()
    {
        Kind = PackageKind.Export,
        CreatedUtc = new DateTimeOffset(2026, 10, 23, 8, 30, 0, TimeSpan.Zero),
        Sequence = 42,
        Configuration = new FoundryConfigurationDocument()
    };

    private static ConfigurationPackagePayload CreatePublication(PublicationSigningKey signer) => CreateExport() with
    {
        Kind = PackageKind.Publication,
        Publisher = new PackagePublisher(Guid.NewGuid(), "REFERENCE-PC", signer.KeyId, signer.PublicKey)
    };

    private static void FlipCiphertextByte(JsonObject envelope)
    {
        byte[] ciphertext = Convert.FromBase64String(envelope["ciphertext"]!.GetValue<string>());
        ciphertext[0] ^= 0x01;
        envelope["ciphertext"] = Convert.ToBase64String(ciphertext);
    }

    private static byte[] Edit(byte[] package, Action<JsonObject> edit)
    {
        JsonObject envelope = JsonNode.Parse(package)!.AsObject();
        edit(envelope);
        return Encoding.UTF8.GetBytes(envelope.ToJsonString());
    }
}
