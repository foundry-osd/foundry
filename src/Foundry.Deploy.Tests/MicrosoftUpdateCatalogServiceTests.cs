// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Net;
using System.Security.Cryptography;
using System.Text;
using Foundry.Deploy.Models;
using Foundry.Deploy.Services.Download;
using Foundry.Deploy.Services.DriverPacks;
using Foundry.Deploy.Services.System;
using Microsoft.Extensions.Logging.Abstractions;

namespace Foundry.Deploy.Tests;

public sealed class MicrosoftUpdateCatalogServiceTests
{
    [Fact]
    public async Task DriverDownload_WhenCatalogHashExists_UsesPersistentCacheWithoutCopyingCab()
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
            _ => cacheDirectory,
            TestContext.Current.CancellationToken);

        string expectedCachePath = Path.Combine(cacheDirectory, "update-1", "driver-amd64.cab");
        string expectedRawPath = Path.Combine(rawDirectory, "update-1", "driver-amd64.cab");
        Assert.True(result.IsPayloadAvailable);
        Assert.Equal(expectedCachePath, downloadService.DestinationPath);
        Assert.Equal(new string('B', 64), downloadService.ExpectedHash);
        Assert.Equal("MicrosoftUpdateCatalogDriver", downloadService.ArtifactKind);
        Assert.False(File.Exists(expectedRawPath));
        Assert.Equal(expectedCachePath, Assert.Single(result.DownloadedDrivers).FilePath);
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

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task CatalogDownload_VerifiesCachedContentBeforeUsingPayload(bool firmware, bool validCache)
    {
        using TempDirectory temp = TempDirectory.Create();
        string rawDirectory = Path.Combine(temp.Path, "raw");
        string cacheDirectory = Path.Combine(temp.Path, "cache");
        string cachePath = Path.Combine(cacheDirectory, "update-1", "driver-amd64.cab");
        byte[] content = Encoding.UTF8.GetBytes("original-content");
        string expectedHash = Convert.ToHexString(SHA256.HashData(content));
        Directory.CreateDirectory(Path.GetDirectoryName(cachePath)!);
        await File.WriteAllBytesAsync(cachePath, validCache ? content : Encoding.UTF8.GetBytes("tampered-content"), TestContext.Current.CancellationToken);
        await ArtifactDownloadServiceTests.WriteLegacyManifestAsync(cachePath, expectedHash, "SHA256");
        var catalogClient = new FakeMicrosoftUpdateCatalogClient { Sha256 = expectedHash };
        var handler = new PayloadHttpMessageHandler(content);
        using var client = new HttpClient(handler);
        var downloadService = new ArtifactDownloadService(NullLogger<ArtifactDownloadService>.Instance, client);
        string selectedPath;

        if (firmware)
        {
            var service = new MicrosoftUpdateCatalogFirmwareService(
                new FakeArchiveExtractionService(), catalogClient, downloadService,
                NullLogger<MicrosoftUpdateCatalogFirmwareService>.Instance);
            MicrosoftUpdateCatalogFirmwareResult result = await service.DownloadAsync(
                new HardwareProfile { SystemFirmwareHardwareId = "UEFI\\RES_{FIRMWARE}" }, "x64",
                rawDirectory, Path.Combine(temp.Path, "extracted"), cacheDirectory, TestContext.Current.CancellationToken);
            Assert.True(result.IsUpdateAvailable);
            selectedPath = Path.Combine(rawDirectory, "update-1", "driver-amd64.cab");
        }
        else
        {
            var service = new MicrosoftUpdateCatalogDriverService(
                new FakeArchiveExtractionService(), catalogClient, downloadService,
                NullLogger<MicrosoftUpdateCatalogDriverService>.Instance);
            MicrosoftUpdateCatalogDriverResult result = await service.DownloadAsync(
                CreateHardwareProfile(), new OperatingSystemCatalogItem { ReleaseId = "24H2", Architecture = "x64" },
                rawDirectory, _ => cacheDirectory, TestContext.Current.CancellationToken);
            Assert.True(result.IsPayloadAvailable);
            selectedPath = Assert.Single(result.DownloadedDrivers).FilePath;
            Assert.Equal(cachePath, selectedPath);
            Assert.Empty(Directory.EnumerateFiles(rawDirectory, "*", SearchOption.AllDirectories));
        }

        Assert.Equal(content, await File.ReadAllBytesAsync(selectedPath, TestContext.Current.CancellationToken));
        Assert.Equal(content, await File.ReadAllBytesAsync(cachePath, TestContext.Current.CancellationToken));
        Assert.Equal(validCache ? 0 : 1, handler.RequestCount);
    }

    [Fact]
    public async Task DriverDownload_WithoutHash_DownloadsFreshTemporaryPayloadOnEveryRun()
    {
        using TempDirectory temp = TempDirectory.Create();
        string rawDirectory = Path.Combine(temp.Path, "raw");
        var catalogClient = new FakeMicrosoftUpdateCatalogClient { Sha256 = "", Sha1 = "" };
        byte[] content = Encoding.UTF8.GetBytes("fresh-cab");
        var handler = new PayloadHttpMessageHandler(content);
        using var client = new HttpClient(handler);
        var service = new MicrosoftUpdateCatalogDriverService(
            new FakeArchiveExtractionService(), catalogClient,
            new ArtifactDownloadService(NullLogger<ArtifactDownloadService>.Instance, client),
            NullLogger<MicrosoftUpdateCatalogDriverService>.Instance);

        for (int run = 0; run < 2; run++)
        {
            MicrosoftUpdateCatalogDriverResult result = await service.DownloadAsync(
                CreateHardwareProfile(), new OperatingSystemCatalogItem { ReleaseId = "24H2", Architecture = "x64" },
                rawDirectory, _ => throw new InvalidOperationException("Hashless payload must not use persistent cache."),
                TestContext.Current.CancellationToken);
            string selectedPath = Assert.Single(result.DownloadedDrivers).FilePath;
            Assert.Equal(Path.Combine(rawDirectory, "update-1", "driver-amd64.cab"), selectedPath);
            Assert.Equal(content, await File.ReadAllBytesAsync(selectedPath, TestContext.Current.CancellationToken));
        }

        Assert.Equal(2, handler.RequestCount);
    }

    [Fact]
    public async Task DriverExpand_UsesOnlySelectedCabsAndKeepsSameNamedPayloadsSeparate()
    {
        using TempDirectory temp = TempDirectory.Create();
        string[] paths = [Path.Combine(temp.Path, "one", "driver.cab"), Path.Combine(temp.Path, "two", "driver.cab")];
        foreach (string path in paths)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllTextAsync(path, "cab", TestContext.Current.CancellationToken);
        }

        await File.WriteAllTextAsync(Path.Combine(temp.Path, "unrelated.cab"), "stale", TestContext.Current.CancellationToken);
        var extractor = new FakeArchiveExtractionService();
        var service = new MicrosoftUpdateCatalogDriverService(extractor, new FakeMicrosoftUpdateCatalogClient(),
            new CapturingArtifactDownloadService(), NullLogger<MicrosoftUpdateCatalogDriverService>.Instance);

        MicrosoftUpdateCatalogDriverResult result = await service.ExpandAsync(
            paths, Path.Combine(temp.Path, "extracted"), TestContext.Current.CancellationToken);

        Assert.Equal(paths, extractor.SourcePaths);
        Assert.Equal(2, result.InfCount);
        Assert.True(result.IsPayloadAvailable);
    }

    private sealed class PayloadHttpMessageHandler(byte[] content) : HttpMessageHandler
    {
        public int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(content) });
        }
    }

    internal static HardwareProfile CreateHardwareProfile(bool multipleDevices = false)
    {
        var networkDevice = new PnpDeviceInfo
        {
            Name = "Network adapter",
            DeviceId = @"PCI\VEN_8086&DEV_15B7&SUBSYS_00000000",
            HardwareIds = [@"PCI\VEN_8086&DEV_15B7"],
            PnpClass = "Net"
        };
        return new HardwareProfile
        {
            PnpDevices = multipleDevices ?
            [
                networkDevice,
                new PnpDeviceInfo
                {
                    Name = "Storage controller",
                    DeviceId = @"PCI\VEN_8086&DEV_1234",
                    HardwareIds = [@"PCI\VEN_8086&DEV_1234"],
                    PnpClass = "SCSIAdapter"
                }
            ] : [networkDevice]
        };
    }

    internal sealed class FakeMicrosoftUpdateCatalogClient : IMicrosoftUpdateCatalogClient
    {
        public string Sha256 { get; init; } = new('B', 64);
        public string Sha1 { get; init; } = new('A', 40);
        public long SizeInBytes { get; init; } = 1;

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
                    UpdateId = searchQuery.Contains("DEV_1234", StringComparison.OrdinalIgnoreCase) ? "update-2" : "update-1",
                    Title = "Driver update",
                    Version = "1.0",
                    Size = "1 MB",
                    SizeInBytes = SizeInBytes
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
                    Sha1 = Sha1,
                    Sha256 = Sha256
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

    internal sealed class FakeArchiveExtractionService : IArchiveExtractionService
    {
        public List<string> SourcePaths { get; } = [];
        public Task ExtractWithSevenZipAsync(
            string archivePath,
            string extractedPath,
            string workingDirectory,
            CancellationToken cancellationToken = default,
            IProgress<double>? progress = null)
        {
            SourcePaths.Add(archivePath);
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
