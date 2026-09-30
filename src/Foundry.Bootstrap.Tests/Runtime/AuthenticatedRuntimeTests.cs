// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using Foundry.Bootstrap.Diagnostics;
using Foundry.Bootstrap.Processes;
using Foundry.Bootstrap.Runtime;
using Foundry.Bootstrap.SystemPreparation;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using Xunit;

namespace Foundry.Bootstrap.Tests.Runtime;

public sealed class AuthenticatedRuntimeTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "FoundryAuthenticatedRuntime-" + Guid.NewGuid().ToString("N"));
    private readonly List<Uri> requests = [];
    private readonly Dictionary<string, string> environment = [];
    private string BootRoot => Path.Combine(root, "BootImage");
    private string CacheRoot => Path.Combine(root, "Usb", "Runtime");

    [Fact]
    public async Task CoordinatorLaunchesVerifiedConnectUpdatesOnlineAndOriginalConnectOffline()
    {
        SeedOriginal();
        byte[] update = ArchiveBytes("updated");
        using var online = Client(Release(Hash(update)), Payload(update), Release(Hash(update)));
        using var offline = Client();
        using var logger = new LoggerConfiguration().CreateLogger();
        var boundary = new ConnectBoundary();
        var context = new BootstrapContext(BootRoot, CacheRoot, "win-x64", "TEST", null, false, false);

        foreach (HttpClient client in new[] { online, online, offline })
        {
            var coordinator = new BootstrapCoordinator(context, Resolver(client, logger), boundary, boundary, boundary, logger, _ => { });
            Assert.Equal(BootstrapOutcome.Cancelled, (await coordinator.RunAsync(TestContext.Current.CancellationToken)).Outcome);
        }

        Assert.Equal(["updated", "updated", "original"], boundary.LaunchedContent);
        Assert.Equal(3, requests.Count(request => request.Host == "api.github.com"));
        Assert.Equal(update, File.ReadAllBytes(CurrentArchive));
    }

    [Fact]
    public async Task StalledConnectLookupYieldsToOriginalWithoutCancellingBoot()
    {
        SeedOriginal();
        using var client = new HttpClient(new StalledHandler());
        using var logger = new LoggerConfiguration().CreateLogger();
        string executable = await Resolver(client, logger).ResolveAsync("Foundry.Connect", false, TestContext.Current.CancellationToken);
        Assert.Equal("original", File.ReadAllText(executable));
        Assert.False(TestContext.Current.CancellationToken.IsCancellationRequested);
    }

    [Theory]
    [InlineData("win-x64", "Foundry.Connect")]
    [InlineData("win-arm64", "Foundry.Deploy")]
    [InlineData("win-x64", "Foundry.PostInstall")]
    [InlineData("win-arm64", "Foundry.PostInstall")]
    public async Task OriginalArchiveAuthenticatesAllFilesWithoutImportingLooseCacheContent(string rid, string application)
    {
        string original = SeedOriginal(application, rid);
        string cache = Path.GetDirectoryName(original)!;
        File.WriteAllText(Path.Combine(cache, application + ".exe"), "substituted");
        File.WriteAllText(Path.Combine(cache, "Foundry.Core.dll"), "substituted dependency");
        File.WriteAllText(Path.Combine(cache, "extra.dll"), "injected");
        using var client = Client();
        using var logger = new LoggerConfiguration().CreateLogger();

        string executable = await Resolver(client, logger, rid).ResolveAsync(application, true, TestContext.Current.CancellationToken);

        Assert.StartsWith(Path.Combine(BootRoot, "Execution") + Path.DirectorySeparatorChar, executable);
        Assert.Equal("original", File.ReadAllText(executable));
        Assert.Equal("trusted dependency", File.ReadAllText(Path.Combine(Path.GetDirectoryName(executable)!, "Foundry.Core.dll")));
        Assert.False(File.Exists(Path.Combine(Path.GetDirectoryName(executable)!, "extra.dll")));
        Assert.Empty(requests);
        Assert.True(File.Exists(original));
    }

    [Theory]
    [InlineData("Foundry.Connect.exe")]
    [InlineData("Foundry.Core.dll")]
    public async Task ChangedOriginalArchiveCannotBeAuthorizedByAdjacentMetadata(string changedFile)
    {
        string archive = SeedOriginal();
        using (ZipArchive zip = ZipFile.Open(archive, ZipArchiveMode.Update))
        {
            zip.GetEntry(changedFile)!.Delete();
            using var writer = new StreamWriter(zip.CreateEntry(changedFile).Open());
            writer.Write("substituted");
        }
        File.WriteAllText(Path.Combine(Path.GetDirectoryName(archive)!, "manifest"), "ArchiveSha256=" + Hash(archive));
        using var client = Client();
        using var logger = new LoggerConfiguration().CreateLogger();

        await Assert.ThrowsAsync<InvalidDataException>(() => Resolver(client, logger).ResolveAsync(
            "Foundry.Connect", true, TestContext.Current.CancellationToken));

        AssertNoPreparedPayload();
        Assert.Empty(requests);
    }

    [Fact]
    public async Task OfflineRebootUsesOriginalEvenAfterAnOnlineUpdate()
    {
        string original = SeedOriginal();
        byte[] update = ArchiveBytes("updated");
        using var online = Client(Release(Hash(update)), Payload(update));
        using var logger = new LoggerConfiguration().CreateLogger();
        string updatedExecutable = await Resolver(online, logger).ResolveAsync("Foundry.Connect", false, TestContext.Current.CancellationToken);
        Assert.Equal("updated", File.ReadAllText(updatedExecutable));
        Assert.Equal(update, File.ReadAllBytes(CurrentArchive));
        Assert.Equal("original", ReadArchiveEntry(original, "Foundry.Connect.exe"));

        using var offline = Client();
        var warnings = new List<string>();
        string offlineExecutable = await Resolver(offline, logger, warnings: warnings).ResolveAsync(
            "Foundry.Connect", false, TestContext.Current.CancellationToken);

        Assert.Equal("original", File.ReadAllText(offlineExecutable));
        Assert.Contains(warnings, warning => warning.Contains("original", StringComparison.OrdinalIgnoreCase));
        Assert.NotEqual(updatedExecutable, offlineExecutable);
        Assert.True(File.Exists(updatedExecutable));
    }

    [Theory]
    [InlineData("win-x64")]
    [InlineData("win-arm64")]
    public async Task PostInstallUpdatesAndReusesItsOwnCacheAndRequiresAuthenticatedOriginalOffline(string rid)
    {
        const string application = "Foundry.PostInstall";
        string original = SeedOriginal(application, rid);
        string current = Path.Combine(CacheRoot, application, rid, "current.zip");
        byte[] update = ArchiveBytes("updated", application);
        environment["FOUNDRY_DEPLOY_RELEASE_TAG"] = "must-not-select-deploy-release";
        using var online = Client(Release(Hash(update), application: application, rid: rid), Payload(update),
            Release(Hash(update), application: application, rid: rid));
        using var logger = new LoggerConfiguration().CreateLogger();

        for (int attempt = 0; attempt < 2; attempt++)
        {
            string executable = await Resolver(online, logger, rid).ResolveAsync(application, false, TestContext.Current.CancellationToken);
            Assert.Equal("updated", File.ReadAllText(executable));
        }

        Assert.Equal(update, File.ReadAllBytes(current));
        Assert.Equal(2, requests.Count(request => request.AbsolutePath.EndsWith("/releases/latest", StringComparison.Ordinal)));
        Assert.Equal(1, requests.Count(request => request.Host == "example.test"));
        using var offline = Client();
        string fallback = await Resolver(offline, logger, rid).ResolveAsync(application, false, TestContext.Current.CancellationToken);
        Assert.Equal("original", File.ReadAllText(fallback));
        File.Delete(original);
        await Assert.ThrowsAsync<InvalidDataException>(() => Resolver(offline, logger, rid).ResolveAsync(
            application, false, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task CurrentArchiveIsRehashedAgainstFreshMetadataAndExecutionUsesTheCopiedBytes()
    {
        SeedOriginal();
        byte[] update = ArchiveBytes("updated");
        File.WriteAllBytes(CurrentArchive, update);
        using var client = Client(Release(Hash(update)));
        using var logger = new LoggerConfiguration().CreateLogger();
        bool mutated = false;
        var resolver = Resolver(client, logger, progress: value =>
        {
            if (!mutated && value.Phase == RuntimeProgressPhase.Verification && value.BytesReceived > 0)
            {
                mutated = true;
                File.WriteAllBytes(CurrentArchive, ArchiveBytes("substituted"));
            }
        });

        string executable = await resolver.ResolveAsync("Foundry.Connect", false, TestContext.Current.CancellationToken);

        Assert.True(mutated);
        Assert.Equal("updated", File.ReadAllText(executable));
        Assert.Single(requests);
        Assert.Equal("api.github.com", requests[0].Host);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Runtime")]
    [InlineData("Runtime/../Runtime")]
    public async Task OnlineIsoUpdateDoesNotCreateACurrentArchive(string relativeRuntimeRoot)
    {
        string runtimeRoot = Path.Combine(BootRoot, relativeRuntimeRoot);
        string original = SeedOriginal(runtimeRoot: runtimeRoot);
        byte[] update = ArchiveBytes("updated");
        using var client = Client(Release(Hash(update)), Payload(update));
        using var logger = new LoggerConfiguration().CreateLogger();

        string executable = await Resolver(client, logger, runtimeRoot: runtimeRoot).ResolveAsync(
            "Foundry.Connect", false, TestContext.Current.CancellationToken);

        Assert.Equal("updated", File.ReadAllText(executable));
        Assert.False(File.Exists(Path.Combine(runtimeRoot, "Foundry.Connect", "win-x64", "current.zip")));
        Assert.Equal("original", ReadArchiveEntry(original, "Foundry.Connect.exe"));
        Assert.Empty(Directory.EnumerateFiles(Path.Combine(BootRoot, "Execution"), "*.zip", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task OnlineIsoUpdateDoesNotReadAnExistingCurrentArchive()
    {
        string runtimeRoot = Path.Combine(BootRoot, "Runtime");
        SeedOriginal(runtimeRoot: runtimeRoot);
        string current = Path.Combine(runtimeRoot, "Foundry.Connect", "win-x64", "current.zip");
        byte[] update = ArchiveBytes("updated");
        File.WriteAllBytes(current, update);
        using var client = Client(Release(Hash(update)), Payload(update));
        using var logger = new LoggerConfiguration().CreateLogger();

        string executable = await Resolver(client, logger, runtimeRoot: runtimeRoot).ResolveAsync(
            "Foundry.Connect", false, TestContext.Current.CancellationToken);

        Assert.Equal("updated", File.ReadAllText(executable));
        Assert.Equal(2, requests.Count);
        Assert.Equal("example.test", requests[1].Host);
    }

    [Fact]
    public async Task RuntimeCacheBesideWinPeRootRemainsPersistentAndReusable()
    {
        string runtimeRoot = BootRoot + "-Usb";
        SeedOriginal(runtimeRoot: runtimeRoot);
        byte[] update = ArchiveBytes("updated");
        using var client = Client(Release(Hash(update)), Payload(update), Release(Hash(update)));
        using var logger = new LoggerConfiguration().CreateLogger();
        var resolver = Resolver(client, logger, runtimeRoot: runtimeRoot);

        for (int attempt = 0; attempt < 2; attempt++)
        {
            string executable = await resolver.ResolveAsync("Foundry.Connect", false, TestContext.Current.CancellationToken);
            Assert.Equal("updated", File.ReadAllText(executable));
        }

        Assert.Equal(update, File.ReadAllBytes(Path.Combine(runtimeRoot, "Foundry.Connect", "win-x64", "current.zip")));
        Assert.Equal(1, requests.Count(request => request.Host == "example.test"));
    }

    [Fact]
    public async Task ReleaseRemovesOnlyItsPreparedWorkspaceAndBundleWhilePreservingArchives()
    {
        string original = SeedOriginal();
        byte[] originalBytes = File.ReadAllBytes(original);
        byte[] update = ArchiveBytes("updated");
        File.WriteAllBytes(CurrentArchive, update);
        using var client = Client(Release(Hash(update)));
        using var logger = new LoggerConfiguration().CreateLogger();
        var resolver = Resolver(client, logger);
        string executable = await resolver.ResolveAsync("Foundry.Connect", false, TestContext.Current.CancellationToken);
        string otherExecutable = await resolver.ResolveAsync("Foundry.Connect", true, TestContext.Current.CancellationToken);
        string payload = Path.GetDirectoryName(executable)!;
        string workspace = Directory.GetParent(payload)!.FullName;
        string bundle = Path.Combine(payload, ".bundle", "launch", "native.dll");
        Directory.CreateDirectory(Path.GetDirectoryName(bundle)!);
        File.WriteAllText(bundle, "native runtime fixture");

        resolver.Release(executable);
        resolver.Release(executable);

        Assert.False(Directory.Exists(workspace));
        Assert.True(File.Exists(otherExecutable));
        Assert.Equal(originalBytes, File.ReadAllBytes(original));
        Assert.Equal(update, File.ReadAllBytes(CurrentArchive));
    }

    [Fact]
    public async Task ReleaseIgnoresPathsThatAreNotItsRegisteredExecutables()
    {
        SeedOriginal();
        using var client = Client();
        using var logger = new LoggerConfiguration().CreateLogger();
        var resolver = Resolver(client, logger);
        string executable = await resolver.ResolveAsync("Foundry.Connect", true, TestContext.Current.CancellationToken);
        string otherExecutable = await Resolver(client, logger).ResolveAsync("Foundry.Connect", true, TestContext.Current.CancellationToken);
        string dependency = Path.Combine(Path.GetDirectoryName(executable)!, "Foundry.Core.dll");
        string unrelated = Path.Combine(root, "unrelated.exe");
        File.WriteAllText(unrelated, "unrelated executable");

        resolver.Release(otherExecutable);
        resolver.Release(dependency);
        resolver.Release(unrelated);

        Assert.True(File.Exists(executable));
        Assert.True(File.Exists(otherExecutable));
        Assert.Equal("trusted dependency", File.ReadAllText(dependency));
        Assert.Equal("unrelated executable", File.ReadAllText(unrelated));
    }

    [Fact]
    public async Task ReleaseLogsLockedWorkspaceCleanupWithoutThrowing()
    {
        SeedOriginal();
        using var client = Client();
        var sink = new CollectingSink();
        using var logger = new LoggerConfiguration().WriteTo.Sink(sink).CreateLogger();
        var resolver = Resolver(client, logger);
        string executable = await resolver.ResolveAsync("Foundry.Connect", true, TestContext.Current.CancellationToken);
        using var lockedExecutable = new FileStream(executable, FileMode.Open, FileAccess.Read, FileShare.None);

        resolver.Release(executable);

        Assert.True(File.Exists(executable));
        Assert.Contains(sink.Events, entry => entry.Level == LogEventLevel.Warning &&
            entry.Exception is IOException or UnauthorizedAccessException);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("sha256:abc")]
    [InlineData("sha512:1234")]
    public async Task MissingOrUnsupportedReleaseDigestCannotAuthorizeCachedOrDownloadedCode(string? digest)
    {
        SeedOriginal();
        File.WriteAllBytes(CurrentArchive, ArchiveBytes("substituted"));
        using var client = Client(Release(digest, addPrefix: false));
        using var logger = new LoggerConfiguration().CreateLogger();

        string executable = await Resolver(client, logger).ResolveAsync("Foundry.Connect", false, TestContext.Current.CancellationToken);

        Assert.Equal("original", File.ReadAllText(executable));
        Assert.Single(requests);
    }

    [Fact]
    public async Task FailedDownloadValidationPreservesTheOriginalAndPreviousUpdate()
    {
        string original = SeedOriginal();
        string originalHash = Hash(original);
        byte[] previous = ArchiveBytes("previous");
        File.WriteAllBytes(CurrentArchive, previous);
        using var client = Client(Release(new string('0', 64)), Payload(ArchiveBytes("corrupt candidate")));
        using var logger = new LoggerConfiguration().CreateLogger();

        string executable = await Resolver(client, logger).ResolveAsync("Foundry.Connect", false, TestContext.Current.CancellationToken);

        Assert.Equal("original", File.ReadAllText(executable));
        Assert.Equal(originalHash, Hash(original));
        Assert.Equal(previous, File.ReadAllBytes(CurrentArchive));
        Assert.Equal(2, requests.Count);
    }

    [Fact]
    public async Task CorruptCurrentArchiveDownloadsAndVerifiesAReplacement()
    {
        SeedOriginal();
        File.WriteAllBytes(CurrentArchive, ArchiveBytes("substituted"));
        byte[] update = ArchiveBytes("updated");
        using var client = Client(Release(Hash(update)), Payload(update));
        using var logger = new LoggerConfiguration().CreateLogger();

        string executable = await Resolver(client, logger).ResolveAsync("Foundry.Connect", false, TestContext.Current.CancellationToken);

        Assert.Equal("updated", File.ReadAllText(executable));
        Assert.Equal(update, File.ReadAllBytes(CurrentArchive));
        Assert.Equal(2, requests.Count);
    }

    [Fact]
    public async Task CancellationDuringExtractionNeverFallsBackAndRemovesIncompletePayload()
    {
        string original = SeedOriginal();
        using var client = Client();
        using var logger = new LoggerConfiguration().CreateLogger();
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var resolver = Resolver(client, logger, progress: value =>
        {
            if (value.Phase == RuntimeProgressPhase.Extraction && value.BytesReceived > 0) cancellation.Cancel();
        });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => resolver.ResolveAsync("Foundry.Connect", true, cancellation.Token));

        AssertNoPreparedPayload();
        Assert.True(File.Exists(original));
        Assert.Empty(requests);
    }

    [Theory]
    [InlineData("Foundry.Connect", "FOUNDRY_CONNECT")]
    [InlineData("Foundry.PostInstall", "FOUNDRY_POSTINSTALL")]
    public async Task ExplicitOverrideRequiresItsOwnExpectedHashAndNeverFallsBack(string application, string prefix)
    {
        SeedOriginal(application);
        string archive = Path.Combine(root, "custom.zip");
        File.WriteAllBytes(archive, ArchiveBytes("custom", application));
        environment[prefix + "_ARCHIVE"] = archive;
        using var client = Client();
        using var logger = new LoggerConfiguration().CreateLogger();

        await Assert.ThrowsAsync<InvalidDataException>(() => Resolver(client, logger).ResolveAsync(
            application, true, TestContext.Current.CancellationToken));
        Assert.Empty(requests);

        environment[prefix + "_ARCHIVE_SHA256"] = Hash(archive);
        string executable = await Resolver(client, logger).ResolveAsync(application, true, TestContext.Current.CancellationToken);
        Assert.Equal("custom", File.ReadAllText(executable));
        Assert.Empty(requests);
    }

    [Fact]
    public async Task LauncherPinsBundleExtractionUnderThePreparedRuntime()
    {
        SeedOriginal();
        using var client = Client();
        using var logger = new LoggerConfiguration().CreateLogger();
        string executable = await Resolver(client, logger).ResolveAsync("Foundry.Connect", true, TestContext.Current.CancellationToken);

        var start = ApplicationLauncher.CreateStartInfo(executable,
            new Dictionary<string, string?> { ["DOTNET_BUNDLE_EXTRACT_BASE_DIR"] = Path.Combine(root, "Usb", "malicious") }, []);

        Assert.StartsWith(Path.GetDirectoryName(executable)! + Path.DirectorySeparatorChar,
            start.Environment["DOTNET_BUNDLE_EXTRACT_BASE_DIR"]!);
        Assert.Equal(Path.GetDirectoryName(executable), start.WorkingDirectory);
    }

    private string CurrentArchive => Path.Combine(CacheRoot, "Foundry.Connect", "win-x64", "current.zip");

    [Fact]
    public async Task BackgroundRefreshKeepsTheVerifiedArchiveWithoutRetainingUnusedRamPayloads()
    {
        SeedOriginal();
        byte[] update = ArchiveBytes("updated");
        using var client = Client(Release(Hash(update)), Payload(update));
        using var logger = new LoggerConfiguration().CreateLogger();

        await Resolver(client, logger).RefreshAsync("Foundry.Connect", TestContext.Current.CancellationToken);

        Assert.Equal(update, File.ReadAllBytes(CurrentArchive));
        AssertNoPreparedPayload();
    }

    [Theory]
    [InlineData("Foundry.Connect", "FOUNDRY_CONNECT")]
    [InlineData("Foundry.PostInstall", "FOUNDRY_POSTINSTALL")]
    public async Task SpecificReleaseTagTakesPrecedenceOverTheGlobalTag(string application, string prefix)
    {
        string original = SeedOriginal(application);
        byte[] update = ArchiveBytes("updated", application);
        File.WriteAllBytes(Path.Combine(Path.GetDirectoryName(original)!, "current.zip"), update);
        environment[prefix + "_RELEASE_TAG"] = " v-specific ";
        environment["FOUNDRY_RELEASE_TAG"] = "v-global";
        using var client = Client(Release(Hash(update), application: application));
        using var logger = new LoggerConfiguration().CreateLogger();

        string executable = await Resolver(client, logger).ResolveAsync(application, false, TestContext.Current.CancellationToken);

        Assert.Equal("updated", File.ReadAllText(executable));
        Assert.EndsWith("/tags/v-specific", Assert.Single(requests).AbsoluteUri);
    }

    [Fact]
    public async Task FailedCachePublicationDoesNotPreventUsingTheAuthenticatedDownload()
    {
        SeedOriginal();
        Directory.CreateDirectory(CurrentArchive);
        byte[] update = ArchiveBytes("updated");
        using var client = Client(Release(Hash(update)), Payload(update));
        using var logger = new LoggerConfiguration().CreateLogger();
        var warnings = new List<string>();

        string executable = await Resolver(client, logger, warnings: warnings).ResolveAsync("Foundry.Connect", false, TestContext.Current.CancellationToken);

        Assert.Equal("updated", File.ReadAllText(executable));
        Assert.Contains(warnings, value => value.Contains("saved to the cache", StringComparison.Ordinal));
        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(CurrentArchive)!, "*.download"));
    }

    [Theory]
    [InlineData("wrong.exe")]
    [InlineData("../escape.exe")]
    public async Task AuthenticatedButInvalidOverrideDoesNotFallBackOrLeaveExecutableContent(string entry)
    {
        SeedOriginal();
        string archive = Path.Combine(root, "override.zip");
        using (var zip = ZipFile.Open(archive, ZipArchiveMode.Create))
        {
            using var writer = new StreamWriter(zip.CreateEntry(entry).Open());
            writer.Write("candidate");
        }
        environment["FOUNDRY_CONNECT_ARCHIVE"] = archive;
        environment["FOUNDRY_CONNECT_ARCHIVE_SHA256"] = Hash(archive);
        using var client = Client();
        using var logger = new LoggerConfiguration().CreateLogger();

        await Assert.ThrowsAsync<InvalidDataException>(() => Resolver(client, logger).ResolveAsync(
            "Foundry.Connect", false, TestContext.Current.CancellationToken));

        AssertNoPreparedPayload();
        Assert.Empty(requests);
    }

    private string SeedOriginal(string application = "Foundry.Connect", string rid = "win-x64", string? runtimeRoot = null)
    {
        string path = Path.Combine(runtimeRoot ?? CacheRoot, application, rid, "original.zip");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, ArchiveBytes("original", application));
        Directory.CreateDirectory(Path.Combine(BootRoot, "Config"));
        File.WriteAllText(Path.Combine(BootRoot, "Config", "foundry.runtime-trust.json"), JsonSerializer.Serialize(new
        {
            schemaVersion = 1,
            archives = new[] { new { application, runtimeIdentifier = rid, sha256 = Hash(path) } }
        }));
        return path;
    }

    private static byte[] ArchiveBytes(string content, string application = "Foundry.Connect")
    {
        using var buffer = new MemoryStream();
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            using (var writer = new StreamWriter(zip.CreateEntry(application + ".exe").Open())) writer.Write(content);
            using (var writer = new StreamWriter(zip.CreateEntry("Foundry.Core.dll").Open())) writer.Write("trusted dependency");
            using (var writer = new StreamWriter(zip.CreateEntry("foundry.startup.json").Open())) writer.Write("{\"protocolVersions\":[1]}");
        }
        return buffer.ToArray();
    }

    private static string ReadArchiveEntry(string path, string entry)
    {
        using var zip = ZipFile.OpenRead(path);
        using var reader = new StreamReader(zip.GetEntry(entry)!.Open());
        return reader.ReadToEnd();
    }

    private RuntimeResolver Resolver(HttpClient client, Serilog.ILogger logger, string rid = "win-x64",
        List<string>? warnings = null, Action<RuntimeDownloadProgress>? progress = null, string? runtimeRoot = null) => new(BootRoot, runtimeRoot ?? CacheRoot, rid, client,
        logger, progress, key => environment.GetValueOrDefault(key), warning: value => warnings?.Add(value));

    private HttpClient Client(params HttpResponseMessage[] responses) => new(new Handler(requests, new(responses)));
    private static HttpResponseMessage Payload(byte[] bytes) => new(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };
    private static string Hash(string file) => Hash(File.ReadAllBytes(file));
    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));

    private static HttpResponseMessage Release(string? digest, bool addPrefix = true, string application = "Foundry.Connect", string rid = "win-x64") => new(HttpStatusCode.OK)
    {
        Content = new StringContent(JsonSerializer.Serialize(new
        {
            tag_name = "v1.2.3",
            assets = new[] { new { name = $"{application}-{rid}.zip", digest = addPrefix ? "sha256:" + digest : digest, browser_download_url = "https://example.test/payload.zip" } }
        }))
    };

    private void AssertNoPreparedPayload()
    {
        string execution = Path.Combine(BootRoot, "Execution");
        Assert.True(!Directory.Exists(execution) || !Directory.EnumerateFileSystemEntries(execution).Any());
    }

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

    private sealed class StalledHandler : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
            throw new InvalidOperationException("The metadata deadline should cancel this request.");
        }
    }

    private sealed class CollectingSink : ILogEventSink
    {
        internal List<LogEvent> Events { get; } = [];
        public void Emit(LogEvent logEvent) => Events.Add(logEvent);
    }

    private sealed class ConnectBoundary : ISystemPreparation, IApplicationLauncher, IBootstrapLogPersistence
    {
        internal List<string> LaunchedContent { get; } = [];
        public Task PrepareNetworkAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task PrepareClockAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task PrepareSystemAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task PersistAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<ApplicationLaunchResult> RunConnectAsync(string executable, string configurationPath,
            IReadOnlyDictionary<string, string?> environment, CancellationToken cancellationToken)
        {
            LaunchedContent.Add(File.ReadAllText(executable));
            return Task.FromResult(new ApplicationLaunchResult(false, 20));
        }
        public Task<ApplicationLaunchResult> StartDeployAsync(string executable,
            IReadOnlyDictionary<string, string?> environment, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Connect cancellation must prevent deployment.");
    }
}
