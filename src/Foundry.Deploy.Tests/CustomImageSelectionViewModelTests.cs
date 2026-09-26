// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Models.Configuration;
using Foundry.Core.Models.Configuration.Deploy;
using Foundry.Deploy.Models;
using Foundry.Deploy.Services.Images;
using Foundry.Deploy.ViewModels;
using Foundry.Utilities.Storage;

namespace Foundry.Deploy.Tests;

public sealed class CustomImageSelectionViewModelTests
{
    [Fact]
    public async Task EnteringCustomMode_RediscoversImagesAfterReturningFromCatalog()
    {
        var volumes = new EmptyVolumes();
        using var model = new CustomImageSelectionViewModel(new CustomImageCatalogService(volumes));
        model.Configure(new DeployCustomImagesSettings
        {
            IsEnabled = true,
            DefaultSource = CustomImageSource.Catalog,
            ManifestId = "build",
            ManifestHash = new string('a', 64)
        });
        Assert.Equal(0, volumes.DiscoveryCount);

        model.IsCustom = true;
        Assert.Equal(1, volumes.DiscoveryCount);
        Assert.Equal("CustomImages.NoImages", model.ErrorKey);

        model.IsCatalog = true;
        model.IsCustom = true;
        Assert.Equal(2, volumes.DiscoveryCount);

        model.Configure(new DeployCustomImagesSettings());
        await model.RefreshAsync();
        Assert.Equal(2, volumes.DiscoveryCount);
    }

    [Fact]
    public void MissingDefaultIndex_RemainsBlockedAfterRefreshUntilOperatorOverrides()
    {
        using var model = new CustomImageSelectionViewModel();
        var asset = new CustomImageAsset { Id = "preferred", DisplayName = "Image", ExpectedHash = new string('a', 64), ImagePath = @"D:\image.wim", VolumeRoot = @"D:\" };
        model.Configure(new DeployCustomImagesSettings { IsEnabled = true, DefaultSource = CustomImageSource.Custom, DefaultImageId = asset.Id, DefaultImageIndex = 9 });
        for (int refresh = 0; refresh < 2; refresh++)
        {
            model.ApplyCatalog([asset]);
            model.ApplyIndexes([new() { Index = 1, Name = "Other" }]);
            Assert.Null(model.Selection);
            Assert.Equal("CustomImages.MissingDefault", model.ErrorKey);
        }
        model.SelectedIndex = model.Indexes[0];
        Assert.NotNull(model.Selection);
    }
    [Fact]
    public void ApplyCatalog_MissingConfiguredDefaultDoesNotChooseAnotherImage()
    {
        using var model = new CustomImageSelectionViewModel();
        model.Configure(new DeployCustomImagesSettings { IsEnabled = true, DefaultSource = CustomImageSource.Custom, DefaultImageId = "missing" });
        model.ApplyCatalog([new CustomImageAsset { Id = "other", DisplayName = "Other", ImagePath = @"D:\image.wim", VolumeRoot = @"D:\" }]);
        Assert.Null(model.SelectedAsset);
        Assert.Null(model.Selection);
        Assert.Equal("CustomImages.MissingDefault", model.ErrorKey);
    }

    [Fact]
    public void ApplyIndexes_DuplicateEditionsRequireExactIndexAndPreserveNonCatalogMetadata()
    {
        using var model = new CustomImageSelectionViewModel();
        model.Configure(new DeployCustomImagesSettings { IsEnabled = true, DefaultSource = CustomImageSource.Custom });
        model.ApplyIndexes([new() { Index = 2, Name = "A", EditionId = "EnterpriseS" }, new() { Index = 7, Name = "B", EditionId = "EnterpriseS" }]);
        Assert.Null(model.SelectedIndex);
        Assert.Equal(2, model.Indexes.Count);
        model.SelectedIndex = model.Indexes[1];
        Assert.Equal(7, model.SelectedIndex.Index);
    }

    private sealed class EmptyVolumes : IVolumeDiscovery
    {
        public int DiscoveryCount { get; private set; }

        public IReadOnlyList<VolumeInfo> GetVolumes()
        {
            DiscoveryCount++;
            return [];
        }
    }
}
