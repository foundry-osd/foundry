// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Models.Configuration;
using Foundry.Core.Services.Configuration;
using Foundry.Core.Services.Images;
using Foundry.Core.Services.Profiles;
using Xunit;

namespace Foundry.Core.Tests.Images;

public sealed class CustomImageConfigurationTests
{
    [Fact]
    public void GeneratorPreservesIndependentSourceAndExactIndexDefaults()
    {
        var settings = Settings();
        var runtime = new DeployConfigurationGenerator().Generate(new() { CustomImages = settings });
        Assert.True(runtime.CustomImages.IsEnabled);
        Assert.Equal(CustomImageSource.Catalog, runtime.CustomImages.DefaultSource);
        Assert.Equal("custom", runtime.CustomImages.DefaultImageId);
        Assert.Equal(4, runtime.CustomImages.DefaultImageIndex);
        Assert.Null(runtime.CustomImages.ManifestHash);
    }

    [Fact]
    public void PortableProfilesRetainReferencesWithoutImageBytesOrHostPaths()
    {
        CustomImagesSettings settings = Settings();
        FoundryConfigurationDocument projected = DeploymentProfileProjection.CreatePortable(new() { CustomImages = settings });
        Assert.Equal(settings, projected.CustomImages);
        string json = new FoundryConfigurationService().Serialize(projected);
        Assert.DoesNotContain("sourcePath", json, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(new string('a', 64), json);
    }

    [Fact]
    public void InvalidProfileImageMetadataIsRejectedBeforeActivation()
    {
        CustomImagesSettings settings = Settings() with { Images = [Settings().Images[0] with { ContentHash = "../outside" }] };
        Assert.Throws<InvalidDataException>(() => DeploymentProfileProjection.CreatePortable(new() { CustomImages = settings }));
    }

    [Fact]
    public void DuplicateContentAndNullMetadataAreRejectedWithoutThrowingNullReference()
    {
        CustomImageReference image = Settings().Images[0];
        Assert.NotEmpty(CustomImageSettingsValidator.Validate(Settings() with { Images = [image, image with { Id = "other" }] }));
        Assert.False(CustomImageSettingsValidator.IsValidReference(image with { DisplayName = null! }));
        Assert.False(CustomImageSettingsValidator.IsValidReference(image with { Indexes = [new() { Index = 1, Languages = null! }] }));
    }

    [Fact]
    public void OlderAuthoringDocumentsKeepCatalogMode()
    {
        FoundryConfigurationDocument document = new FoundryConfigurationService().Deserialize("{\"schemaVersion\":16}");
        Assert.False(document.CustomImages.IsEnabled);
        Assert.Equal(CustomImageSource.Catalog, document.CustomImages.DefaultSource);
        Assert.Empty(document.CustomImages.Images);
    }

    [Theory]
    [InlineData("C:\\", "sources/install.wim", "C:\\sources\\install.wim")]
    [InlineData("C:\\library", "content/image.wim", "C:\\library\\content\\image.wim")]
    public void MediaPathsAcceptContainedFilesOnVolumeRoots(string root, string relative, string expected)
    {
        Assert.Equal(expected, CustomImagePathPolicy.ResolveRelativePath(root, relative));
    }

    [Theory]
    [InlineData("../image.wim")]
    [InlineData("managed/../../image.wim")]
    [InlineData("C:/image.wim")]
    [InlineData("image.wim:alternate")]
    public void MediaPathsRejectEscapesAndAlternateStreams(string path)
    {
        Assert.Throws<InvalidDataException>(() => CustomImagePathPolicy.ResolveRelativePath(Path.GetTempPath(), path));
    }

    private static CustomImagesSettings Settings() => new()
    {
        IsEnabled = true,
        DefaultSource = CustomImageSource.Catalog,
        DefaultImageId = "custom",
        DefaultImageIndex = 4,
        Images = [new()
        {
            Id = "custom", DisplayName = "Server with applications", ContentHash = new string('a', 64), Length = 100,
            Indexes = [new() { Index = 1, EditionId = "SameEdition" }, new() { Index = 4, EditionId = "SameEdition", Architecture = "x86" }]
        }]
    };
}
