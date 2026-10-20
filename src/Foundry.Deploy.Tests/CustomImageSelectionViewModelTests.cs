// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Runtime.InteropServices;
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
        using var model = new CustomImageSelectionViewModel(deviceArchitecture: Architecture.X64);
        var asset = new CustomImageAsset { Id = "preferred", DisplayName = "Image", ExpectedHash = new string('a', 64), ImagePath = @"D:\image.wim", VolumeRoot = @"D:\" };
        model.Configure(new DeployCustomImagesSettings { IsEnabled = true, DefaultSource = CustomImageSource.Custom, DefaultImageId = asset.Id, DefaultImageIndex = 9 });
        for (int refresh = 0; refresh < 2; refresh++)
        {
            model.ApplyCatalog([asset]);
            model.ApplyIndexes([new() { Index = 1, Name = "Other", Architecture = "x64" }]);
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
        using var model = new CustomImageSelectionViewModel(deviceArchitecture: Architecture.X64);
        model.Configure(new DeployCustomImagesSettings { IsEnabled = true, DefaultSource = CustomImageSource.Custom });
        model.ApplyIndexes([new() { Index = 2, Name = "A", EditionId = "EnterpriseS", Architecture = "x64" }, new() { Index = 7, Name = "B", EditionId = "EnterpriseS", Architecture = "x64" }]);
        Assert.Null(model.SelectedIndex);
        Assert.Equal(2, model.Indexes.Count);
        model.SelectedIndex = model.Indexes[1];
        Assert.Equal(7, model.SelectedIndex.Index);
    }

    [Fact]
    public void ApplyIndexes_OffersOnlyIndexesBuiltForTheDevice()
    {
        using var model = new CustomImageSelectionViewModel(deviceArchitecture: Architecture.Arm64);
        model.Configure(new DeployCustomImagesSettings { IsEnabled = true, DefaultSource = CustomImageSource.Custom });

        model.ApplyIndexes([new() { Index = 1, Name = "x64", Architecture = "x64" }, new() { Index = 2, Name = "arm64", Architecture = "arm64" }]);

        CustomImageIndex offered = Assert.Single(model.Indexes);
        Assert.Equal(2, offered.Index);
        Assert.Equal(2, model.SelectedIndex?.Index);
        Assert.Equal(string.Empty, model.ErrorKey);
    }

    [Theory]
    [InlineData(Architecture.X64, "arm64")]
    [InlineData(Architecture.Arm64, "x64")]
    [InlineData(Architecture.X64, "x86")]
    [InlineData(Architecture.Arm64, "unknown")]
    public void ApplyIndexes_WhenNoIndexMatchesTheDevice_ReportsArchitectureMismatch(Architecture device, string imageArchitecture)
    {
        using var model = new CustomImageSelectionViewModel(deviceArchitecture: device);
        model.Configure(new DeployCustomImagesSettings { IsEnabled = true, DefaultSource = CustomImageSource.Custom });

        model.ApplyIndexes([new() { Index = 1, Name = "Image", Architecture = imageArchitecture }]);

        Assert.Empty(model.Indexes);
        Assert.Null(model.Selection);
        Assert.Equal("Preflight.ArchitectureMismatch", model.ErrorKey);
    }

    [Fact]
    public void ApplyIndexes_WhenDefaultIndexTargetsAnotherArchitecture_ReportsArchitectureMismatch()
    {
        using var model = new CustomImageSelectionViewModel(deviceArchitecture: Architecture.X64);
        var asset = new CustomImageAsset { Id = "preferred", DisplayName = "Image", ExpectedHash = new string('a', 64), ImagePath = @"D:\image.wim", VolumeRoot = @"D:\" };
        model.Configure(new DeployCustomImagesSettings { IsEnabled = true, DefaultSource = CustomImageSource.Custom, DefaultImageId = asset.Id, DefaultImageIndex = 2 });
        model.ApplyCatalog([asset]);

        model.ApplyIndexes([new() { Index = 1, Name = "x64", Architecture = "x64" }, new() { Index = 2, Name = "arm64", Architecture = "arm64" }]);

        Assert.Null(model.Selection);
        Assert.Equal("Preflight.ArchitectureMismatch", model.ErrorKey);
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
