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
using Foundry.Deploy.Services.Cache;
using Foundry.Deploy.Services.Images;
using Foundry.Utilities.Networking;
using Foundry.Utilities.Storage;

namespace Foundry.Deploy.Tests;

public sealed class CustomImageCatalogTests
{
    [Theory]
    [InlineData(DriveType.Fixed, "Foundry Cache")]
    [InlineData(DriveType.CDRom, "FOUNDRY_ISO")]
    [InlineData(DriveType.Removable, "FOUNDRY")]
    public async Task DiscoverAsync_ResolvesPreparedImagesWithoutOfferingUnreferencedContent(DriveType driveType, string label)
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

            var service = new CustomImageCatalogService(new Volumes(media, driveType, label));
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

    [Fact]
    public async Task DiscoverAsync_FindsImagesPublishedOnlyOnTheNetworkCacheShare()
    {
        string root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        try
        {
            string share = Path.Combine(root, "share");
            var (settings, image, published) = await PublishImageAsync(root);
            CopyTree(published, Path.Combine(share, "Foundry"));
            using NetworkCacheSession session = NetworkCacheSession.Connected(share, canWrite: false, new NoopConnector());

            var service = new CustomImageCatalogService(new NoVolumes(), new NetworkCacheSessionAccessor { Current = session });
            var result = await service.DiscoverAsync(settings, TestContext.Current.CancellationToken);

            Assert.Empty(result.ErrorKey);
            var discovered = Assert.Single(result.Images);
            Assert.Equal(image.Id, discovered.Id);
            Assert.Equal(Path.Combine(share, "Foundry", "Cache", "OperatingSystems", "Custom", image.ContentHash, "image.wim"), discovered.ImagePath);
            Assert.False(discovered.IsOptical);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public async Task DiscoverAsync_NeverOffersManualWimsDroppedOnTheNetworkCacheShare()
    {
        string root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        try
        {
            string share = Path.Combine(root, "share");
            var (settings, image, published) = await PublishImageAsync(root);
            CopyTree(published, Path.Combine(share, "Foundry"));
            await File.WriteAllTextAsync(Path.Combine(share, "Foundry", "Cache", "OperatingSystems", "Custom", "planted.wim"), "wim",
                TestContext.Current.CancellationToken);
            using NetworkCacheSession session = NetworkCacheSession.Connected(share, canWrite: false, new NoopConnector());

            var service = new CustomImageCatalogService(new NoVolumes(), new NetworkCacheSessionAccessor { Current = session });
            var result = await service.DiscoverAsync(settings, TestContext.Current.CancellationToken);

            Assert.Equal(image.Id, Assert.Single(result.Images).Id);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public async Task DiscoverAsync_KeepsLocalImagesWhenTheShareManifestIsInvalid()
    {
        string root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        try
        {
            string share = Path.Combine(root, "share");
            string media = Path.Combine(root, "media");
            var (settings, image, published) = await PublishImageAsync(root);
            CopyTree(published, media);
            CopyTree(published, Path.Combine(share, "Foundry"));
            await File.WriteAllTextAsync(
                Path.Combine(share, "Foundry", "Cache", "OperatingSystems", "Custom", "manifests", settings.ManifestId + ".json"), "{}",
                TestContext.Current.CancellationToken);
            using NetworkCacheSession session = NetworkCacheSession.Connected(share, canWrite: false, new NoopConnector());

            var service = new CustomImageCatalogService(new Volumes(media), new NetworkCacheSessionAccessor { Current = session });
            var result = await service.DiscoverAsync(settings, TestContext.Current.CancellationToken);

            Assert.Empty(result.ErrorKey);
            Assert.Equal(image.Id, Assert.Single(result.Images).Id);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public async Task DiscoverAsync_PrefersLocalMediaOverTheNetworkCacheShare()
    {
        string root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        try
        {
            string share = Path.Combine(root, "share");
            string media = Path.Combine(root, "media");
            var (settings, image, published) = await PublishImageAsync(root);
            CopyTree(published, media);
            CopyTree(published, Path.Combine(share, "Foundry"));
            using NetworkCacheSession session = NetworkCacheSession.Connected(share, canWrite: false, new NoopConnector());

            var service = new CustomImageCatalogService(new Volumes(media), new NetworkCacheSessionAccessor { Current = session });
            var result = await service.DiscoverAsync(settings, TestContext.Current.CancellationToken);

            Assert.Empty(result.ErrorKey);
            var discovered = Assert.Single(result.Images);
            Assert.Equal(Path.Combine(media, "Cache", "OperatingSystems", "Custom", image.ContentHash, "image.wim"), discovered.ImagePath);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public async Task DiscoverAsync_IgnoresANetworkCacheShareThatStoppedAnswering()
    {
        string root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        try
        {
            string share = Path.Combine(root, "share");
            var (settings, _, published) = await PublishImageAsync(root);
            CopyTree(published, Path.Combine(share, "Foundry"));
            using NetworkCacheSession session = NetworkCacheSession.Connected(share, canWrite: false, new NoopConnector());
            session.MarkUnresponsive();

            var service = new CustomImageCatalogService(new NoVolumes(), new NetworkCacheSessionAccessor { Current = session });
            var result = await service.DiscoverAsync(settings, TestContext.Current.CancellationToken);

            Assert.Empty(result.Images);
            Assert.Equal("CustomImages.NoImages", result.ErrorKey);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public async Task DiscoverAsync_TreatsAnUnreadableShareManifestAsNotFound()
    {
        string root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        try
        {
            string share = Path.Combine(root, "share");
            var (settings, _, published) = await PublishImageAsync(root);
            CopyTree(published, Path.Combine(share, "Foundry"));
            string manifest = Path.Combine(share, "Foundry", "Cache", "OperatingSystems", "Custom", "manifests", settings.ManifestId + ".json");
            using NetworkCacheSession session = NetworkCacheSession.Connected(share, canWrite: false, new NoopConnector());
            using var locked = new FileStream(manifest, FileMode.Open, FileAccess.Read, FileShare.None);

            var service = new CustomImageCatalogService(new NoVolumes(), new NetworkCacheSessionAccessor { Current = session });
            var result = await service.DiscoverAsync(settings, TestContext.Current.CancellationToken);

            Assert.Empty(result.Images);
            Assert.Equal("CustomImages.NoImages", result.ErrorKey);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    /// <summary>Imports one image, publishes it to <c>&lt;root&gt;\published</c> and returns the bound settings.</summary>
    private static async Task<(DeployCustomImagesSettings Settings, CustomImageReference Image, string Published)> PublishImageAsync(string root)
    {
        Directory.CreateDirectory(root);
        string source = Path.Combine(root, "source.wim");
        await File.WriteAllTextAsync(source, "image", TestContext.Current.CancellationToken);
        var library = new CustomImageLibraryService(Path.Combine(root, "library"), new MetadataReader());
        CustomImageReference image = await library.ImportAsync(new(source, "Managed image"), cancellationToken: TestContext.Current.CancellationToken);
        using WinPeCustomImageMediaLease package = await new WinPeCustomImageMediaService().PrepareAsync(library,
            new() { IsEnabled = true, Images = [image] }, TestContext.Current.CancellationToken);
        string published = Path.Combine(root, "published");
        await new WinPeCustomImageMediaService().PublishAsync(package, published, TestContext.Current.CancellationToken);
        return (new DeployCustomImagesSettings { IsEnabled = true, ManifestId = package.ManifestId, ManifestHash = package.ManifestHash }, image, published);
    }

    /// <summary>Copies published media content to another root, as Foundry OSD mirrors it to the share content root.</summary>
    internal static void CopyTree(string source, string destination)
    {
        foreach (string directory in Directory.GetDirectories(source, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(Path.Combine(destination, Path.GetRelativePath(source, directory)));
        Directory.CreateDirectory(destination);
        foreach (string file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
            File.Copy(file, Path.Combine(destination, Path.GetRelativePath(source, file)));
    }

    private sealed class NoVolumes : IVolumeDiscovery
    {
        public IReadOnlyList<VolumeInfo> GetVolumes() => [];
    }

    private sealed class NoopConnector : ISmbConnector
    {
        public SmbConnectResult Connect(string uncPath, string? userName, ReadOnlySpan<char> password) => new(SmbConnectStatus.Connected, 0);
        public void Disconnect(string uncPath) { }
    }

    private sealed class MetadataReader : ICustomImageMetadataReader
    {
        public Task<IReadOnlyList<CustomImageIndex>> ReadAsync(string path, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<CustomImageIndex>>([new() { Index = 4, Name = "Image", ExpandedSizeBytes = 1024 }]);
    }

    private sealed class Volumes(string root, DriveType driveType = DriveType.Fixed, string label = "Foundry Cache") : IVolumeDiscovery
    {
        public IReadOnlyList<VolumeInfo> GetVolumes() => [new(root, label, driveType, true, long.MaxValue)];
    }
}
