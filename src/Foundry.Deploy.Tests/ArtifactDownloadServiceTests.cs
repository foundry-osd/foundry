using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Foundry.Deploy.Services.Download;
using Microsoft.Extensions.Logging.Abstractions;

namespace Foundry.Deploy.Tests;

public sealed class ArtifactDownloadServiceTests
{
    [Fact]
    public async Task DownloadAsync_WhenLegacyCacheIsHashValid_WritesManifestAndReusesCache()
    {
        using TempDirectory temp = TempDirectory.Create();
        string destinationPath = Path.Combine(temp.Path, "install.esd");
        byte[] content = Encoding.UTF8.GetBytes("valid cached content");
        await File.WriteAllBytesAsync(destinationPath, content, TestContext.Current.CancellationToken);
        string expectedHash = ComputeSha256(content);

        var service = new ArtifactDownloadService(NullLogger<ArtifactDownloadService>.Instance);

        ArtifactDownloadResult result = await service.DownloadAsync(
            "https://example.test/install.esd",
            destinationPath,
            expectedHash,
            expectedSizeBytes: content.Length,
            artifactKind: "OperatingSystemImage",
            cancellationToken: TestContext.Current.CancellationToken);

        string manifestPath = $"{destinationPath}.manifest.json";
        Assert.False(result.Downloaded);
        Assert.Equal("cache-hit", result.Method);
        Assert.True(File.Exists(manifestPath));

        using FileStream stream = File.OpenRead(manifestPath);
        ArtifactCacheManifest? manifest = await JsonSerializer.DeserializeAsync<ArtifactCacheManifest>(
            stream,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.NotNull(manifest);
        Assert.Equal(1, manifest.Version);
        Assert.Equal("OperatingSystemImage", manifest.ArtifactKind);
        Assert.Equal("SHA256", manifest.HashAlgorithm);
        Assert.Equal(expectedHash, manifest.ExpectedHash);
        Assert.Equal(content.Length, manifest.ExpectedSizeBytes);
        Assert.Equal(content.Length, manifest.FileSizeBytes);
    }

    [Fact]
    public async Task DownloadAsync_WhenManifestMatches_UsesFastCacheHitWithoutRehashing()
    {
        using TempDirectory temp = TempDirectory.Create();
        string destinationPath = Path.Combine(temp.Path, "install.esd");
        byte[] content = Encoding.UTF8.GetBytes("tampered-content");
        await File.WriteAllBytesAsync(destinationPath, content, TestContext.Current.CancellationToken);
        DateTimeOffset lastWriteTime = DateTimeOffset.UtcNow.AddMinutes(-3);
        File.SetLastWriteTimeUtc(destinationPath, lastWriteTime.UtcDateTime);

        await WriteManifestAsync(
            destinationPath,
            new ArtifactCacheManifest
            {
                ArtifactKind = "OperatingSystemImage",
                SourceUrl = "https://example.test/install.esd",
                HashAlgorithm = "SHA256",
                ExpectedHash = new string('A', 64),
                ExpectedSizeBytes = content.Length,
                FileSizeBytes = content.Length,
                FileLastWriteTimeUtc = lastWriteTime,
                ValidatedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-2)
            });

        var handler = new ThrowingHttpMessageHandler();
        var service = new ArtifactDownloadService(
            NullLogger<ArtifactDownloadService>.Instance,
            new HttpClient(handler));

        ArtifactDownloadResult result = await service.DownloadAsync(
            "https://example.test/install.esd",
            destinationPath,
            new string('A', 64),
            expectedSizeBytes: content.Length,
            artifactKind: "OperatingSystemImage",
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.False(result.Downloaded);
        Assert.Equal("cache-hit", result.Method);
        Assert.Equal(0, handler.RequestCount);
    }

    [Fact]
    public async Task DownloadAsync_WhenExistingFileSizeDiffers_RedownloadsWithoutHashingCache()
    {
        using TempDirectory temp = TempDirectory.Create();
        string destinationPath = Path.Combine(temp.Path, "driver.cab");
        await File.WriteAllTextAsync(destinationPath, "bad", TestContext.Current.CancellationToken);

        byte[] downloadedContent = Encoding.UTF8.GetBytes("valid driver payload");
        string expectedHash = ComputeSha256(downloadedContent);
        var handler = new StaticHttpMessageHandler(downloadedContent);
        var service = new ArtifactDownloadService(
            NullLogger<ArtifactDownloadService>.Instance,
            new HttpClient(handler));

        ArtifactDownloadResult result = await service.DownloadAsync(
            "https://example.test/driver.cab",
            destinationPath,
            expectedHash,
            expectedSizeBytes: downloadedContent.Length,
            artifactKind: "OemDriverPack",
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(result.Downloaded);
        Assert.Equal("httpclient", result.Method);
        Assert.Equal(1, handler.RequestCount);
        Assert.Equal(downloadedContent, await File.ReadAllBytesAsync(destinationPath, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task DownloadAsync_WhenDownloadedHashMatches_WritesManifestWithoutSecondFileHashPass()
    {
        using TempDirectory temp = TempDirectory.Create();
        string destinationPath = Path.Combine(temp.Path, "firmware.cab");
        byte[] downloadedContent = Encoding.UTF8.GetBytes("firmware payload");
        string expectedHash = ComputeSha256(downloadedContent);
        var service = new ArtifactDownloadService(
            NullLogger<ArtifactDownloadService>.Instance,
            new HttpClient(new StaticHttpMessageHandler(downloadedContent)));

        ArtifactDownloadResult result = await service.DownloadAsync(
            "https://example.test/firmware.cab",
            destinationPath,
            expectedHash,
            expectedSizeBytes: downloadedContent.Length,
            artifactKind: "MicrosoftUpdateCatalogFirmware",
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(result.Downloaded);
        Assert.True(File.Exists($"{destinationPath}.manifest.json"));
    }

    [Fact]
    public async Task DownloadAsync_WhenDownloadedHashDiffers_ThrowsHashVerificationError()
    {
        using TempDirectory temp = TempDirectory.Create();
        string destinationPath = Path.Combine(temp.Path, "driver.cab");
        byte[] downloadedContent = Encoding.UTF8.GetBytes("unexpected payload");
        var service = new ArtifactDownloadService(
            NullLogger<ArtifactDownloadService>.Instance,
            new HttpClient(new StaticHttpMessageHandler(downloadedContent)));

        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.DownloadAsync(
                "https://example.test/driver.cab",
                destinationPath,
                new string('B', 64),
                expectedSizeBytes: downloadedContent.Length,
                artifactKind: "MicrosoftUpdateCatalogDriver",
                cancellationToken: TestContext.Current.CancellationToken));

        Assert.Contains("Hash verification failed", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static async Task WriteManifestAsync(string destinationPath, ArtifactCacheManifest manifest)
    {
        await using FileStream stream = File.Create($"{destinationPath}.manifest.json");
        await JsonSerializer.SerializeAsync(
            stream,
            manifest,
            new JsonSerializerOptions { WriteIndented = true },
            TestContext.Current.CancellationToken);
    }

    private static string ComputeSha256(byte[] content)
    {
        return Convert.ToHexString(SHA256.HashData(content));
    }

    private sealed class StaticHttpMessageHandler(byte[] content) : HttpMessageHandler
    {
        public int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(content)
            });
        }
    }

    private sealed class ThrowingHttpMessageHandler : HttpMessageHandler
    {
        public int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;
            throw new InvalidOperationException("HTTP should not be used for a manifest-backed cache hit.");
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
            return new TempDirectory(System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"foundry-artifact-cache-{Guid.NewGuid():N}"));
        }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
