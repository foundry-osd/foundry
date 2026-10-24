// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Foundry.Core.Models.Configuration;
using Foundry.Core.Services.Files;
using Foundry.Core.Services.Secrets;
using Foundry.Core.Services.Sharing;
using Foundry.Core.Tests.Configuration.Legacy;
using Foundry.Core.Tests.TestUtilities;

namespace Foundry.Core.Tests.Sharing;

public sealed class ConfigurationTransferTests : IDisposable
{
    private const string Passphrase = "correct-horse-battery";
    private const string PackageHash = "2222222222222222222222222222222222222222222222222222222222222222";
    private const string DriverSetHash = "4444444444444444444444444444444444444444444444444444444444444444";
    private const string ImageHash = "6666666666666666666666666666666666666666666666666666666666666666";
    private static readonly LegacyFixtureValues Legacy = LegacyFixtures.LoadValues();
    private readonly TemporaryDirectory temp = new();

    [Fact]
    public void Export_WithoutPasswords_HasNoSecrets()
    {
        var vault = new Dictionary<string, string> { [SecretIds.WifiPassphrase] = "wifi-secret" };

        ConfigurationPackagePayload payload = ExportAndRead(new FoundryConfigurationDocument(), vault, includePasswords: false);

        Assert.Null(payload.Secrets);
        Assert.Equal(PackageKind.Export, payload.Kind);
    }

    [Fact]
    public void Export_WithPasswords_ExcludesSharingInternalIds()
    {
        var vault = new Dictionary<string, string>
        {
            [SecretIds.WifiPassphrase] = "wifi-secret",
            [SecretIds.PublicationPassword] = "publication-secret",
            [SecretIds.SigningKey] = "signing-key",
            [SecretIds.MasterKey] = "master-key"
        };

        ConfigurationPackagePayload payload = ExportAndRead(new FoundryConfigurationDocument(), vault, includePasswords: true);

        Assert.Equal(new Dictionary<string, string> { [SecretIds.WifiPassphrase] = "wifi-secret" }, payload.Secrets);
    }

    [Fact]
    public void Export_EmbeddedFilesOver16MiB_Throws()
    {
        var library = new AuthoringFileLibrary(Path.Combine(temp.Path, "files"));
        AuthoringFileReference[] references = Enumerable.Range(1, 5)
            .Select(index => library.Import(Enumerable.Repeat((byte)index, AuthoringFileLibrary.MaximumFileBytes).ToArray(), $"file{index}.bin"))
            .ToArray();
        var configuration = new FoundryConfigurationDocument
        {
            Network = new NetworkSettings
            {
                Dot1x = new Dot1xSettings { ProfileTemplate = references[0], Certificate = references[1] },
                Wifi = new WifiSettings { EnterpriseProfileTemplate = references[2], Certificate = references[3] }
            },
            Autopilot = new AutopilotSettings
            {
                HardwareHashUpload = new AutopilotHardwareHashUploadSettings
                {
                    BootMediaCertificate = new AutopilotBootMediaCertificateSettings { Pfx = references[4] }
                }
            }
        };

        Assert.Throws<InvalidDataException>(() =>
            ConfigurationTransfer.Export(configuration, new Dictionary<string, string>(), library, Passphrase, includePasswords: false, "PC-1"));
    }

    [Fact]
    public void Export_DriverSets_AreListedButNeverEmbedded()
    {
        var library = new AuthoringFileLibrary(Path.Combine(temp.Path, "files"));
        AuthoringFileReference template = library.Import("<profile />"u8, "profile.xml");
        FoundryConfigurationDocument configuration = WithMissingContent(new FoundryConfigurationDocument
        {
            Network = new NetworkSettings { Dot1x = new Dot1xSettings { IsEnabled = true, ProfileTemplate = template } }
        });

        ConfigurationPackagePayload payload = ExportAndRead(configuration, new Dictionary<string, string>(), includePasswords: false, library);

        Assert.Contains(payload.Contents, content => content is { Kind: SharedContentKind.DriverSet, Hash: DriverSetHash });
        Assert.Contains(payload.Contents, content => content is { Kind: SharedContentKind.Package, Hash: PackageHash });
        Assert.Contains(payload.Contents, content => content is { Kind: SharedContentKind.Image, Hash: ImageHash });
        Assert.Equal([template.ContentHash], payload.Files!.Keys);
    }

    [Fact]
    public void Preview_LegacyExportFixture_ReturnsLegacyPreview()
    {
        PackageReadStatus status = ConfigurationTransfer.TryPreview(
            LegacyFixtures.Read(LegacyFixtures.ExportWithSecrets), Legacy.ExportPassphrase, out ImportPreview? preview);

        Assert.Equal(PackageReadStatus.Ok, status);
        Assert.NotNull(preview);
        Assert.True(preview.IsLegacy);
        Assert.False(preview.IsLegacyConnectionFile);
        Assert.True(preview.IncludesPasswords);
        Assert.Equal("Fixture export", preview.Name);
        Assert.Equal(Legacy.WifiPassphrase, preview.Payload.Secrets![SecretIds.WifiPassphrase]);
        AuthoringFileReference certificate = Assert.IsType<AuthoringFileReference>(preview.Payload.Configuration.Network.Dot1x.Certificate);
        Assert.Equal(Convert.FromBase64String(Legacy.WiredCertificateBase64), preview.Payload.Files![certificate.ContentHash]);
        Assert.Equal(Legacy.WiredCertificatePassword, preview.Payload.Secrets[SecretIds.Dot1xCertificate(certificate.ContentHash)]);
    }

    [Fact]
    public void Preview_LegacyConnectionFixture_FlagsConnectionFile()
    {
        PackageReadStatus status = ConfigurationTransfer.TryPreview(
            LegacyFixtures.Read(LegacyFixtures.Connection), Legacy.ConnectionPassphrase, out ImportPreview? preview);

        Assert.Equal(PackageReadStatus.Ok, status);
        Assert.NotNull(preview);
        Assert.True(preview.IsLegacy);
        Assert.True(preview.IsLegacyConnectionFile);
        Assert.Equal(Legacy.WifiSsid, preview.Payload.Configuration.Network.Wifi.Ssid);
        Assert.DoesNotContain(preview.Payload.Secrets ?? new Dictionary<string, string>(), entry => entry.Value == Legacy.ConnectionKeyBase64);
    }

    [Fact]
    public void Preview_NestedNullSection_ReturnsInvalid()
    {
        JsonObject payload = JsonSerializer.SerializeToNode(
            new ConfigurationPackagePayload { Kind = PackageKind.Export, Configuration = new FoundryConfigurationDocument() },
            ConfigurationPackageJson.SerializerOptions)!.AsObject();
        payload["configuration"]!["schemaVersion"] = 13;
        payload["configuration"]!["customization"]!["machineNaming"] = null;
        byte[] package = ConfigurationPackageCodec.Seal(Encoding.UTF8.GetBytes(payload.ToJsonString()), Passphrase, signer: null);

        PackageReadStatus status = ConfigurationTransfer.TryPreview(package, Passphrase, out ImportPreview? preview);

        Assert.Equal(PackageReadStatus.Invalid, status);
        Assert.Null(preview);
    }

    [Fact]
    public void Apply_ImportsFilesIntoLibraryAndMergesPasswords()
    {
        var source = new AuthoringFileLibrary(Path.Combine(temp.Path, "source"));
        AuthoringFileReference certificate = source.Import("certificate-bytes"u8, "wired.pfx");
        var reference = new FoundryConfigurationDocument
        {
            General = new GeneralSettings { IsoOutputPath = "C:\\Reference\\foundry.iso", DeploymentProtection = new DeploymentProtectionSettings { IsEnabled = true } },
            Network = new NetworkSettings { Dot1x = new Dot1xSettings { IsEnabled = true, RequiresCertificate = true, Certificate = certificate } }
        };
        var referenceVault = new Dictionary<string, string>
        {
            [SecretIds.Dot1xCertificate(certificate.ContentHash)] = "pfx-secret",
            [SecretIds.WifiPassphrase] = "reference-wifi"
        };
        byte[] file = ConfigurationTransfer.Export(reference, referenceVault, source, Passphrase, includePasswords: true, "REFERENCE-PC");
        Assert.Equal(PackageReadStatus.Ok, ConfigurationTransfer.TryPreview(file, Passphrase, out ImportPreview? preview));
        var target = new AuthoringFileLibrary(Path.Combine(temp.Path, "target"));
        var local = new FoundryConfigurationDocument { General = new GeneralSettings { IsoOutputPath = "E:\\Local\\foundry.iso" } };
        var localVault = new Dictionary<string, string>
        {
            [SecretIds.DeploymentProtection] = "local-protection",
            [SecretIds.WifiPassphrase] = "local-wifi"
        };

        ImportOutcome outcome = ConfigurationTransfer.Apply(preview!, local, localVault, target, _ => true, _ => true, _ => true);

        Assert.Equal("REFERENCE-PC", preview!.Name);
        Assert.True(target.IsAvailable(outcome.Configuration.Network.Dot1x.Certificate!));
        Assert.Equal("certificate-bytes"u8.ToArray(), target.ReadVerified(outcome.Configuration.Network.Dot1x.Certificate!));
        Assert.Equal("E:\\Local\\foundry.iso", outcome.Configuration.General.IsoOutputPath);
        Assert.Equal("pfx-secret", outcome.VaultEntries[SecretIds.Dot1xCertificate(certificate.ContentHash)]);
        Assert.Equal("reference-wifi", outcome.VaultEntries[SecretIds.WifiPassphrase]);
        Assert.Equal("local-protection", outcome.VaultEntries[SecretIds.DeploymentProtection]);
        Assert.Empty(outcome.MissingContent);
    }

    [Fact]
    public void Apply_MissingPackagesDriverSetsAndImages_ReportsMissingContent()
    {
        var source = new AuthoringFileLibrary(Path.Combine(temp.Path, "source"));
        FoundryConfigurationDocument configuration = WithMissingContent(new FoundryConfigurationDocument());
        byte[] file = ConfigurationTransfer.Export(configuration, new Dictionary<string, string>(), source, Passphrase, includePasswords: false, "PC-1");
        Assert.Equal(PackageReadStatus.Ok, ConfigurationTransfer.TryPreview(file, Passphrase, out ImportPreview? preview));

        ImportOutcome outcome = ConfigurationTransfer.Apply(preview!, new FoundryConfigurationDocument(), new Dictionary<string, string>(),
            new AuthoringFileLibrary(Path.Combine(temp.Path, "target")), _ => false, _ => false, _ => false);

        Assert.Equal(
            [(SharedContentKind.Package, PackageHash), (SharedContentKind.DriverSet, DriverSetHash), (SharedContentKind.Image, ImageHash)],
            outcome.MissingContent.Select(content => (content.Kind, content.Hash)).OrderBy(item => item.Kind).ToArray());
        Assert.Equal(DriverSetHash, Assert.Single(outcome.Configuration.CustomDrivers.Sets).Package.ContentHash);
        Assert.Equal(PackageHash, Assert.Single(outcome.Configuration.PreOobe.Actions).Package!.ContentHash);
        Assert.Equal(ImageHash, Assert.Single(outcome.Configuration.CustomImages.Images).ContentHash);
    }

    public void Dispose() => temp.Dispose();

    private ConfigurationPackagePayload ExportAndRead(
        FoundryConfigurationDocument configuration,
        IReadOnlyDictionary<string, string> vault,
        bool includePasswords,
        AuthoringFileLibrary? library = null)
    {
        library ??= new AuthoringFileLibrary(Path.Combine(temp.Path, "files"));
        byte[] file = ConfigurationTransfer.Export(configuration, vault, library, Passphrase, includePasswords, "PC-1");
        PackageReadResult result = ConfigurationPackageCodec.Read(file, Passphrase);
        Assert.Equal(PackageReadStatus.Ok, result.Status);
        return result.Payload!;
    }

    private static FoundryConfigurationDocument WithMissingContent(FoundryConfigurationDocument configuration) => configuration with
    {
        PreOobe = new PreOobeSettings
        {
            IsEnabled = true,
            Actions = [new PreOobeActionSettings { Name = "Install", Package = Package(PackageHash) }]
        },
        CustomDrivers = new CustomDriverSetsSettings
        {
            IsEnabled = true,
            Sets = [new CustomDriverSetReference { Id = "set", DisplayName = "Drivers", Package = Package(DriverSetHash) }],
            DefaultSetId = "set"
        },
        CustomImages = new CustomImagesSettings
        {
            IsEnabled = true,
            Images = [new CustomImageReference { Id = "image", ContentHash = ImageHash, DisplayName = "Image", Length = 100 }]
        }
    };

    private static ContentPackageReference Package(string hash) =>
        new() { ContentHash = hash, DisplayName = "Package " + hash[..4], Length = 10, FileCount = 1 };
}
