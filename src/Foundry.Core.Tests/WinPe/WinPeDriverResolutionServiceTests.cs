using Foundry.Core.Services.WinPe;

namespace Foundry.Core.Tests.WinPe;

public sealed class WinPeDriverResolutionServiceTests
{
    [Fact]
    public async Task ResolveAsync_WhenNoDriversAreRequestedForStandardWinPe_ReturnsEmptyList()
    {
        var service = new WinPeDriverResolutionService(
            new FakeCatalogService([]),
            new FakePackageService());

        WinPeResult<IReadOnlyList<string>> result = await service.ResolveAsync(
            new WinPeDriverResolutionRequest
            {
                Artifact = new WinPeBuildArtifact(),
                Architecture = WinPeArchitecture.X64,
                BootImageSource = WinPeBootImageSource.WinPe,
                CatalogUri = "catalog.xml",
                DriverVendors = []
            },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value!);
    }

    [Fact]
    public async Task ResolveAsync_WhenWinReWifiIsSelected_IncludesIntelWirelessSupplement()
    {
        var catalogEntries = new[]
        {
            new WinPeDriverCatalogEntry
            {
                Id = "intel-old",
                Vendor = WinPeVendorSelection.Intel,
                PackageRole = WinPeDriverPackageRole.WifiSupplement,
                DriverFamily = WinPeDriverFamily.IntelWireless,
                ReleaseDate = new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero),
                DownloadUri = "https://example.test/intel-old.zip",
                FileName = "intel-old.zip"
            },
            new WinPeDriverCatalogEntry
            {
                Id = "intel-new",
                Vendor = WinPeVendorSelection.Intel,
                PackageRole = WinPeDriverPackageRole.WifiSupplement,
                DriverFamily = WinPeDriverFamily.IntelWireless,
                ReleaseDate = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
                DownloadUri = "https://example.test/intel-new.zip",
                FileName = "intel-new.zip"
            }
        };
        var packageService = new FakePackageService();
        var service = new WinPeDriverResolutionService(new FakeCatalogService(catalogEntries), packageService);

        WinPeResult<IReadOnlyList<string>> result = await service.ResolveAsync(
            new WinPeDriverResolutionRequest
            {
                Artifact = new WinPeBuildArtifact
                {
                    DriverWorkspacePath = "drivers"
                },
                Architecture = WinPeArchitecture.X64,
                BootImageSource = WinPeBootImageSource.WinReWifi,
                CatalogUri = "catalog.xml",
                DriverVendors = []
            },
            CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error?.Details);
        Assert.Equal("intel-new", Assert.Single(packageService.PreparedPackages).Id);
        Assert.Single(result.Value!);
    }

    [Fact]
    public async Task ResolveAsync_WhenVendorAndCustomDirectoryAreProvided_ReturnsPreparedAndCustomPaths()
    {
        string customDirectory = Path.Combine(Path.GetTempPath(), $"foundry-custom-drivers-{Guid.NewGuid():N}");
        Directory.CreateDirectory(customDirectory);
        await File.WriteAllTextAsync(Path.Combine(customDirectory, "driver.inf"), string.Empty);

        var catalogEntries = new[]
        {
            new WinPeDriverCatalogEntry
            {
                Id = "dell-old",
                Vendor = WinPeVendorSelection.Dell,
                PackageRole = WinPeDriverPackageRole.BaseDriverPack,
                ReleaseDate = new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero),
                DownloadUri = "https://example.test/dell-old.cab",
                FileName = "dell-old.cab"
            },
            new WinPeDriverCatalogEntry
            {
                Id = "dell-new",
                Vendor = WinPeVendorSelection.Dell,
                PackageRole = WinPeDriverPackageRole.BaseDriverPack,
                ReleaseDate = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
                DownloadUri = "https://example.test/dell-new.cab",
                FileName = "dell-new.cab"
            }
        };
        var packageService = new FakePackageService();
        var service = new WinPeDriverResolutionService(new FakeCatalogService(catalogEntries), packageService);

        try
        {
            WinPeResult<IReadOnlyList<string>> result = await service.ResolveAsync(
                new WinPeDriverResolutionRequest
                {
                    Artifact = new WinPeBuildArtifact
                    {
                        DriverWorkspacePath = "drivers"
                    },
                    Architecture = WinPeArchitecture.X64,
                    BootImageSource = WinPeBootImageSource.WinPe,
                    CatalogUri = "catalog.xml",
                    DriverVendors = [WinPeVendorSelection.Dell],
                    CustomDriverDirectoryPath = customDirectory
                },
                CancellationToken.None);

            Assert.True(result.IsSuccess, result.Error?.Details);
            Assert.Equal("dell-new", Assert.Single(packageService.PreparedPackages).Id);
            Assert.Contains(customDirectory, result.Value!);
        }
        finally
        {
            Directory.Delete(customDirectory, recursive: true);
        }
    }

    private sealed class FakeCatalogService(IReadOnlyList<WinPeDriverCatalogEntry> entries) : IWinPeDriverCatalogService
    {
        public Task<WinPeResult<IReadOnlyList<WinPeDriverCatalogEntry>>> GetCatalogAsync(
            WinPeDriverCatalogOptions options,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(WinPeResult<IReadOnlyList<WinPeDriverCatalogEntry>>.Success(entries));
        }
    }

    private sealed class FakePackageService : IWinPeDriverPackageService
    {
        public List<WinPeDriverCatalogEntry> PreparedPackages { get; } = [];

        public Task<WinPeResult<WinPePreparedDriverSet>> PrepareAsync(
            IReadOnlyList<WinPeDriverCatalogEntry> packages,
            string downloadRootPath,
            string extractRootPath,
            IProgress<WinPeDownloadProgress>? downloadProgress,
            CancellationToken cancellationToken)
        {
            PreparedPackages.AddRange(packages);
            return Task.FromResult(WinPeResult<WinPePreparedDriverSet>.Success(new WinPePreparedDriverSet
            {
                ExtractionDirectories = packages.Select(package => Path.Combine(extractRootPath, package.Id)).ToArray(),
                DownloadedPackagePaths = packages.Select(package => Path.Combine(downloadRootPath, package.FileName)).ToArray()
            }));
        }
    }
}
