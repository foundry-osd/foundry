// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using Foundry.Deploy.Services.Deployment.PreOobe;
using Foundry.Deploy.Services.Download;
using Microsoft.Extensions.Logging.Abstractions;

namespace Foundry.Deploy.Tests;

public sealed class PostInstallRuntimeRecoveryTests
{
    [Fact]
    public async Task AuthenticatedContractOneCachedRuntimeRemainsAvailable()
    {
        using var fixture = new Fixture();
        File.WriteAllBytes(fixture.CacheArchive, fixture.Archive);
        using var prepared = await fixture.Recovery.AcquireAsync("win-x64", fixture.CacheRoot, TestContext.Current.CancellationToken);
        Assert.Equal(1, prepared.RuntimeManifest.ContractVersion);
        Assert.Equal(fixture.Archive, File.ReadAllBytes(fixture.CacheArchive));
        Assert.Equal("original", File.ReadAllText(fixture.OriginalArchive));
        prepared.Dispose();
        Assert.Empty(Directory.GetDirectories(Path.Combine(fixture.WinPeRoot, "Execution")));
    }

    [Theory]
    [InlineData("win-x64")]
    [InlineData("win-arm64")]
    public async Task MissingRuntime_DownloadsMatchingReleaseAndPublishesReusableUsbArchive(string rid)
    {
        using var fixture = new Fixture(rid);
        using var prepared = await fixture.RecoverAsync(rid);
        Assert.Equal("fixture", File.ReadAllText(Path.Combine(prepared.RuntimeDirectory, "Foundry.PostInstall.exe")));
        Assert.StartsWith(fixture.WinPeRoot, prepared.RuntimeDirectory);
        Assert.Equal(fixture.Archive, File.ReadAllBytes(fixture.CacheArchive));
        Assert.Equal("original", File.ReadAllText(fixture.OriginalArchive));
        Assert.Equal(["https://api.github.com/repos/foundry-osd/foundry/releases/tags/v26.9.29.1", $"https://example.test/Foundry.PostInstall-{rid}.zip"], fixture.Requests);
        string workspace = Path.GetDirectoryName(prepared.RuntimeDirectory)!;
        prepared.Dispose();
        Assert.False(Directory.Exists(workspace));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UsbCache_VerifiesBytesAndDownloadsOnlyWhenInvalid(bool corrupt)
    {
        using var fixture = new Fixture();
        File.WriteAllBytes(fixture.CacheArchive, corrupt ? [1, 2, 3] : fixture.Archive);
        using var prepared = await fixture.RecoverAsync();
        Assert.Equal(corrupt ? 2 : 1, fixture.Requests.Count);
        Assert.Equal(fixture.Archive, File.ReadAllBytes(fixture.CacheArchive));
        Assert.Equal("win-x64", prepared.RuntimeIdentifier);
    }

    [Fact]
    public async Task InvalidDownloadedHash_PreservesPreviousCacheAndRemovesWorkspace()
    {
        using var fixture = new Fixture { CorruptDownload = true };
        byte[] previous = [4, 5, 6];
        File.WriteAllBytes(fixture.CacheArchive, previous);
        await Assert.ThrowsAnyAsync<IOException>(() => fixture.RecoverAsync());
        Assert.Equal(previous, File.ReadAllBytes(fixture.CacheArchive));
        Assert.Empty(Directory.GetDirectories(Path.Combine(fixture.WinPeRoot, "Execution")));
    }

    [Fact]
    public async Task IncompatibleRuntime_IsNotPublishedToCache()
    {
        using var fixture = new Fixture();
        await Assert.ThrowsAnyAsync<IOException>(() => fixture.RecoverAsync("win-arm64"));
        Assert.EndsWith("Foundry.PostInstall-win-arm64.zip", fixture.Requests.Last());
        Assert.False(File.Exists(fixture.CacheArchive));
    }

    [Fact]
    public async Task UnwritableCache_DoesNotPreventUsingVerifiedRuntime()
    {
        using var fixture = new Fixture();
        string blockedRoot = Path.Combine(fixture.Root, "blocked-cache");
        File.WriteAllText(blockedRoot, "not a directory");
        using var prepared = await fixture.Recovery.AcquireAsync("win-x64", blockedRoot, TestContext.Current.CancellationToken);
        Assert.True(File.Exists(Path.Combine(prepared.RuntimeDirectory, "Launch.cmd")));
    }

    [Fact]
    public async Task Cancellation_DoesNotDownloadOrChangeCache()
    {
        using var fixture = new Fixture();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fixture.Recovery.AcquireAsync("win-x64", fixture.CacheRoot, cancellation.Token));
        Assert.Empty(fixture.Requests);
        Assert.False(File.Exists(fixture.CacheArchive));
    }

    [Fact]
    public async Task OfflineLookup_DoesNotTrustCachedUpdateWithoutReleaseDigest()
    {
        using var fixture = new Fixture { ReleaseStatus = HttpStatusCode.Forbidden };
        File.WriteAllBytes(fixture.CacheArchive, fixture.Archive);
        await Assert.ThrowsAsync<PostInstallRuntimeUnavailableException>(() => fixture.RecoverAsync());
        Assert.Single(fixture.Requests);
        Assert.Equal(fixture.Archive, File.ReadAllBytes(fixture.CacheArchive));
    }

    [Fact]
    public async Task DownloadTimeout_ReportsRuntimeUnavailableAndRemovesWorkspace()
    {
        using var fixture = new Fixture { DownloadTimeout = true };
        var exception = await Assert.ThrowsAsync<PostInstallRuntimeUnavailableException>(() => fixture.RecoverAsync());
        Assert.IsType<TimeoutException>(exception.InnerException);
        Assert.Empty(Directory.GetDirectories(Path.Combine(fixture.WinPeRoot, "Execution")));
        Assert.False(File.Exists(fixture.CacheArchive));
    }

    [Fact]
    public async Task DebugMedia_DoesNotFetchAnUnrelatedPublishedRuntime()
    {
        using var fixture = new Fixture();
        Directory.CreateDirectory(Path.Combine(fixture.WinPeRoot, "Config"));
        File.WriteAllText(Path.Combine(fixture.WinPeRoot, "Config", "foundry.deploy.provisioning-source.txt"), "debug");
        await Assert.ThrowsAsync<PostInstallRuntimeUnavailableException>(() => fixture.RecoverAsync());
        Assert.Empty(fixture.Requests);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ArchiveOverride_RequiresTrustedHashWithoutChangingUsbCache(bool valid)
    {
        using var fixture = new Fixture();
        fixture.EnvironmentValues["FOUNDRY_POSTINSTALL_ARCHIVE"] = Path.Combine(fixture.Root, "runtime.zip");
        fixture.EnvironmentValues["FOUNDRY_POSTINSTALL_ARCHIVE_SHA256"] = valid ? Convert.ToHexString(SHA256.HashData(fixture.Archive)) : new string('0', 64);
        if (valid)
        {
            using var prepared = await fixture.RecoverAsync();
            Assert.True(File.Exists(Path.Combine(prepared.RuntimeDirectory, "Launch.cmd")));
        }
        else await Assert.ThrowsAsync<PostInstallRuntimeUnavailableException>(() => fixture.RecoverAsync());
        Assert.Empty(fixture.Requests);
        Assert.False(File.Exists(fixture.CacheArchive));
    }

    [Theory]
    [InlineData("FOUNDRY_RELEASE_TAG")]
    [InlineData("FOUNDRY_DEPLOY_RELEASE_TAG")]
    [InlineData("FOUNDRY_POSTINSTALL_RELEASE_TAG")]
    public async Task ExplicitReleaseOverride_IsUsedInsteadOfLatest(string variable)
    {
        using var fixture = new Fixture();
        fixture.EnvironmentValues[variable] = "v26.9.14.1";
        using var prepared = await fixture.RecoverAsync();
        Assert.EndsWith("/tags/v26.9.14.1", fixture.Requests[0]);
    }

    internal sealed class Fixture : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "Foundry.Deploy.Tests", Guid.NewGuid().ToString("N"));
        public string WinPeRoot => Path.Combine(Root, "WinPE");
        public string CacheRoot => Path.Combine(Root, "USB", "Runtime");
        public string CacheArchive => Path.Combine(CacheRoot, "Foundry.PostInstall", runtimeIdentifier, "current.zip");
        public string OriginalArchive => Path.Combine(Path.GetDirectoryName(CacheArchive)!, "original.zip");
        public byte[] Archive { get; }
        public List<string> Requests { get; } = [];
        public Dictionary<string, string> EnvironmentValues { get; } = [];
        public bool CorruptDownload { get; init; }
        public bool DownloadTimeout { get; init; }
        public HttpStatusCode ReleaseStatus { get; init; } = HttpStatusCode.OK;
        public PostInstallRuntimeRecovery Recovery { get; }
        private readonly HttpClient client;
        private readonly string runtimeIdentifier;

        public Fixture(string rid = "win-x64")
        {
            runtimeIdentifier = rid;
            Directory.CreateDirectory(WinPeRoot);
            string executable = NativeRuntimeFixture.CreateFiles(Root);
            if (rid != "win-x64")
            {
                string directory = Path.GetDirectoryName(executable)!;
                var manifest = JsonSerializer.Deserialize<Foundry.Core.Models.PreOobe.PostInstallRuntimeManifest>(
                    File.ReadAllText(Path.Combine(directory, "foundry.postinstall.json")), Services.Configuration.ConfigurationJsonDefaults.SerializerOptions)!;
                NativeRuntimeFixture.WriteManifest(directory, manifest with { RuntimeIdentifier = rid });
            }
            string archive = Path.Combine(Root, "runtime.zip");
            ZipFile.CreateFromDirectory(Path.GetDirectoryName(executable)!, archive);
            Archive = File.ReadAllBytes(archive);
            Directory.CreateDirectory(Path.GetDirectoryName(CacheArchive)!);
            File.WriteAllText(OriginalArchive, "original");
            client = new HttpClient(new Handler(request =>
            {
                Requests.Add(request.RequestUri!.AbsoluteUri);
                if (request.RequestUri.Host == "api.github.com")
                {
                    return new(ReleaseStatus)
                    {
                        Content = new StringContent(JsonSerializer.Serialize(new
                        {
                            assets = new[] { "win-x64", "win-arm64" }.Select(rid => new
                            {
                                name = $"Foundry.PostInstall-{rid}.zip",
                                browser_download_url = $"https://example.test/Foundry.PostInstall-{rid}.zip",
                                digest = "sha256:" + Convert.ToHexString(SHA256.HashData(Archive))
                            })
                        }))
                    };
                }
                if (DownloadTimeout) throw new TimeoutException("The payload transfer timed out.");
                return new(HttpStatusCode.OK) { Content = new ByteArrayContent(CorruptDownload ? [9] : Archive) };
            }));
            Recovery = new PostInstallRuntimeRecovery(WinPeRoot, "26.9.29.1", client,
                new ArtifactDownloadService(NullLogger<ArtifactDownloadService>.Instance, client), name => EnvironmentValues.GetValueOrDefault(name));
        }

        public Task<PreOobePreparedContent> RecoverAsync(string rid = "win-x64") =>
            Recovery.AcquireAsync(rid, CacheRoot, TestContext.Current.CancellationToken);

        public void Dispose()
        {
            client.Dispose();
            Directory.Delete(Root, recursive: true);
        }
    }

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(respond(request));
    }
}
