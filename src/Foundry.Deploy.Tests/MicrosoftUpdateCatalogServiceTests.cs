using Foundry.Deploy.Models;
using Foundry.Deploy.Services.Download;
using Foundry.Deploy.Services.DriverPacks;
using Foundry.Deploy.Services.System;
using Microsoft.Extensions.Logging.Abstractions;

namespace Foundry.Deploy.Tests;

public sealed class MicrosoftUpdateCatalogServiceTests
{
    [Fact]
    public async Task DriverDownload_WhenCatalogHashExists_UsesPersistentCacheAndStagesCab()
    {
        using TempDirectory temp = TempDirectory.Create();
        string rawDirectory = Path.Combine(temp.Path, "raw");
        string cacheDirectory = Path.Combine(temp.Path, "cache");
        var catalogClient = new FakeMicrosoftUpdateCatalogClient();
        var downloadService = new CapturingArtifactDownloadService();
        var service = new MicrosoftUpdateCatalogDriverService(
            new FakeArchiveExtractionService(),
            catalogClient,
            downloadService,
            NullLogger<MicrosoftUpdateCatalogDriverService>.Instance);

        MicrosoftUpdateCatalogDriverResult result = await service.DownloadAsync(
            CreateHardwareProfile(),
            new OperatingSystemCatalogItem { ReleaseId = "24H2", Architecture = "x64" },
            rawDirectory,
            cacheDirectory,
            TestContext.Current.CancellationToken);

        string expectedCachePath = Path.Combine(cacheDirectory, "update-1", "driver-amd64.cab");
        string expectedRawPath = Path.Combine(rawDirectory, "update-1", "driver-amd64.cab");
        Assert.True(result.IsPayloadAvailable);
        Assert.Equal(expectedCachePath, downloadService.DestinationPath);
        Assert.Equal(new string('B', 64), downloadService.ExpectedHash);
        Assert.Equal("MicrosoftUpdateCatalogDriver", downloadService.ArtifactKind);
        Assert.True(File.Exists(expectedRawPath));
    }

    [Fact]
    public async Task FirmwareDownload_WhenCatalogHashExists_UsesPersistentCacheAndStagesCab()
    {
        using TempDirectory temp = TempDirectory.Create();
        string rawDirectory = Path.Combine(temp.Path, "raw");
        string extractedDirectory = Path.Combine(temp.Path, "extracted");
        string cacheDirectory = Path.Combine(temp.Path, "cache");
        var catalogClient = new FakeMicrosoftUpdateCatalogClient();
        var downloadService = new CapturingArtifactDownloadService();
        var service = new MicrosoftUpdateCatalogFirmwareService(
            new FakeArchiveExtractionService(),
            catalogClient,
            downloadService,
            NullLogger<MicrosoftUpdateCatalogFirmwareService>.Instance);

        MicrosoftUpdateCatalogFirmwareResult result = await service.DownloadAsync(
            new HardwareProfile { SystemFirmwareHardwareId = "UEFI\\RES_{FIRMWARE}" },
            "x64",
            rawDirectory,
            extractedDirectory,
            cacheDirectory,
            TestContext.Current.CancellationToken);

        string expectedCachePath = Path.Combine(cacheDirectory, "update-1", "driver-amd64.cab");
        string expectedRawPath = Path.Combine(rawDirectory, "update-1", "driver-amd64.cab");
        Assert.True(result.IsUpdateAvailable);
        Assert.Equal(expectedCachePath, downloadService.DestinationPath);
        Assert.Equal(new string('B', 64), downloadService.ExpectedHash);
        Assert.Equal("MicrosoftUpdateCatalogFirmware", downloadService.ArtifactKind);
        Assert.True(File.Exists(expectedRawPath));
    }

    private static HardwareProfile CreateHardwareProfile()
    {
        return new HardwareProfile
        {
            PnpDevices =
            [
                new PnpDeviceInfo
                {
                    Name = "Network adapter",
                    DeviceId = @"PCI\VEN_8086&DEV_15B7&SUBSYS_00000000",
                    HardwareIds = [@"PCI\VEN_8086&DEV_15B7"],
                    PnpClass = "Net"
                }
            ]
        };
    }

    private sealed class FakeMicrosoftUpdateCatalogClient : IMicrosoftUpdateCatalogClient
    {
        public Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(true);
        }

        public Task<IReadOnlyList<MicrosoftUpdateCatalogUpdate>> SearchAsync(
            string searchQuery,
            bool descending = true,
            CancellationToken cancellationToken = default)
        {
            IReadOnlyList<MicrosoftUpdateCatalogUpdate> updates =
            [
                new MicrosoftUpdateCatalogUpdate
                {
                    UpdateId = "update-1",
                    Title = "Driver update",
                    Version = "1.0",
                    Size = "1 MB"
                }
            ];

            return Task.FromResult(updates);
        }

        public Task<IReadOnlyList<MicrosoftUpdateCatalogDownload>> GetDownloadsAsync(
            string updateId,
            CancellationToken cancellationToken = default)
        {
            IReadOnlyList<MicrosoftUpdateCatalogDownload> downloads =
            [
                new MicrosoftUpdateCatalogDownload
                {
                    DownloadUrl = "https://example.test/driver-amd64.cab",
                    FileName = "driver-amd64.cab",
                    Sha1 = new string('A', 40),
                    Sha256 = new string('B', 64)
                }
            ];

            return Task.FromResult(downloads);
        }
    }

    private sealed class CapturingArtifactDownloadService : IArtifactDownloadService
    {
        public string? DestinationPath { get; private set; }
        public string? ExpectedHash { get; private set; }
        public string? ArtifactKind { get; private set; }

        public async Task<ArtifactDownloadResult> DownloadAsync(
            string sourceUrl,
            string destinationPath,
            string? expectedHash = null,
            long? expectedSizeBytes = null,
            string? artifactKind = null,
            CancellationToken cancellationToken = default,
            IProgress<DownloadProgress>? progress = null)
        {
            DestinationPath = destinationPath;
            ExpectedHash = expectedHash;
            ArtifactKind = artifactKind;
            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
            await File.WriteAllTextAsync(destinationPath, "cab", cancellationToken).ConfigureAwait(false);

            return new ArtifactDownloadResult
            {
                DestinationPath = destinationPath,
                Downloaded = true,
                Method = "test",
                SizeBytes = 3
            };
        }
    }

    private sealed class FakeArchiveExtractionService : IArchiveExtractionService
    {
        public Task ExtractWithSevenZipAsync(
            string archivePath,
            string extractedPath,
            string workingDirectory,
            CancellationToken cancellationToken = default,
            IProgress<double>? progress = null)
        {
            Directory.CreateDirectory(extractedPath);
            File.WriteAllText(Path.Combine(extractedPath, "driver.inf"), "; test");
            return Task.CompletedTask;
        }
    }

    private sealed class TempDirectory : IDisposable
    {
        private TempDirectory(string path)
        {
            Path = path;
            Directory.CreateDirectory(path);
        }

        public string Path { get; }

        public static TempDirectory Create()
        {
            return new TempDirectory(System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"foundry-muc-{Guid.NewGuid():N}"));
        }

        public void Dispose()
        {
            Directory.Delete(Path, recursive: true);
        }
    }
}
