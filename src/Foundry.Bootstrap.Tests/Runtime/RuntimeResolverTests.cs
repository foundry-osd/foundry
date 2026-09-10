// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Net;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text.Json;
using Foundry.Bootstrap.Runtime;
using Serilog;
using Xunit;

namespace Foundry.Bootstrap.Tests.Runtime;

public sealed class RuntimeResolverTests : IDisposable
{
    private static readonly string ArchitectureFolder = RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? "arm64" : "x64";
    private static readonly string RuntimeIdentifier = "win-" + ArchitectureFolder;
    private readonly string root = Path.Combine(Path.GetTempPath(), "FoundryRuntimeTests", Guid.NewGuid().ToString("N"));
    private readonly Dictionary<string, string> environment = [];
    private readonly List<Uri> requests = [];

    [Fact]
    public async Task ExplicitArchiveFailureDoesNotFallBackToUsableCacheOrReleaseTag()
    {
        string executable = SeedCache();
        string archive = Path.Combine(root, "override.zip");
        File.WriteAllText(archive, "candidate");
        environment["FOUNDRY_CONNECT_ARCHIVE"] = archive;
        environment["FOUNDRY_CONNECT_ARCHIVE_SHA256"] = new string('0', 64);
        environment["FOUNDRY_RELEASE_TAG"] = "v-global";
        using var client = CreateClient();
        await Assert.ThrowsAsync<InvalidDataException>(() => CreateResolver(client).ResolveAsync("Foundry.Connect", false, CancellationToken.None));
        Assert.Equal("original", File.ReadAllText(executable));
        Assert.Empty(requests);
        Assert.False(File.Exists(Path.Combine(root, "Runtime", "Foundry.Connect", $"Foundry.Connect-{RuntimeIdentifier}.zip.download")));
    }

    [Fact]
    public async Task SpecificTagTakesPrecedenceAndCurrentDigestAvoidsDownload()
    {
        string executable = SeedCache($"Asset=Foundry.Connect-{RuntimeIdentifier}.zip\nArchiveSha256=" + new string('A', 64));
        environment["FOUNDRY_CONNECT_RELEASE_TAG"] = " v-specific ";
        environment["FOUNDRY_RELEASE_TAG"] = "v-global";
        using var client = CreateClient(Release(new string('A', 64)));
        Assert.Equal(executable, await CreateResolver(client).ResolveAsync("Foundry.Connect", false, CancellationToken.None));
        Assert.Single(requests);
        Assert.EndsWith("/tags/v-specific", requests[0].AbsoluteUri);
    }

    [Fact]
    public async Task ReleaseDigestMismatchKeepsExistingCache()
    {
        string executable = SeedCache();
        using var client = CreateClient(Release(new string('0', 64)), new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("candidate") });
        Assert.Equal(executable, await CreateResolver(client).ResolveAsync("Foundry.Connect", false, CancellationToken.None));
        Assert.Equal("original", File.ReadAllText(executable));
    }

    [Fact]
    public async Task SkipLookupAcceptsLegacyExecutableWithoutManifest()
    {
        string executable = SeedCache();
        using var client = CreateClient();
        Assert.Equal(executable, await CreateResolver(client).ResolveAsync("Foundry.Connect", true, CancellationToken.None));
        Assert.Empty(requests);
    }

    [Fact]
    public async Task MissingAssetIsFatalEvenWithExistingCache()
    {
        SeedCache();
        using var client = CreateClient(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"tag_name\":\"v1\",\"assets\":[]}") });
        await Assert.ThrowsAsync<InvalidDataException>(() => CreateResolver(client).ResolveAsync("Foundry.Connect", false, CancellationToken.None));
    }

    [Fact]
    public void FailedPromotionRestoresPreviousCache()
    {
        string executable = SeedCache();
        string cache = Path.GetDirectoryName(executable)!;
        string staging = cache + ".staging";
        Directory.CreateDirectory(staging);
        var store = new RuntimeCache((source, destination) =>
        {
            if (source == staging) throw new IOException("promotion failed");
            Directory.Move(source, destination);
        });
        Assert.Throws<IOException>(() => store.Promote(staging, cache));
        Assert.Equal("original", File.ReadAllText(executable));
        Assert.False(Directory.Exists(cache + ".previous"));
    }

    [Fact]
    public async Task OfflineLookupUsesExistingCacheWithoutManifestAndReportsSafeWarning()
    {
        string executable = SeedCache();
        var warnings = new List<string>();
        using var client = CreateClient();
        Assert.Equal(executable, await CreateResolver(client, warnings.Add).ResolveAsync("Foundry.Connect", false, TestContext.Current.CancellationToken));
        Assert.Single(requests);
        Assert.Equal("Release lookup failed. Continuing with the cached application.", Assert.Single(warnings));
    }

    [Fact]
    public async Task DebugDeployUsesEmbeddedArchiveBeforeExistingCacheWithoutNetwork()
    {
        ProvisionExtractor();
        string embedded = Path.Combine(root, "Seed", "Foundry.Deploy.zip");
        CreateArchive(embedded, "Foundry.Deploy.exe", "embedded");
        string cache = Path.Combine(root, "Runtime", "Foundry.Deploy", RuntimeIdentifier);
        Directory.CreateDirectory(cache);
        File.WriteAllText(Path.Combine(cache, "Foundry.Deploy.exe"), "old");
        using var client = CreateClient();
        string executable = await CreateResolver(client).ResolveAsync("Foundry.Deploy", true, CancellationToken.None);
        Assert.Equal("embedded", File.ReadAllText(executable));
        Assert.Empty(requests);
        Assert.Contains($"Asset=Foundry.Deploy-{RuntimeIdentifier}.zip", File.ReadAllText(Path.Combine(cache, "manifest")));
        Assert.False(Directory.Exists(cache + ".staging"));
        Assert.False(Directory.Exists(cache + ".previous"));
    }

    [Fact]
    public async Task MissingStagedExecutableDoesNotReplaceCache()
    {
        ProvisionExtractor();
        string executable = SeedCache();
        string archive = Path.Combine(root, "override.zip");
        CreateArchive(archive, "wrong.exe", "candidate");
        environment["FOUNDRY_CONNECT_ARCHIVE"] = archive;
        using var client = CreateClient();
        await Assert.ThrowsAsync<FileNotFoundException>(() => CreateResolver(client).ResolveAsync("Foundry.Connect", false, CancellationToken.None));
        Assert.Equal("original", File.ReadAllText(executable));
        Assert.False(Directory.Exists(Path.GetDirectoryName(executable) + ".staging"));
    }

    [Fact]
    public async Task TraversalArchiveIsRejectedBeforeExtraction()
    {
        string executable = SeedCache();
        string archive = Path.Combine(root, "override.zip");
        CreateArchive(archive, "../escape.exe", "candidate");
        environment["FOUNDRY_CONNECT_ARCHIVE"] = archive;
        using var client = CreateClient();
        await Assert.ThrowsAsync<InvalidDataException>(() => CreateResolver(client).ResolveAsync("Foundry.Connect", false, CancellationToken.None));
        Assert.Equal("original", File.ReadAllText(executable));
        Assert.False(File.Exists(Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(executable)!)!, "escape.exe")));
    }

    [Fact]
    public async Task CancellationDoesNotReturnFallbackCache()
    {
        SeedCache();
        using var client = CreateClient();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => CreateResolver(client).ResolveAsync("Foundry.Connect", false, new CancellationToken(true)));
        Assert.Empty(requests);
    }

    [Fact]
    public async Task OfflineDeployWithoutCacheExtractsEmbeddedFallback()
    {
        ProvisionExtractor();
        CreateArchive(Path.Combine(root, "Seed", "Foundry.Deploy.zip"), "Foundry.Deploy.exe", "embedded");
        using var client = CreateClient();
        string executable = await CreateResolver(client).ResolveAsync("Foundry.Deploy", false, CancellationToken.None);
        Assert.Equal("embedded", File.ReadAllText(executable));
        Assert.Single(requests);
    }

    [Fact]
    public async Task ManifestForDifferentAssetForcesRefreshDespiteMatchingDigest()
    {
        string executable = SeedCache($"Asset=Foundry.Deploy-{RuntimeIdentifier}.zip\nArchiveSha256=" + new string('A', 64));
        using var client = CreateClient(Release(new string('A', 64)), new HttpResponseMessage(HttpStatusCode.NotFound));
        Assert.Equal(executable, await CreateResolver(client).ResolveAsync("Foundry.Connect", false, CancellationToken.None));
        Assert.Equal(2, requests.Count);
    }

    private static void CreateArchive(string path, string entry, string contents)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using ZipArchive archive = ZipFile.Open(path, ZipArchiveMode.Create);
        using var writer = new StreamWriter(archive.CreateEntry(entry).Open());
        writer.Write(contents);
    }

    private void ProvisionExtractor()
    {
        DirectoryInfo? repository = new(AppContext.BaseDirectory);
        while (repository is not null && !File.Exists(Path.Combine(repository.FullName, "src", "Foundry.Core", "Assets", "7z", ArchitectureFolder, "7za.exe"))) repository = repository.Parent;
        Assert.NotNull(repository);
        string destination = Path.Combine(root, "Tools", "7zip", ArchitectureFolder);
        Directory.CreateDirectory(destination);
        File.Copy(Path.Combine(repository.FullName, "src", "Foundry.Core", "Assets", "7z", ArchitectureFolder, "7za.exe"), Path.Combine(destination, "7za.exe"));
    }

    private RuntimeResolver CreateResolver(HttpClient client, Action<string>? warning = null) => new(root, Path.Combine(root, "Runtime"), RuntimeIdentifier, client,
        new LoggerConfiguration().CreateLogger(), getEnvironmentVariable: key => environment.GetValueOrDefault(key), warning: warning);

    private string SeedCache(string? manifest = null)
    {
        string cache = Path.Combine(root, "Runtime", "Foundry.Connect", RuntimeIdentifier);
        Directory.CreateDirectory(cache);
        string executable = Path.Combine(cache, "Foundry.Connect.exe");
        File.WriteAllText(executable, "original");
        if (manifest is not null) File.WriteAllText(Path.Combine(cache, "manifest"), manifest);
        return executable;
    }

    private HttpClient CreateClient(params HttpResponseMessage[] responses) => new(new Handler(requests, new Queue<HttpResponseMessage>(responses)));

    private static HttpResponseMessage Release(string digest) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(JsonSerializer.Serialize(new
        {
            tag_name = "v1.2.3",
            assets = new[] { new { name = $"Foundry.Connect-{RuntimeIdentifier}.zip", digest = "sha256:" + digest, browser_download_url = "https://example.test/payload.zip" } }
        }))
    };

    public void Dispose()
    {
        if (Directory.Exists(root)) Directory.Delete(root, true);
    }

    private sealed class Handler(List<Uri> requests, Queue<HttpResponseMessage> responses) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            requests.Add(request.RequestUri!);
            return Task.FromResult(responses.Count > 0 ? responses.Dequeue() : new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }
}
