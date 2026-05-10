using Foundry.Core.Services.WinPe;

namespace Foundry.Core.Tests.WinPe;

public sealed class WinPeDriverCatalogServiceTests
{
    [Fact]
    public async Task GetCatalogAsync_FiltersArchitectureReleaseVendorAndPreviewPackages()
    {
        string catalogPath = Path.Combine(Path.GetTempPath(), $"foundry-driver-catalog-{Guid.NewGuid():N}.xml");
        await File.WriteAllTextAsync(catalogPath, """
                                                <Catalog>
                                                  <DriverPack id="dell-x64" manufacturer="Dell" name="Dell WinPE11 package" version="1.0" downloadUrl="https://example.test/dell.cab" fileName="dell.cab" format="cab" releaseDate="2026-01-01">
                                                    <OsInfo releaseId="11/24H2" architecture="x64" />
                                                    <Hashes sha256="abc" />
                                                  </DriverPack>
                                                  <DriverPack id="hp-arm64" manufacturer="HP" name="HP WinPE11 package" version="1.0" downloadUrl="https://example.test/hp.cab" fileName="hp.cab" format="cab" releaseDate="2026-01-02">
                                                    <OsInfo releaseId="11" architecture="arm64" />
                                                    <Hashes sha256="def" />
                                                  </DriverPack>
                                                  <DriverPack id="dell-preview" manufacturer="Dell" name="Dell preview WinPE11 package" version="2.0-preview" downloadUrl="https://example.test/preview.cab" fileName="preview.cab" format="cab" releaseDate="2026-01-03">
                                                    <OsInfo releaseId="11" architecture="x64" />
                                                  </DriverPack>
                                                </Catalog>
                                                """);

        var service = new WinPeDriverCatalogService();

        try
        {
            WinPeResult<IReadOnlyList<WinPeDriverCatalogEntry>> result = await service.GetCatalogAsync(
                new WinPeDriverCatalogOptions
                {
                    CatalogUri = catalogPath,
                    Architecture = WinPeArchitecture.X64,
                    Vendors = [WinPeVendorSelection.Dell],
                    RequiredWinPeReleaseId = "11"
                },
                CancellationToken.None);

            Assert.True(result.IsSuccess, result.Error?.Details);
            WinPeDriverCatalogEntry entry = Assert.Single(result.Value!);
            Assert.Equal("dell-x64", entry.Id);
            Assert.Equal(WinPeVendorSelection.Dell, entry.Vendor);
            Assert.Equal(WinPeArchitecture.X64, entry.Architecture);
        }
        finally
        {
            File.Delete(catalogPath);
        }
    }

    [Fact]
    public async Task GetCatalogAsync_ParsesWifiSupplementMetadata()
    {
        string catalogPath = Path.Combine(Path.GetTempPath(), $"foundry-driver-catalog-{Guid.NewGuid():N}.xml");
        await File.WriteAllTextAsync(catalogPath, """
                                                <Catalog>
                                                  <DriverPack id="intel-wifi" manufacturer="Intel" name="Intel Wi-Fi supplement" version="1.0" downloadUrl="https://example.test/intel.zip" fileName="intel.zip" format="zip" packageRole="WifiSupplement" driverFamily="IntelWireless" releaseDate="2026-01-01">
                                                    <OsInfo releaseId="11" architecture="amd64" />
                                                    <Hashes sha256="abc" />
                                                  </DriverPack>
                                                </Catalog>
                                                """);

        var service = new WinPeDriverCatalogService();

        try
        {
            WinPeResult<IReadOnlyList<WinPeDriverCatalogEntry>> result = await service.GetCatalogAsync(
                new WinPeDriverCatalogOptions
                {
                    CatalogUri = catalogPath,
                    Architecture = WinPeArchitecture.X64,
                    Vendors = [WinPeVendorSelection.Intel]
                },
                CancellationToken.None);

            Assert.True(result.IsSuccess, result.Error?.Details);
            WinPeDriverCatalogEntry entry = Assert.Single(result.Value!);
            Assert.Equal(WinPeDriverPackageRole.WifiSupplement, entry.PackageRole);
            Assert.Equal(WinPeDriverFamily.IntelWireless, entry.DriverFamily);
        }
        finally
        {
            File.Delete(catalogPath);
        }
    }
}
