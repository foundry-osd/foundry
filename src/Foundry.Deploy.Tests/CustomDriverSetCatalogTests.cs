// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Models.Configuration;
using Foundry.Core.Models.Configuration.Deploy;
using Foundry.Core.Models.Packages;
using Foundry.Core.Services.Drivers;
using Foundry.Core.Services.Packages;
using Foundry.Core.Services.WinPe;
using Foundry.Deploy.Services.Drivers;
using Foundry.Utilities.Storage;

namespace Foundry.Deploy.Tests;

public sealed class CustomDriverSetCatalogTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "Fdriverscatalog", Guid.NewGuid().ToString("N"));
    private CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task UnavailableWhenSectionDisabled()
    {
        var (settings, media) = await PublishAsync(1);
        var service = new CustomDriverSetCatalogService(new Volumes(new VolumeInfo(media, "Foundry Cache", DriveType.Fixed, true, long.MaxValue)));

        CustomDriverSetCatalog catalog = await service.DiscoverAsync(settings with { IsEnabled = false }, Cancellation);

        Assert.Equal(CustomDriverSetCatalogStatus.Unavailable, catalog.Status);
        Assert.Empty(catalog.Sets);
    }

    [Fact]
    public async Task UnavailableWhenNoVolumeHoldsTheManifest()
    {
        var (settings, media) = await PublishAsync(1);
        string empty = Directory.CreateDirectory(Path.Combine(root, "pxe")).FullName;
        var service = new CustomDriverSetCatalogService(new Volumes(
            new VolumeInfo(empty, "Foundry Cache", DriveType.Fixed, true, long.MaxValue),
            new VolumeInfo(media, "Other", DriveType.Fixed, true, long.MaxValue)));

        CustomDriverSetCatalog catalog = await service.DiscoverAsync(settings, Cancellation);

        Assert.Equal(CustomDriverSetCatalogStatus.Unavailable, catalog.Status);
    }

    [Fact]
    public async Task InvalidWhenManifestHashDiffers()
    {
        var (settings, media) = await PublishAsync(1);
        var service = new CustomDriverSetCatalogService(new Volumes(new VolumeInfo(media, "Foundry Cache", DriveType.Fixed, true, long.MaxValue)));

        CustomDriverSetCatalog catalog = await service.DiscoverAsync(settings with { ManifestHash = new string('0', 64) }, Cancellation);

        Assert.Equal(CustomDriverSetCatalogStatus.Invalid, catalog.Status);
        Assert.Empty(catalog.Sets);
    }

    [Fact]
    public async Task InvalidWhenAConfiguredSetIsMissingFromTheManifest()
    {
        var (settings, media) = await PublishAsync(1);
        var service = new CustomDriverSetCatalogService(new Volumes(new VolumeInfo(media, "Foundry Cache", DriveType.Fixed, true, long.MaxValue)));
        DeployCustomDriverSet missing = settings.Sets[0] with { Id = "missing", ContentHash = new string('b', 64) };

        CustomDriverSetCatalog catalog = await service.DiscoverAsync(settings with { Sets = [settings.Sets[0], missing] }, Cancellation);

        Assert.Equal(CustomDriverSetCatalogStatus.Invalid, catalog.Status);
    }

    [Fact]
    public async Task InvalidWhenAConfiguredSetHasADifferentLengthOrFileCount()
    {
        var (settings, media) = await PublishAsync(1);
        var service = new CustomDriverSetCatalogService(new Volumes(new VolumeInfo(media, "Foundry Cache", DriveType.Fixed, true, long.MaxValue)));

        CustomDriverSetCatalog length = await service.DiscoverAsync(settings with { Sets = [settings.Sets[0] with { Length = settings.Sets[0].Length + 1 }] }, Cancellation);
        CustomDriverSetCatalog count = await service.DiscoverAsync(settings with { Sets = [settings.Sets[0] with { FileCount = settings.Sets[0].FileCount + 1 }] }, Cancellation);

        Assert.Equal(CustomDriverSetCatalogStatus.Invalid, length.Status);
        Assert.Equal(CustomDriverSetCatalogStatus.Invalid, count.Status);
    }

    [Fact]
    public async Task UsesOnlyTheVolumeWhoseManifestMatches()
    {
        var (settings, media) = await PublishAsync(1);
        var (_, otherMedia) = await PublishAsync(1, "other");
        var service = new CustomDriverSetCatalogService(new Volumes(
            new VolumeInfo(otherMedia, "Foundry Cache", DriveType.Fixed, true, long.MaxValue),
            new VolumeInfo(media, "Foundry Cache", DriveType.Fixed, true, long.MaxValue)));

        CustomDriverSetCatalog catalog = await service.DiscoverAsync(settings, Cancellation);

        Assert.Equal(CustomDriverSetCatalogStatus.Available, catalog.Status);
        Assert.Equal(media, Assert.Single(catalog.Sets).MediaRoot);
    }

    [Fact]
    public async Task AvailableListsSetsWithInfCount()
    {
        var (settings, media) = await PublishAsync(2);
        var service = new CustomDriverSetCatalogService(new Volumes(new VolumeInfo(media, "Anything", DriveType.CDRom, true, long.MaxValue)));

        CustomDriverSetCatalog catalog = await service.DiscoverAsync(settings with { Sets = [settings.Sets[1], settings.Sets[0]] }, Cancellation);

        Assert.Equal(CustomDriverSetCatalogStatus.Available, catalog.Status);
        Assert.Equal([settings.Sets[1].Id, settings.Sets[0].Id], catalog.Sets.Select(set => set.Id));
        Assert.All(catalog.Sets, set =>
        {
            Assert.Equal(2, set.FileCount);
            Assert.Equal(1, set.InfCount);
            Assert.Equal(media, set.MediaRoot);
            Assert.Equal(set.FileCount, set.Manifest.Files.Count);
        });
    }

    /// <summary>Imports <paramref name="count"/> sets, publishes them to a media folder and returns the matching Deploy settings.</summary>
    private async Task<(DeployCustomDriversSettings Settings, string Media)> PublishAsync(int count, string name = "main")
    {
        var library = new CustomDriverSetLibrary(Path.Combine(root, name, "library"));
        var sets = new List<CustomDriverSetReference>();
        for (int index = 0; index < count; index++)
        {
            string source = Path.Combine(root, name, "source" + index);
            Directory.CreateDirectory(source);
            await File.WriteAllTextAsync(Path.Combine(source, "net.inf"), "[Version]\r\n" + name + index, Cancellation);
            await File.WriteAllTextAsync(Path.Combine(source, "net.sys"), "driver " + name + index, Cancellation);
            ContentPackageReference reference = await library.ImportAsync(source, Cancellation);
            sets.Add(new() { Id = "set" + index, DisplayName = "Set " + index, Package = reference });
        }
        using WinPeContentPackageMediaLease lease = await WinPeCustomDriverSetMediaService.PrepareAsync(library,
            new() { IsEnabled = true, Sets = sets, DefaultSetId = sets[0].Id }, Cancellation);
        string media = Path.Combine(root, name, "media");
        await new WinPeContentPackageMediaService(ContentPackageKind.Drivers).PublishAsync(lease, media, Cancellation);
        return (new DeployCustomDriversSettings
        {
            IsEnabled = true,
            DefaultSetId = sets[0].Id,
            ManifestId = lease.ManifestId,
            ManifestHash = lease.ManifestHash,
            Sets = sets.Select(set => new DeployCustomDriverSet
            {
                Id = set.Id,
                DisplayName = set.DisplayName,
                ContentHash = set.Package.ContentHash,
                Length = set.Package.Length,
                FileCount = set.Package.FileCount
            }).ToArray()
        }, media);
    }

    private sealed class Volumes(params VolumeInfo[] volumes) : IVolumeDiscovery
    {
        public IReadOnlyList<VolumeInfo> GetVolumes() => volumes;
    }

    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
}
