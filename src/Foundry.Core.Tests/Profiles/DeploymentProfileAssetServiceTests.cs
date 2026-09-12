// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Security.Cryptography;
using Foundry.Core.Models.Configuration;
using Foundry.Core.Models.Profiles;
using Foundry.Core.Services.Profiles;

namespace Foundry.Core.Tests.Profiles;

public sealed class DeploymentProfileAssetServiceTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "Foundry.ProfileAssets.Tests", Guid.NewGuid().ToString("N"));

    public DeploymentProfileAssetServiceTests() => Directory.CreateDirectory(root);

    [Fact]
    public void Capture_WhenBuildIsStrict_SkipsInactiveMissingAndChangedSources()
    {
        string missing = Path.Combine(root, "missing.pfx");
        string changedAnswer = Path.Combine(root, "changed.xml");
        File.WriteAllText(changedAnswer, "changed");
        FoundryConfigurationDocument configuration = new()
        {
            Network = new()
            {
                Dot1x = new() { ProfileTemplatePath = missing, CertificatePath = missing, RequiresCertificate = true },
                WifiProvisioned = false,
                Wifi = new() { IsEnabled = true, HasEnterpriseProfile = true, EnterpriseProfileTemplatePath = missing, CertificatePath = missing, RequiresCertificate = true }
            },
            Autopilot = new()
            {
                IsEnabled = true,
                ProvisioningMode = AutopilotProvisioningMode.JsonProfile,
                HardwareHashUpload = new() { BootMediaCertificate = new() { PfxPath = missing } }
            },
            Unattend = new()
            {
                Files = [new() { Id = "answer", SourcePath = changedAnswer, ContentHash = Convert.ToHexString(SHA256.HashData("original"u8)) }]
            }
        };

        Assert.Empty(DeploymentProfileAssetService.Capture(configuration, true, requireContent: true));
    }

    [Fact]
    public void Capture_WhenProfileIncludesDormantSource_PreservesItsBytes()
    {
        string source = Path.Combine(root, "dormant.pfx");
        File.WriteAllBytes(source, [1, 2, 3]);
        FoundryConfigurationDocument configuration = new() { Network = new() { Dot1x = new() { CertificatePath = source } } };
        IReadOnlyList<DeploymentProfileAsset> assets = DeploymentProfileAssetService.Capture(configuration, true);
        try { Assert.Equal(new byte[] { 1, 2, 3 }, Assert.Single(assets).Content); }
        finally { DeploymentProfileAssetService.Clear(assets); }
    }

    [Fact]
    public void Capture_WhenActiveTransportHasOptionalCertificate_PreservesBuildInput()
    {
        string source = Path.Combine(root, "selected.bin");
        File.WriteAllBytes(source, [1, 2, 3]);
        FoundryConfigurationDocument configuration = new()
        {
            Network = new() { Dot1x = new() { IsEnabled = true, ProfileTemplatePath = source, CertificatePath = source, RequiresCertificate = false } }
        };
        IReadOnlyList<DeploymentProfileAsset> assets = DeploymentProfileAssetService.Capture(configuration, true, requireContent: true);
        try { Assert.Equal(new byte[] { 1, 2, 3 }, Assert.Single(assets, asset => asset.Kind == ProfileAssetKind.WiredCertificate).Content); }
        finally { DeploymentProfileAssetService.Clear(assets); }
    }

    [Fact]
    public void Capture_WhenProvisionedWifiIsActive_StillRequiresSelectedEnterpriseSource()
    {
        FoundryConfigurationDocument configuration = new()
        {
            Network = new()
            {
                WifiProvisioned = true,
                Wifi = new() { IsEnabled = true, HasEnterpriseProfile = true, EnterpriseProfileTemplatePath = Path.Combine(root, "missing.xml") }
            }
        };
        Assert.Throws<FileNotFoundException>(() => DeploymentProfileAssetService.Capture(configuration, true, requireContent: true));
    }

    [Fact]
    public void Capture_WhenExcludedSourceIsUnavailable_KeepsOmissionWithoutClaimingVerifiedContent()
    {
        var configuration = new FoundryConfigurationDocument
        {
            Network = new NetworkSettings { Dot1x = new Dot1xSettings { CertificatePath = Path.Combine(root, "missing.pfx") } }
        };
        DeploymentProfileAsset asset = Assert.Single(DeploymentProfileAssetService.Capture(configuration, false));
        Assert.Equal(ProfileValueState.Omitted, asset.State);
        Assert.Null(asset.Content);
        Assert.Null(asset.Sha256);
    }

    [Fact]
    public void Capture_WhenContentIsExcluded_PreservesVerifiedFingerprintAndPasswordContext()
    {
        string source = Path.Combine(root, "certificate.pfx");
        File.WriteAllBytes(source, [1, 2, 3]);
        FoundryConfigurationDocument configuration = new() { Network = new() { Dot1x = new() { CertificatePath = source } } };
        DeploymentProfileDocument omitted = new() { Configuration = configuration, Assets = DeploymentProfileAssetService.Capture(configuration, false) };
        DeploymentProfileDocument included = new() { Configuration = configuration, Assets = DeploymentProfileAssetService.Capture(configuration, true) };
        try
        {
            DeploymentProfileAsset asset = Assert.Single(omitted.Assets);
            Assert.Equal(ProfileValueState.Omitted, asset.State);
            Assert.Null(asset.Content);
            Assert.Equal(Convert.ToHexString(SHA256.HashData(new byte[] { 1, 2, 3 })), asset.Sha256);
            Assert.Equal(DeploymentProfileSecretBinding.Identity(ProfileSecretPurpose.WiredCertificatePassword, included),
                DeploymentProfileSecretBinding.Identity(ProfileSecretPurpose.WiredCertificatePassword, omitted));
            File.WriteAllBytes(source, [3, 2, 1]);
            Assert.NotEqual(asset.Sha256, Assert.Single(DeploymentProfileAssetService.Capture(configuration, false)).Sha256);
        }
        finally { DeploymentProfileSecretBinding.Clear(included); }
    }

    [Fact]
    public void Capture_WhenExcludedSourceExceedsLimit_RejectsBeforeReadingContent()
    {
        string source = Path.Combine(root, "large.pfx");
        using (FileStream stream = File.Create(source)) stream.SetLength(DeploymentProfileAssetService.MaximumAssetBytes + 1);
        FoundryConfigurationDocument configuration = new() { Network = new() { Dot1x = new() { CertificatePath = source } } };
        Assert.Throws<InvalidDataException>(() => DeploymentProfileAssetService.Capture(configuration, false));
    }

    [Fact]
    public void Materialize_UsesGeneratedPathsAndOriginalCapturedBytes()
    {
        string source = Path.Combine(root, "input.xml");
        File.WriteAllText(source, "<profile>first</profile>");
        FoundryConfigurationDocument configuration = new()
        {
            Network = new NetworkSettings { Dot1x = new Dot1xSettings { ProfileTemplatePath = source } }
        };
        IReadOnlyList<DeploymentProfileAsset> assets = DeploymentProfileAssetService.Capture(configuration, true);
        File.WriteAllText(source, "<profile>second</profile>");
        DeploymentProfileDocument profile = new()
        {
            Configuration = configuration,
            Assets = [assets[0] with { RelativePath = "../../outside.xml" }]
        };
        string staging = Path.Combine(root, "staging");
        FoundryConfigurationDocument restored = DeploymentProfileAssetService.Materialize(profile, staging);
        Assert.StartsWith(staging + Path.DirectorySeparatorChar, restored.Network.Dot1x.ProfileTemplatePath!);
        Assert.Equal("<profile>first</profile>", File.ReadAllText(restored.Network.Dot1x.ProfileTemplatePath!));
        DeploymentProfileAssetService.Clear(assets);
        Assert.All(assets[0].Content!, value => Assert.Equal(0, value));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Capture_WhenAnswerFileHasChanged_RequiresExplicitRefresh(bool includeContent)
    {
        string source = Path.Combine(root, "answer.xml");
        File.WriteAllText(source, "changed");
        var configuration = new FoundryConfigurationDocument
        {
            Unattend = new UnattendSettings
            {
                Files = [new UnattendFileSettings { Id = Guid.NewGuid().ToString("N"), SourcePath = source, ContentHash = Convert.ToHexString(SHA256.HashData("original"u8)) }]
            }
        };
        Assert.Throws<InvalidDataException>(() => DeploymentProfileAssetService.Capture(configuration, includeContent));
    }

    [Fact]
    public void Materialize_WhenDigestDoesNotMatch_RejectsBeforeWritingContent()
    {
        var profile = new DeploymentProfileDocument
        {
            Assets = [new DeploymentProfileAsset
            {
                Id = "wired-profile", Kind = ProfileAssetKind.WiredProfile, RelativePath = "profile.xml",
                State = ProfileValueState.Present, Content = [1, 2, 3], Sha256 = new string('0', 64)
            }]
        };
        string staging = Path.Combine(root, "invalid");
        Assert.Throws<InvalidDataException>(() => DeploymentProfileAssetService.Materialize(profile, staging));
        Assert.Empty(Directory.EnumerateFiles(staging));
    }

    [Fact]
    public void SecretBinding_ChangesForAccountRenameButNotForLocalAssetRemapping()
    {
        var profile = new DeploymentProfileDocument
        {
            Configuration = new FoundryConfigurationDocument
            {
                Customization = new CustomizationSettings
                {
                    Oobe = new OobeSettings { AdditionalAccounts = [new OobeAdditionalAccountSettings { Id = "account", UserName = "original" }] }
                }
            },
            Assets = [new DeploymentProfileAsset { Kind = ProfileAssetKind.WiredCertificate, Sha256 = new string('A', 64) }]
        };
        string original = DeploymentProfileSecretBinding.Identity(ProfileSecretPurpose.AdditionalAccountPassword, profile, "account");
        DeploymentProfileDocument renamed = profile with
        {
            Configuration = profile.Configuration with
            {
                Customization = profile.Configuration.Customization with
                {
                    Oobe = profile.Configuration.Customization.Oobe with { AdditionalAccounts = [new OobeAdditionalAccountSettings { Id = "account", UserName = "renamed" }] }
                }
            }
        };
        Assert.NotEqual(original, DeploymentProfileSecretBinding.Identity(ProfileSecretPurpose.AdditionalAccountPassword, renamed, "account"));
        Assert.Equal(DeploymentProfileSecretBinding.Identity(ProfileSecretPurpose.WiredCertificatePassword, profile),
            DeploymentProfileSecretBinding.Identity(ProfileSecretPurpose.WiredCertificatePassword, renamed));
    }

    public void Dispose() => Directory.Delete(root, true);
}
