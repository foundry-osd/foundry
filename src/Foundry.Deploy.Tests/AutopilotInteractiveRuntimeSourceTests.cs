// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Net;
using System.Text.Json;
using Foundry.Deploy.Services.Autopilot;
using Foundry.Deploy.Services.Deployment;
using Foundry.Deploy.Services.Download;
using Microsoft.Extensions.Logging.Abstractions;

namespace Foundry.Deploy.Tests;

public sealed class AutopilotInteractiveRuntimeSourceTests
{
    [Fact]
    public async Task Resolve_UsesDeployVersionTag()
    {
        using var fixture = new Fixture();

        AutopilotInteractiveRuntimeOrigin origin = await fixture.Source.ResolveAsync("win-x64", TestContext.Current.CancellationToken);

        Assert.EndsWith("releases/tags/v26.11.3.1", Assert.Single(fixture.Requests));
        Assert.Equal("https://example.test/Foundry.AutopilotInteractive-win-x64.zip", origin.Source);
        Assert.Equal(fixture.Sha256.ToLowerInvariant(), origin.Sha256);
        Assert.False(origin.IsLocalArchive);
    }

    [Theory]
    [InlineData("FOUNDRY_DEPLOY_RELEASE_TAG", "v26.9.14.1")]
    [InlineData("FOUNDRY_RELEASE_TAG", "v26.9.14.1")]
    [InlineData("FOUNDRY_POSTINSTALL_RELEASE_TAG", "v26.11.3.1")]
    public async Task Resolve_UsesDeployReleaseTagOverride(string variable, string expectedTag)
    {
        using var fixture = new Fixture();
        fixture.EnvironmentValues[variable] = "v26.9.14.1";

        await fixture.Source.ResolveAsync("win-x64", TestContext.Current.CancellationToken);

        Assert.EndsWith("/tags/" + expectedTag, Assert.Single(fixture.Requests));
    }

    [Fact]
    public async Task Resolve_Throws_WhenAssetMissing()
    {
        using var fixture = new Fixture { PublishedAssetRid = "win-arm64" };

        await Assert.ThrowsAsync<InvalidDataException>(() => fixture.Source.ResolveAsync("win-x64", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Resolve_Throws_WhenDigestMissing()
    {
        using var fixture = new Fixture { PublishDigest = false };

        await Assert.ThrowsAsync<InvalidDataException>(() => fixture.Source.ResolveAsync("win-x64", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Resolve_UsesForwardedDebugArchive_WithoutNetwork()
    {
        using var fixture = new Fixture();
        fixture.MarkDebugMedia();
        fixture.ForwardArchive(fixture.ArchivePath, fixture.Sha256);

        AutopilotInteractiveRuntimeOrigin origin = await fixture.Source.ResolveAsync("win-x64", TestContext.Current.CancellationToken);

        Assert.True(origin.IsLocalArchive);
        Assert.Equal(fixture.ArchivePath, origin.Source);
        Assert.Equal(fixture.Sha256, origin.Sha256, ignoreCase: true);
        Assert.Empty(fixture.Requests);
    }

    [Theory]
    [InlineData("not-a-hash")]
    [InlineData("")]
    public async Task Resolve_Throws_WhenForwardedArchiveHasNoTrustedHash(string sha256)
    {
        using var fixture = new Fixture();
        fixture.ForwardArchive(fixture.ArchivePath, sha256);

        await Assert.ThrowsAsync<InvalidDataException>(() => fixture.Source.ResolveAsync("win-x64", TestContext.Current.CancellationToken));
        Assert.Empty(fixture.Requests);
    }

    [Fact]
    public async Task Resolve_Throws_OnDebugMediaWithoutArchive()
    {
        using var fixture = new Fixture();
        fixture.MarkDebugMedia();

        await Assert.ThrowsAsync<InvalidDataException>(() => fixture.Source.ResolveAsync("win-x64", TestContext.Current.CancellationToken));
        Assert.Empty(fixture.Requests);
    }

    [Fact]
    public void ReleaseTag_Throws_WhenDeployVersionIsNotFourPart()
    {
        Assert.Throws<InvalidDataException>(() => DeployReleaseTag.Resolve("26.11.3", _ => null, "FOUNDRY_RELEASE_TAG"));
        Assert.Equal("v26.11.3.1", DeployReleaseTag.Resolve("26.11.3.1+abcdef", _ => null, "FOUNDRY_RELEASE_TAG"));
    }

    [Fact]
    public async Task Install_ExtractsIntoTargetAndDeletesZip()
    {
        using var fixture = new Fixture();
        AutopilotInteractiveRuntimeOrigin origin = await fixture.Source.ResolveAsync("win-x64", TestContext.Current.CancellationToken);

        await fixture.Source.InstallAsync(origin, fixture.TargetRuntime, null, TestContext.Current.CancellationToken);

        Assert.Equal("app", File.ReadAllText(Path.Combine(fixture.TargetRuntime, "Foundry.AutopilotInteractive.exe")));
        Assert.Empty(Directory.GetFiles(fixture.TargetRuntime, "*.zip"));
        Assert.Equal(origin.Source, fixture.Requests[^1]);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Install_Throws_OnHashMismatch(bool localArchive)
    {
        using var fixture = new Fixture();
        var origin = new AutopilotInteractiveRuntimeOrigin(
            localArchive ? fixture.ArchivePath : "https://example.test/Foundry.AutopilotInteractive-win-x64.zip",
            new string('0', 64), localArchive);

        Exception exception = await Assert.ThrowsAnyAsync<Exception>(() =>
            fixture.Source.InstallAsync(origin, fixture.TargetRuntime, null, TestContext.Current.CancellationToken));

        Assert.True(localArchive ? exception is InvalidDataException : exception is ArtifactHashMismatchException, exception.ToString());
        Assert.Empty(Directory.GetFiles(fixture.TargetRuntime, "*.zip"));
        Assert.False(File.Exists(Path.Combine(fixture.TargetRuntime, "Foundry.AutopilotInteractive.exe")));
    }

    [Fact]
    public async Task Install_RetriesThreeTimes_ThenThrows()
    {
        using var fixture = new Fixture();
        var download = new FailingDownload();
        List<TimeSpan> delays = [];
        var source = new AutopilotInteractiveRuntimeSource(fixture.WinPeRoot, "26.11.3.1", fixture.Client, download,
            name => fixture.EnvironmentValues.GetValueOrDefault(name))
        {
            Delay = (delay, _) => { delays.Add(delay); return Task.CompletedTask; }
        };
        var origin = new AutopilotInteractiveRuntimeOrigin("https://example.test/Foundry.AutopilotInteractive-win-x64.zip", fixture.Sha256, false);

        await Assert.ThrowsAsync<HttpRequestException>(() =>
            source.InstallAsync(origin, fixture.TargetRuntime, null, TestContext.Current.CancellationToken));

        Assert.Equal(3, download.Attempts);
        Assert.Equal([TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(15)], delays);
        Assert.Empty(Directory.GetFiles(fixture.TargetRuntime, "*.zip"));
    }

    [Fact]
    public async Task Install_Throws_WhenExecutableMissing()
    {
        using var fixture = new Fixture();
        (string archive, string sha256) = AutopilotInteractiveTestRuntime.CreateArchive(fixture.Root, includeExecutable: false);

        await Assert.ThrowsAsync<InvalidDataException>(() => fixture.Source.InstallAsync(
            new AutopilotInteractiveRuntimeOrigin(archive, sha256, true), fixture.TargetRuntime, null, TestContext.Current.CancellationToken));

        Assert.Empty(Directory.GetFiles(fixture.TargetRuntime, "*.zip"));
        Assert.False(File.Exists(Path.Combine(fixture.TargetRuntime, "Foundry.AutopilotInteractive.exe")));
    }

    private sealed class FailingDownload : IArtifactDownloadService
    {
        public int Attempts { get; private set; }

        public Task<ArtifactDownloadResult> DownloadAsync(string sourceUrl, string destinationPath, string? expectedHash = null,
            long? expectedSizeBytes = null, string? artifactKind = null, CancellationToken cancellationToken = default,
            IProgress<DownloadProgress>? progress = null)
        {
            Attempts++;
            Assert.Equal("AutopilotInteractive", artifactKind);
            File.WriteAllText(destinationPath, "partial");
            throw new HttpRequestException("Release download unavailable.");
        }
    }

    private sealed class Fixture : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "Foundry.Deploy.Tests", Guid.NewGuid().ToString("N"));
        public string WinPeRoot => Path.Combine(Root, "WinPE");
        public string TargetRuntime => Path.Combine(Root, "Target", "Windows", "Temp", "Foundry", "Runtime", "AutopilotInteractive");
        public string ArchivePath { get; }
        public string Sha256 { get; }
        public List<string> Requests { get; } = [];
        public Dictionary<string, string> EnvironmentValues { get; } = [];
        public string PublishedAssetRid { get; init; } = "win-x64";
        public bool PublishDigest { get; init; } = true;
        public HttpClient Client { get; }
        public AutopilotInteractiveRuntimeSource Source { get; }

        public Fixture()
        {
            Directory.CreateDirectory(WinPeRoot);
            Directory.CreateDirectory(TargetRuntime);
            (ArchivePath, Sha256) = AutopilotInteractiveTestRuntime.CreateArchive(Root);
            byte[] archive = File.ReadAllBytes(ArchivePath);
            Client = new HttpClient(new Handler(request =>
            {
                Requests.Add(request.RequestUri!.AbsoluteUri);
                if (request.RequestUri.Host == "api.github.com")
                {
                    return new(HttpStatusCode.OK)
                    {
                        Content = new StringContent(JsonSerializer.Serialize(new
                        {
                            assets = new[]
                            {
                                new
                                {
                                    name = $"Foundry.AutopilotInteractive-{PublishedAssetRid}.zip",
                                    browser_download_url = $"https://example.test/Foundry.AutopilotInteractive-{PublishedAssetRid}.zip",
                                    digest = PublishDigest ? "sha256:" + Sha256 : null
                                }
                            }
                        }))
                    };
                }
                return new(HttpStatusCode.OK) { Content = new ByteArrayContent(archive) };
            }));
            Source = new AutopilotInteractiveRuntimeSource(WinPeRoot, "26.11.3.1", Client,
                new ArtifactDownloadService(NullLogger<ArtifactDownloadService>.Instance, Client), name => EnvironmentValues.GetValueOrDefault(name))
            {
                Delay = (_, _) => Task.CompletedTask
            };
        }

        public void MarkDebugMedia()
        {
            Directory.CreateDirectory(Path.Combine(WinPeRoot, "Config"));
            File.WriteAllText(Path.Combine(WinPeRoot, "Config", "foundry.deploy.provisioning-source.txt"), "debug");
        }

        public void ForwardArchive(string path, string sha256)
        {
            EnvironmentValues["FOUNDRY_AUTOPILOTINTERACTIVE_ARCHIVE"] = path;
            EnvironmentValues["FOUNDRY_AUTOPILOTINTERACTIVE_ARCHIVE_SHA256"] = sha256;
        }

        public void Dispose()
        {
            Client.Dispose();
            Directory.Delete(Root, recursive: true);
        }
    }

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(respond(request));
    }
}
