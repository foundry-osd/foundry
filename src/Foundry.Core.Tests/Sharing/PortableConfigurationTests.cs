// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Models.Configuration;
using Foundry.Core.Services.Sharing;
using Foundry.Telemetry;

namespace Foundry.Core.Tests.Sharing;

public sealed class PortableConfigurationTests
{
    private const string FileHash = "1111111111111111111111111111111111111111111111111111111111111111";
    private const string EnabledPackageHash = "2222222222222222222222222222222222222222222222222222222222222222";
    private const string DisabledPackageHash = "3333333333333333333333333333333333333333333333333333333333333333";
    private const string IncludedSetHash = "4444444444444444444444444444444444444444444444444444444444444444";
    private const string ExcludedSetHash = "5555555555555555555555555555555555555555555555555555555555555555";
    private const string IncludedImageHash = "6666666666666666666666666666666666666666666666666666666666666666";
    private const string ExcludedImageHash = "7777777777777777777777777777777777777777777777777777777777777777";

    [Fact]
    public void Strip_RemovesPerPcValues()
    {
        FoundryConfigurationDocument configuration = WithPerPcValues(new FoundryConfigurationDocument
        {
            General = new GeneralSettings { WinPeLanguage = "fr-FR" }
        }, "C:\\Media\\foundry.iso", "D:\\WinPeDrivers", "install-1");

        FoundryConfigurationDocument stripped = PortableConfiguration.Strip(configuration);

        Assert.Null(stripped.General.IsoOutputPath);
        Assert.Null(stripped.General.CustomDriverDirectoryPath);
        Assert.Equal(new TelemetrySettings(), stripped.Telemetry);
        Assert.Equal("fr-FR", stripped.General.WinPeLanguage);
    }

    [Fact]
    public void ApplyLocal_KeepsLocalPerPcValues()
    {
        FoundryConfigurationDocument incoming = WithPerPcValues(new FoundryConfigurationDocument
        {
            General = new GeneralSettings { WinPeLanguage = "de-DE" }
        }, "C:\\Reference\\foundry.iso", "C:\\ReferenceDrivers", "reference-install");
        FoundryConfigurationDocument local = WithPerPcValues(new FoundryConfigurationDocument(), "E:\\Local\\foundry.iso", "E:\\LocalDrivers", "local-install");

        FoundryConfigurationDocument applied = PortableConfiguration.ApplyLocal(incoming, local);

        Assert.Equal("E:\\Local\\foundry.iso", applied.General.IsoOutputPath);
        Assert.Equal("E:\\LocalDrivers", applied.General.CustomDriverDirectoryPath);
        Assert.Equal(local.Telemetry, applied.Telemetry);
        Assert.Equal("de-DE", applied.General.WinPeLanguage);
    }

    [Fact]
    public void ListContents_IncludesOnlyIncludedImagesEnabledPackagesAndIncludedDriverSets()
    {
        FoundryConfigurationDocument configuration = CreateConfigurationWithContent(customDriversEnabled: true);

        IReadOnlyList<PackageContent> contents = PortableConfiguration.ListContents(configuration);

        Assert.Equal(
            [(SharedContentKind.File, FileHash), (SharedContentKind.Package, EnabledPackageHash), (SharedContentKind.DriverSet, IncludedSetHash),
                (SharedContentKind.Image, IncludedImageHash)],
            contents.Select(content => (content.Kind, content.Hash)).OrderBy(item => item.Kind).ToArray());
        PackageContent file = Assert.Single(contents, content => content.Kind == SharedContentKind.File);
        Assert.Equal(new PackageContent(FileHash, SharedContentKind.File, 12, "profile.xml"), file);
    }

    [Fact]
    public void ListContents_DisabledCustomDrivers_ListsNoDriverSet()
    {
        FoundryConfigurationDocument configuration = CreateConfigurationWithContent(customDriversEnabled: false);

        IReadOnlyList<PackageContent> contents = PortableConfiguration.ListContents(configuration);

        Assert.DoesNotContain(contents, content => content.Kind == SharedContentKind.DriverSet);
        Assert.Contains(contents, content => content.Kind == SharedContentKind.Package);
    }

    private static FoundryConfigurationDocument WithPerPcValues(FoundryConfigurationDocument configuration, string isoPath, string driverPath, string installId) =>
        configuration with
        {
            General = configuration.General with { IsoOutputPath = isoPath, CustomDriverDirectoryPath = driverPath },
            Telemetry = new TelemetrySettings { IsEnabled = false, IsRemoteDiagnosticsEnabled = false, InstallId = installId }
        };

    private static FoundryConfigurationDocument CreateConfigurationWithContent(bool customDriversEnabled) => new()
    {
        Network = new NetworkSettings
        {
            Dot1x = new Dot1xSettings
            {
                IsEnabled = true,
                ProfileTemplate = new AuthoringFileReference { ContentHash = FileHash, FileName = "profile.xml", Length = 12 }
            }
        },
        PreOobe = new PreOobeSettings
        {
            IsEnabled = true,
            Actions =
            [
                new PreOobeActionSettings { Name = "Enabled", Package = Package(EnabledPackageHash) },
                new PreOobeActionSettings { Name = "Disabled", IsEnabled = false, Package = Package(DisabledPackageHash) }
            ]
        },
        CustomDrivers = new CustomDriverSetsSettings
        {
            IsEnabled = customDriversEnabled,
            Sets =
            [
                new CustomDriverSetReference { Id = "included", DisplayName = "Included", Package = Package(IncludedSetHash) },
                new CustomDriverSetReference { Id = "excluded", DisplayName = "Excluded", Package = Package(ExcludedSetHash), IsIncluded = false }
            ]
        },
        CustomImages = new CustomImagesSettings
        {
            IsEnabled = true,
            Images =
            [
                new CustomImageReference { Id = "included", ContentHash = IncludedImageHash, DisplayName = "Included", Length = 100 },
                new CustomImageReference { Id = "excluded", ContentHash = ExcludedImageHash, DisplayName = "Excluded", Length = 100, IsIncluded = false }
            ]
        }
    };

    private static ContentPackageReference Package(string hash) => new() { ContentHash = hash, DisplayName = "Package " + hash[..4], Length = 10, FileCount = 1 };
}
