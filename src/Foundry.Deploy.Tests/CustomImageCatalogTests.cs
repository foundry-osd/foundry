// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Security.Cryptography;
using System.Text.Json;
using Foundry.Core.Models.Configuration;
using Foundry.Core.Models.Configuration.Deploy;
using Foundry.Core.Models.Images;
using Foundry.Core.Services.Images;
using Foundry.Core.Services.WinPe;
using Foundry.Deploy.Services.Images;
using Foundry.Utilities.Storage;

namespace Foundry.Deploy.Tests;

public sealed class CustomImageCatalogTests
{
    [Theory]
    [InlineData(DriveType.Fixed)]
    [InlineData(DriveType.CDRom)]
    public async Task DiscoverAsync_ResolvesPreparedImagesWithoutOfferingUnreferencedContent(DriveType driveType)
    {
        string root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string source = Path.Combine(root, "source.wim");
            await File.WriteAllTextAsync(source, "image", TestContext.Current.CancellationToken);
            var library = new CustomImageLibraryService(Path.Combine(root, "library"), new MetadataReader());
            CustomImageReference image = await library.ImportAsync(new(source, "Managed image"), cancellationToken: TestContext.Current.CancellationToken);
            using WinPeCustomImageMediaLease package = await new WinPeCustomImageMediaService().PrepareAsync(library,
                new() { IsEnabled = true, Images = [image] }, TestContext.Current.CancellationToken);
            string media = Path.Combine(root, "media");
            string expectedImage = Path.Combine(media, "Cache", "OperatingSystems", "Custom", image.ContentHash, "image.wim");
            await new WinPeCustomImageMediaService().PublishAsync(package, media, TestContext.Current.CancellationToken);
            string unreferenced = Path.Combine(media, "Cache", "OperatingSystems", "Custom", new string('0', 64), "image.wim");
            Directory.CreateDirectory(Path.GetDirectoryName(unreferenced)!);
            await File.WriteAllTextAsync(unreferenced, "older image", TestContext.Current.CancellationToken);

            var service = new CustomImageCatalogService(new Volumes(media, driveType));
            var result = await service.DiscoverAsync(new DeployCustomImagesSettings
            {
                IsEnabled = true,
                ManifestId = package.ManifestId,
                ManifestHash = package.ManifestHash
            }, TestContext.Current.CancellationToken);

            Assert.Empty(result.ErrorKey);
            var discovered = Assert.Single(result.Images);
            Assert.True(discovered.IsManaged);
            Assert.Equal(expectedImage, discovered.ImagePath);
            Assert.Equal(image.Id, discovered.Id);
            Assert.Equal(image.ContentHash, discovered.ExpectedHash);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public async Task DiscoverAsync_OffersOnlyTopLevelManualWimsOnMatchingMedia()
    {
        string root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        try
        {
            string directory = Path.Combine(root, "Cache", "OperatingSystems", "Custom");
            Directory.CreateDirectory(Path.Combine(directory, "manifests"));
            Directory.CreateDirectory(Path.Combine(directory, "nested"));
            await File.WriteAllTextAsync(Path.Combine(root, "Cache", "OperatingSystems", "catalog.wim"), "catalog image", TestContext.Current.CancellationToken);
            await File.WriteAllTextAsync(Path.Combine(directory, "manual.wim"), "wim", TestContext.Current.CancellationToken);
            await File.WriteAllTextAsync(Path.Combine(directory, "nested", "hidden.wim"), "wim", TestContext.Current.CancellationToken);
            byte[] manifest = JsonSerializer.SerializeToUtf8Bytes(new CustomImageMediaManifest { ManifestId = "build" });
            await File.WriteAllBytesAsync(Path.Combine(directory, "manifests", "build.json"), manifest, TestContext.Current.CancellationToken);
            var settings = new DeployCustomImagesSettings { IsEnabled = true, ManifestId = "build", ManifestHash = Convert.ToHexString(SHA256.HashData(manifest)) };
            var service = new CustomImageCatalogService(new Volumes(root));
            var result = await service.DiscoverAsync(settings, TestContext.Current.CancellationToken);
            Assert.Single(result.Images);
            Assert.Equal("manual", result.Images[0].DisplayName);
            Assert.False(result.Images[0].IsManaged);
            var corrupt = await service.DiscoverAsync(settings with { ManifestHash = new string('0', 64) }, TestContext.Current.CancellationToken);
            Assert.Empty(corrupt.Images);
            Assert.NotEmpty(corrupt.ErrorKey);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    private sealed class MetadataReader : ICustomImageMetadataReader
    {
        public Task<IReadOnlyList<CustomImageIndex>> ReadAsync(string path, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<CustomImageIndex>>([new() { Index = 4, Name = "Image", ExpandedSizeBytes = 1024 }]);
    }

    private sealed class Volumes(string root, DriveType driveType = DriveType.Fixed) : IVolumeDiscovery
    {
        public IReadOnlyList<VolumeInfo> GetVolumes() => [new(root, "Foundry Cache", driveType, true, long.MaxValue)];
    }
}
