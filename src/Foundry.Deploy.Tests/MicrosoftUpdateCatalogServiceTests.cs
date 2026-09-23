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
    public async Task DriverExpand_WhenOneSelectedCabContainsNoInf_FailsForThatPayload()
    {
        using TempDirectory temp = TempDirectory.Create();
        string[] paths = [Path.Combine(temp.Path, "valid.cab"), Path.Combine(temp.Path, "empty.cab")];
        foreach (string path in paths) await File.WriteAllTextAsync(path, "cab", TestContext.Current.CancellationToken);
        var service = new MicrosoftUpdateCatalogDriverService(new FakeArchiveExtractionService { EmptyFileName = "empty.cab" },
            new FakeMicrosoftUpdateCatalogClient(), new CapturingArtifactDownloadService(),
            NullLogger<MicrosoftUpdateCatalogDriverService>.Instance);

        InvalidOperationException failure = await Assert.ThrowsAsync<InvalidOperationException>(() => service.ExpandAsync(
            paths, Path.Combine(temp.Path, "extracted"), TestContext.Current.CancellationToken));

        Assert.Contains("empty.cab", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FirmwareExtraction_ReportsProgressBeforeReturningWithoutQueuedCallbacks()
    {
        using TempDirectory temp = TempDirectory.Create();
        string source = Path.Combine(temp.Path, "raw");
        Directory.CreateDirectory(source);
        await File.WriteAllTextAsync(Path.Combine(source, "firmware.cab"), "cab", TestContext.Current.CancellationToken);
        var service = new MicrosoftUpdateCatalogFirmwareService(new FakeArchiveExtractionService(), new FakeMicrosoftUpdateCatalogClient(),
            new CapturingArtifactDownloadService(), NullLogger<MicrosoftUpdateCatalogFirmwareService>.Instance);
        var progress = new RecordingProgress();
        var queuedContext = new QueuedSynchronizationContext();
        SynchronizationContext? original = SynchronizationContext.Current;
        try
        {
            SynchronizationContext.SetSynchronizationContext(queuedContext);
            Task<int> extraction = service.ExtractAsync(source, Path.Combine(temp.Path, "extracted"),
                TestContext.Current.CancellationToken, progress);

            Assert.True(extraction.IsCompletedSuccessfully);
            Assert.Equal([100d], progress.Values);
            Assert.Equal(0, queuedContext.PostCount);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(original);
        }
    }

    [Fact]
    public async Task FirmwareDownload_DoesNotExtractPayload()
    {
        using TempDirectory temp = TempDirectory.Create();
        var extractor = new FakeArchiveExtractionService();
        var service = new MicrosoftUpdateCatalogFirmwareService(extractor, new FakeMicrosoftUpdateCatalogClient(),
            new CapturingArtifactDownloadService(), NullLogger<MicrosoftUpdateCatalogFirmwareService>.Instance);

        MicrosoftUpdateCatalogFirmwareResult result = await service.DownloadAsync(
            new HardwareProfile { SystemFirmwareHardwareId = "test-firmware" }, "x64", Path.Combine(temp.Path, "raw"),
            (_, _) => Path.Combine(temp.Path, "cache"), TestContext.Current.CancellationToken);

        Assert.True(result.IsUpdateAvailable);
        Assert.Empty(extractor.SourcePaths);
        Assert.False(Directory.Exists(Path.Combine(temp.Path, "extracted")));
    }

    [Fact]
    public async Task DriverExpand_WhenSelectedCabContainsNoInf_Fails()
    {
        using TempDirectory temp = TempDirectory.Create();
        string cabPath = Path.Combine(temp.Path, "empty.cab");
        await File.WriteAllTextAsync(cabPath, "cab", TestContext.Current.CancellationToken);
        var service = new MicrosoftUpdateCatalogDriverService(new EmptyExtractionService(), new FakeMicrosoftUpdateCatalogClient(),
            new CapturingArtifactDownloadService(), NullLogger<MicrosoftUpdateCatalogDriverService>.Instance);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ExpandAsync(
            [cabPath], Path.Combine(temp.Path, "extracted"), TestContext.Current.CancellationToken));
    }

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
            (_, _) => cacheDirectory,
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
            (_, _) => cacheDirectory,
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
                rawDirectory, (_, _) => cacheDirectory, TestContext.Current.CancellationToken);
            Assert.True(result.IsUpdateAvailable);
            Assert.Equal(validCache ? 0 : 1, result.DownloadedCount);
            Assert.Equal(validCache ? 1 : 0, result.ReusedCount);
            selectedPath = Path.Combine(rawDirectory, "update-1", "driver-amd64.cab");
        }
        else
        {
            var service = new MicrosoftUpdateCatalogDriverService(
                new FakeArchiveExtractionService(), catalogClient, downloadService,
                NullLogger<MicrosoftUpdateCatalogDriverService>.Instance);
            MicrosoftUpdateCatalogDriverResult result = await service.DownloadAsync(
                CreateHardwareProfile(), new OperatingSystemCatalogItem { ReleaseId = "24H2", Architecture = "x64" },
                rawDirectory, (_, _) => cacheDirectory, TestContext.Current.CancellationToken);
            Assert.True(result.IsPayloadAvailable);
            Assert.Equal(validCache ? 0 : 1, result.DownloadedCount);
            Assert.Equal(validCache ? 1 : 0, result.ReusedCount);
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
                rawDirectory, (_, _) => throw new InvalidOperationException("Hashless payload must not use persistent cache."),
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
        string destination = Path.Combine(temp.Path, "extracted");
        string staleDirectory = Path.Combine(destination, "unselected");
        Directory.CreateDirectory(staleDirectory);
        await File.WriteAllTextAsync(Path.Combine(staleDirectory, "stale.inf"), "; stale", TestContext.Current.CancellationToken);
        var extractor = new FakeArchiveExtractionService();
        var service = new MicrosoftUpdateCatalogDriverService(extractor, new FakeMicrosoftUpdateCatalogClient(),
            new CapturingArtifactDownloadService(), NullLogger<MicrosoftUpdateCatalogDriverService>.Instance);

        MicrosoftUpdateCatalogDriverResult result = await service.ExpandAsync(
            paths, destination, TestContext.Current.CancellationToken);

        Assert.Equal(paths, extractor.SourcePaths);
        Assert.Equal(2, result.InfCount);
        Assert.True(result.IsPayloadAvailable);
        Assert.False(Directory.Exists(staleDirectory));
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
        public bool IsAvailable { get; init; } = true;

        public Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(IsAvailable);
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
        public string? EmptyFileName { get; init; }
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
            if (Path.GetFileName(archivePath) != EmptyFileName) File.WriteAllText(Path.Combine(extractedPath, "driver.inf"), "; test");
            progress?.Report(100d);
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingProgress : IProgress<double>
    {
        public List<double> Values { get; } = [];
        public void Report(double value) => Values.Add(value);
    }

    private sealed class QueuedSynchronizationContext : SynchronizationContext
    {
        public int PostCount { get; private set; }
        public override void Post(SendOrPostCallback callback, object? state) => PostCount++;
    }

    private sealed class EmptyExtractionService : IArchiveExtractionService
    {
        public Task ExtractWithSevenZipAsync(string archivePath, string extractedPath, string workingDirectory,
            CancellationToken cancellationToken = default, IProgress<double>? progress = null) => Task.CompletedTask;
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
