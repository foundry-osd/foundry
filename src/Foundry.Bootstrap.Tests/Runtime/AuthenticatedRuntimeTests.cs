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
    private const string MediaVersion = "26.11.2.1";
    private const string UnavailableMessage = "No authenticated original Foundry.Connect payload is available.";

    private readonly string root = Path.Combine(Path.GetTempPath(), "FoundryAuthenticatedRuntime-" + Guid.NewGuid().ToString("N"));
    private readonly List<Uri> requests = [];
    private readonly Dictionary<string, string> environment = [];
    private string BootRoot => Path.Combine(root, "BootImage");
    private string CacheRoot => Path.Combine(root, "Usb", "Runtime");

    [Fact]
    public async Task CoordinatorLaunchesTheOriginalConnectWithoutTheNetwork()
    {
        SeedOriginal();
        using var client = new HttpClient(new NoNetworkHandler(requests));
        using var logger = new LoggerConfiguration().CreateLogger();
        var boundary = new ConnectBoundary();
        var context = new BootstrapContext(BootRoot, CacheRoot, "win-x64", "TEST", null, false, false);

        var coordinator = new BootstrapCoordinator(context, Resolver(client, logger), boundary, boundary, boundary, logger, _ => { },
            createKeyRelay: () => new NoOpKeyRelay());
        Assert.Equal(BootstrapOutcome.Cancelled, (await coordinator.RunAsync(TestContext.Current.CancellationToken)).Outcome);

        Assert.Equal(["original"], boundary.LaunchedContent);
        Assert.Empty(requests);
    }

    [Theory]
    [InlineData("Foundry.Connect")]
    [InlineData("Foundry.Deploy")]
    [InlineData("Foundry.PostInstall")]
    public async Task Resolve_ValidMediaPayload_DoesNotCallTheNetwork(string application)
    {
        string original = SeedOriginal(application);
        // A cached update written by an older Bootstrap is ignored.
        File.WriteAllBytes(Path.Combine(Path.GetDirectoryName(original)!, "current.zip"), ArchiveBytes("updated", application));
        using var client = new HttpClient(new NoNetworkHandler(requests));
        using var logger = new LoggerConfiguration().CreateLogger();
        var warnings = new List<string>();

        string executable = await Resolver(client, logger, warnings: warnings).ResolveAsync(application, false, TestContext.Current.CancellationToken);

        Assert.Equal("original", File.ReadAllText(executable));
        Assert.Empty(requests);
        Assert.Empty(warnings);
    }

    [Theory]
    [InlineData("Foundry.Connect", "FOUNDRY_CONNECT")]
    [InlineData("Foundry.Deploy", "FOUNDRY_DEPLOY")]
    [InlineData("Foundry.PostInstall", "FOUNDRY_POSTINSTALL")]
    public async Task Resolve_MissingMediaPayload_RepairsFromTheMediaRelease(string application, string prefix)
    {
        byte[] original = RemoveOriginal(SeedOriginal(application));
        // Another application's override must not select the release of this one.
        environment[(prefix == "FOUNDRY_CONNECT" ? "FOUNDRY_DEPLOY" : "FOUNDRY_CONNECT") + "_RELEASE_TAG"] = "v-other";
        using var client = Client(Release(Hash(original), application: application), Payload(original));
        using var logger = new LoggerConfiguration().CreateLogger();
        var warnings = new List<string>();

        string executable = await Resolver(client, logger, warnings: warnings).ResolveAsync(application, false, TestContext.Current.CancellationToken);

        Assert.Equal("original", File.ReadAllText(executable));
        Assert.EndsWith("/releases/tags/v26.11.2.1", requests[0].AbsoluteUri, StringComparison.Ordinal);
        Assert.Equal("example.test", requests[1].Host);
        Assert.Equal([$"{application} was repaired from release v26.11.2.1."], warnings);
    }

    [Fact]
    public async Task Resolve_PostInstall_FollowsTheDeployReleaseOverride()
    {
        byte[] original = RemoveOriginal(SeedOriginal("Foundry.PostInstall"));
        environment["FOUNDRY_DEPLOY_RELEASE_TAG"] = "v26.9.14.1";
        using var client = Client(Release(Hash(original), application: "Foundry.PostInstall"), Payload(original));
        using var logger = new LoggerConfiguration().CreateLogger();

        await Resolver(client, logger).ResolveAsync("Foundry.PostInstall", false, TestContext.Current.CancellationToken);

        Assert.EndsWith("/releases/tags/v26.9.14.1", requests[0].AbsoluteUri, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Resolve_TamperedMediaPayload_RepairsFromTheMediaRelease()
    {
        string archive = SeedOriginal();
        byte[] original = File.ReadAllBytes(archive);
        byte[] tampered = ArchiveBytes("substituted");
        File.WriteAllBytes(archive, tampered);
        using var client = Client(Release(Hash(original)), Payload(original));
        using var logger = new LoggerConfiguration().CreateLogger();
        var warnings = new List<string>();

        string executable = await Resolver(client, logger, warnings: warnings).ResolveAsync("Foundry.Connect", false, TestContext.Current.CancellationToken);

        Assert.Equal("original", File.ReadAllText(executable));
        Assert.Equal(["Foundry.Connect was repaired from release v26.11.2.1."], warnings);
        // A repair lives only in boot-owned storage; the media is never rewritten.
        Assert.Equal(tampered, File.ReadAllBytes(archive));
    }

    [Fact]
    public async Task Resolve_ReleaseAssetHashDiffersFromMedia_Fails()
    {
        RemoveOriginal(SeedOriginal());
        byte[] other = ArchiveBytes("other release");
        using var client = Client(Release(Hash(other)), Payload(other));
        using var logger = new LoggerConfiguration().CreateLogger();

        var exception = await Assert.ThrowsAsync<InvalidDataException>(() => Resolver(client, logger).ResolveAsync(
            "Foundry.Connect", false, TestContext.Current.CancellationToken));

        Assert.StartsWith(UnavailableMessage, exception.Message, StringComparison.Ordinal);
        Assert.Equal("The release asset does not match the boot media.", exception.InnerException?.Message);
        Assert.Single(requests);
        AssertNoPreparedPayload();
    }

    [Theory]
    [InlineData("Foundry.Deploy")]
    [InlineData("Foundry.PostInstall")]
    public async Task Resolve_NoTrustHash_DownloadsFromTheMediaReleaseWithoutRepairWarning(string application)
    {
        // Release media embed only Connect (and PostInstall with Domain Join); other runtimes are downloaded at every start.
        SeedOriginal();
        byte[] release = ArchiveBytes("released", application);
        using var client = Client(Release(Hash(release), application: application), Payload(release));
        using var logger = new LoggerConfiguration().CreateLogger();
        var warnings = new List<string>();

        string executable = await Resolver(client, logger, warnings: warnings).ResolveAsync(application, false, TestContext.Current.CancellationToken);

        Assert.Equal("released", File.ReadAllText(executable));
        Assert.EndsWith("/releases/tags/v26.11.2.1", requests[0].AbsoluteUri, StringComparison.Ordinal);
        Assert.Equal("example.test", requests[1].Host);
        Assert.Empty(warnings);
        Assert.Equal(release, File.ReadAllBytes(CurrentArchive(application)));
    }

    [Fact]
    public async Task Resolve_CachedArchiveOfTheMediaRelease_IsReusedWithoutDownload()
    {
        SeedOriginal();
        byte[] release = ArchiveBytes("released", "Foundry.Deploy");
        Directory.CreateDirectory(Path.GetDirectoryName(CurrentArchive("Foundry.Deploy"))!);
        File.WriteAllBytes(CurrentArchive("Foundry.Deploy"), release);
        using var client = Client(Release(Hash(release), application: "Foundry.Deploy"));
        using var logger = new LoggerConfiguration().CreateLogger();

        string executable = await Resolver(client, logger).ResolveAsync("Foundry.Deploy", false, TestContext.Current.CancellationToken);

        Assert.Equal("released", File.ReadAllText(executable));
        Assert.EndsWith("/releases/tags/v26.11.2.1", Assert.Single(requests).AbsoluteUri, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Resolve_CachedArchiveOfAnotherRelease_IsReplaced()
    {
        SeedOriginal();
        Directory.CreateDirectory(Path.GetDirectoryName(CurrentArchive("Foundry.Deploy"))!);
        File.WriteAllBytes(CurrentArchive("Foundry.Deploy"), ArchiveBytes("newer latest", "Foundry.Deploy"));
        byte[] release = ArchiveBytes("released", "Foundry.Deploy");
        using var client = Client(Release(Hash(release), application: "Foundry.Deploy"), Payload(release));
        using var logger = new LoggerConfiguration().CreateLogger();

        string executable = await Resolver(client, logger).ResolveAsync("Foundry.Deploy", false, TestContext.Current.CancellationToken);

        Assert.Equal("released", File.ReadAllText(executable));
        Assert.Equal(release, File.ReadAllBytes(CurrentArchive("Foundry.Deploy")));
        Assert.Equal(2, requests.Count);
    }

    [Fact]
    public async Task RuntimeCacheBesideWinPeRootRemainsPersistentAndReusable()
    {
        string runtimeRoot = BootRoot + "-Usb";
        SeedOriginal(runtimeRoot: runtimeRoot);
        byte[] release = ArchiveBytes("released", "Foundry.Deploy");
        using var client = Client(Release(Hash(release), application: "Foundry.Deploy"), Payload(release),
            Release(Hash(release), application: "Foundry.Deploy"));
        using var logger = new LoggerConfiguration().CreateLogger();
        var resolver = Resolver(client, logger, runtimeRoot: runtimeRoot);

        for (int attempt = 0; attempt < 2; attempt++)
        {
            string executable = await resolver.ResolveAsync("Foundry.Deploy", false, TestContext.Current.CancellationToken);
            Assert.Equal("released", File.ReadAllText(executable));
        }

        Assert.Equal(release, File.ReadAllBytes(Path.Combine(runtimeRoot, "Foundry.Deploy", "win-x64", "current.zip")));
        Assert.Equal(1, requests.Count(request => request.Host == "example.test"));
    }

    [Theory]
    [InlineData("", false)]
    [InlineData("Runtime", false)]
    [InlineData("Runtime/../Runtime", false)]
    [InlineData("Runtime", true)]
    public async Task Resolve_BootOwnedRuntime_NeitherReadsNorWritesCurrentArchive(string relativeRuntimeRoot, bool seedCache)
    {
        string runtimeRoot = Path.Combine(BootRoot, relativeRuntimeRoot);
        SeedOriginal(runtimeRoot: runtimeRoot);
        string current = Path.Combine(runtimeRoot, "Foundry.Deploy", "win-x64", "current.zip");
        byte[] release = ArchiveBytes("released", "Foundry.Deploy");
        if (seedCache)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(current)!);
            File.WriteAllBytes(current, release);
        }
        using var client = Client(Release(Hash(release), application: "Foundry.Deploy"), Payload(release));
        using var logger = new LoggerConfiguration().CreateLogger();

        string executable = await Resolver(client, logger, runtimeRoot: runtimeRoot).ResolveAsync(
            "Foundry.Deploy", false, TestContext.Current.CancellationToken);

        Assert.Equal("released", File.ReadAllText(executable));
        Assert.Equal("example.test", requests[1].Host);
        Assert.Equal(seedCache, File.Exists(current));
        Assert.Empty(Directory.EnumerateFiles(Path.Combine(BootRoot, "Execution"), "*.zip", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task FailedCachePublicationDoesNotPreventUsingTheAuthenticatedDownload()
    {
        SeedOriginal();
        Directory.CreateDirectory(CurrentArchive("Foundry.Deploy"));
        byte[] release = ArchiveBytes("released", "Foundry.Deploy");
        using var client = Client(Release(Hash(release), application: "Foundry.Deploy"), Payload(release));
        using var logger = new LoggerConfiguration().CreateLogger();
        var warnings = new List<string>();

        string executable = await Resolver(client, logger, warnings: warnings).ResolveAsync("Foundry.Deploy", false, TestContext.Current.CancellationToken);

        Assert.Equal("released", File.ReadAllText(executable));
        Assert.Contains(warnings, value => value.Contains("saved to the cache", StringComparison.Ordinal));
        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(CurrentArchive("Foundry.Deploy"))!, "*.download"));
    }

    [Fact]
    public async Task Resolve_ReleaseWithoutAsset_FailsWithRecreateMessage()
    {
        byte[] original = RemoveOriginal(SeedOriginal());
        using var client = Client(Release(Hash(original), rid: "win-arm64"));
        using var logger = new LoggerConfiguration().CreateLogger();

        var exception = await Assert.ThrowsAsync<InvalidDataException>(() => Resolver(client, logger).ResolveAsync(
            "Foundry.Connect", false, TestContext.Current.CancellationToken));

        Assert.StartsWith(UnavailableMessage, exception.Message, StringComparison.Ordinal);
        Assert.Contains("Recreate the boot media", exception.Message, StringComparison.Ordinal);
        Assert.Single(requests);
        AssertNoPreparedPayload();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("sha256:abc")]
    [InlineData("sha512:1234")]
    public async Task MissingOrUnsupportedReleaseDigestCannotAuthorizeCachedOrDownloadedCode(string? digest)
    {
        SeedOriginal();
        Directory.CreateDirectory(Path.GetDirectoryName(CurrentArchive("Foundry.Deploy"))!);
        File.WriteAllBytes(CurrentArchive("Foundry.Deploy"), ArchiveBytes("cached", "Foundry.Deploy"));
        using var client = Client(Release(digest, addPrefix: false, application: "Foundry.Deploy"), Payload(ArchiveBytes("substituted", "Foundry.Deploy")));
        using var logger = new LoggerConfiguration().CreateLogger();

        await Assert.ThrowsAsync<InvalidDataException>(() => Resolver(client, logger).ResolveAsync(
            "Foundry.Deploy", false, TestContext.Current.CancellationToken));

        Assert.Single(requests);
    }

    [Fact]
    public async Task Resolve_ConnectRepair_StopsAfterDeadline()
    {
        RemoveOriginal(SeedOriginal());
        using var client = new HttpClient(new StalledHandler());
        using var logger = new LoggerConfiguration().CreateLogger();

        var exception = await Assert.ThrowsAsync<InvalidDataException>(() => Resolver(client, logger).ResolveAsync(
            "Foundry.Connect", false, TestContext.Current.CancellationToken));

        Assert.StartsWith(UnavailableMessage, exception.Message, StringComparison.Ordinal);
        Assert.IsAssignableFrom<OperationCanceledException>(exception.InnerException);
        Assert.False(TestContext.Current.CancellationToken.IsCancellationRequested);
    }

    [Theory]
    [InlineData("26.11.2.1", "Foundry.Connect")]
    [InlineData("26.11.2.1+0123abcd", "Foundry.Connect")]
    [InlineData("26.11.2.1", "Foundry.Deploy")]
    public async Task Resolve_PublishedVersion_NeverRequestsLatest(string version, string application)
    {
        // Connect is embedded and missing (repair); Deploy has no trust hash (normal download).
        RemoveOriginal(SeedOriginal());
        using var client = Client();
        using var logger = new LoggerConfiguration().CreateLogger();

        await Assert.ThrowsAsync<InvalidDataException>(() => Resolver(client, logger, releaseVersion: version).ResolveAsync(
            application, false, TestContext.Current.CancellationToken));

        Assert.NotEmpty(requests);
        Assert.All(requests, request => Assert.EndsWith("/releases/tags/v26.11.2.1", request.AbsoluteUri, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("Foundry.Connect", "FOUNDRY_CONNECT")]
    [InlineData("Foundry.PostInstall", "FOUNDRY_POSTINSTALL")]
    public async Task SpecificReleaseTagTakesPrecedenceOverTheGlobalTag(string application, string prefix)
    {
        byte[] original = RemoveOriginal(SeedOriginal(application));
        environment[prefix + "_RELEASE_TAG"] = " v-specific ";
        environment["FOUNDRY_RELEASE_TAG"] = "v-global";
        using var client = Client(Release(Hash(original), application: application), Payload(original));
        using var logger = new LoggerConfiguration().CreateLogger();
        var warnings = new List<string>();

        string executable = await Resolver(client, logger, warnings: warnings).ResolveAsync(application, false, TestContext.Current.CancellationToken);

        Assert.Equal("original", File.ReadAllText(executable));
        Assert.EndsWith("/tags/v-specific", requests[0].AbsoluteUri, StringComparison.Ordinal);
        Assert.Equal([$"{application} was repaired from release v-specific."], warnings);
    }

    [Theory]
    [InlineData("Usb/Runtime")]
    [InlineData("BootImage")]
    [InlineData("BootImage/Runtime")]
    [InlineData("BootImage-Usb")]
    public async Task Resolve_EmbeddedRepair_NeverWritesCurrentArchive(string relativeRuntimeRoot)
    {
        string runtimeRoot = Path.Combine(root, relativeRuntimeRoot);
        string archive = SeedOriginal(runtimeRoot: runtimeRoot);
        byte[] original = RemoveOriginal(archive);
        using var client = Client(Release(Hash(original)), Payload(original));
        using var logger = new LoggerConfiguration().CreateLogger();

        string executable = await Resolver(client, logger, runtimeRoot: runtimeRoot).ResolveAsync(
            "Foundry.Connect", false, TestContext.Current.CancellationToken);

        Assert.Equal("original", File.ReadAllText(executable));
        Assert.Empty(Directory.EnumerateFiles(root, "current.zip", SearchOption.AllDirectories));
        Assert.False(File.Exists(archive));
        Assert.Empty(Directory.EnumerateFiles(Path.Combine(BootRoot, "Execution"), "*.zip", SearchOption.AllDirectories));
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("tampered")]
    [InlineData("untrusted")]
    public async Task Resolve_DebugApplication_DoesNotRepair(string state)
    {
        string archive = SeedOriginal();
        byte[] original = File.ReadAllBytes(archive);
        if (state == "tampered") File.WriteAllBytes(archive, ArchiveBytes("substituted"));
        else if (state == "missing") File.Delete(archive);
        else File.Delete(Path.Combine(BootRoot, "Config", "foundry.runtime-trust.json"));
        using var client = Client(Release(Hash(original)), Payload(original));
        using var logger = new LoggerConfiguration().CreateLogger();

        await Assert.ThrowsAsync<InvalidDataException>(() => Resolver(client, logger).ResolveAsync(
            "Foundry.Connect", true, TestContext.Current.CancellationToken));

        Assert.Empty(requests);
        AssertNoPreparedPayload();
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
    public async Task OriginalArchiveIsVerifiedAndExecutedFromTheCopiedBytes()
    {
        string archive = SeedOriginal();
        using var client = Client();
        using var logger = new LoggerConfiguration().CreateLogger();
        bool mutated = false;
        var resolver = Resolver(client, logger, progress: value =>
        {
            if (!mutated && value.Phase == RuntimeProgressPhase.Verification && value.BytesReceived > 0)
            {
                mutated = true;
                File.WriteAllBytes(archive, ArchiveBytes("substituted"));
            }
        });

        string executable = await resolver.ResolveAsync("Foundry.Connect", false, TestContext.Current.CancellationToken);

        Assert.True(mutated);
        Assert.Equal("original", File.ReadAllText(executable));
        Assert.Empty(requests);
    }

    [Fact]
    public async Task ReleaseRemovesOnlyItsPreparedWorkspaceAndBundleWhilePreservingArchives()
    {
        string original = SeedOriginal();
        byte[] originalBytes = File.ReadAllBytes(original);
        using var client = Client();
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

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => resolver.ResolveAsync("Foundry.Connect", false, cancellation.Token));

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

    [Fact]
    public void TryGetDebugArchive_ReturnsTheBaselineArchiveAndItsTrustedHash()
    {
        string archive = SeedOriginal("Foundry.AutopilotInteractive");
        using var client = Client();
        using var logger = new LoggerConfiguration().CreateLogger();

        var debugArchive = Resolver(client, logger).TryGetDebugArchive("Foundry.AutopilotInteractive");

        Assert.Equal((archive, Hash(archive).ToLowerInvariant()), debugArchive);
        Assert.Empty(requests);
    }

    [Fact]
    public void TryGetDebugArchive_PrefersTheImageCopyOnIsoMedia()
    {
        string imageRuntime = Path.Combine(BootRoot, "Runtime");
        string archive = SeedOriginal("Foundry.AutopilotInteractive", runtimeRoot: imageRuntime);
        using var client = Client();
        using var logger = new LoggerConfiguration().CreateLogger();

        Assert.Equal(archive, Resolver(client, logger, runtimeRoot: imageRuntime).TryGetDebugArchive("Foundry.AutopilotInteractive")!.Value.ArchivePath);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TryGetDebugArchive_ReturnsNull_WhenTheTrustManifestOrTheArchiveIsMissing(bool deleteArchive)
    {
        string archive = SeedOriginal("Foundry.AutopilotInteractive");
        string application = deleteArchive ? "Foundry.AutopilotInteractive" : "Foundry.Connect";
        if (deleteArchive) File.Delete(archive);
        using var client = Client();
        using var logger = new LoggerConfiguration().CreateLogger();

        Assert.Null(Resolver(client, logger).TryGetDebugArchive(deleteArchive ? application : "Foundry.Deploy"));
        Assert.Empty(requests);
    }

    [Fact]
    public void TryGetDebugArchive_ReturnsNull_WhenTheTrustManifestIsMalformed()
    {
        SeedOriginal("Foundry.AutopilotInteractive");
        File.WriteAllText(Path.Combine(BootRoot, "Config", "foundry.runtime-trust.json"), "not json");
        using var client = Client();
        using var logger = new LoggerConfiguration().CreateLogger();

        Assert.Null(Resolver(client, logger).TryGetDebugArchive("Foundry.AutopilotInteractive"));
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

    private string CurrentArchive(string application) => Path.Combine(CacheRoot, application, "win-x64", "current.zip");

    // Deletes the media archive while its trust manifest entry remains, and returns the bytes the release must serve.
    private static byte[] RemoveOriginal(string path)
    {
        byte[] bytes = File.ReadAllBytes(path);
        File.Delete(path);
        return bytes;
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

    private RuntimeResolver Resolver(HttpClient client, Serilog.ILogger logger, string rid = "win-x64",
        List<string>? warnings = null, Action<RuntimeDownloadProgress>? progress = null, string? runtimeRoot = null,
        string releaseVersion = MediaVersion) => new(BootRoot, runtimeRoot ?? CacheRoot, rid, client,
        logger, progress, key => environment.GetValueOrDefault(key), warning: value => warnings?.Add(value), releaseVersion: releaseVersion);

    private HttpClient Client(params HttpResponseMessage[] responses) => new(new Handler(requests, new(responses)));
    private static HttpResponseMessage Payload(byte[] bytes) => new(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };
    private static string Hash(string file) => Hash(File.ReadAllBytes(file));
    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));

    private static HttpResponseMessage Release(string? digest, bool addPrefix = true, string application = "Foundry.Connect", string rid = "win-x64") => new(HttpStatusCode.OK)
    {
        Content = new StringContent(JsonSerializer.Serialize(new
        {
            tag_name = "v" + MediaVersion,
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

    // Records the request before failing, so a resolver that swallows the failure still fails the test.
    private sealed class NoNetworkHandler(List<Uri> requests) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            requests.Add(request.RequestUri!);
            throw new InvalidOperationException("A valid boot-media payload must not use the network.");
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

    // Keeps this runtime test away from real named pipes; the relay itself is covered by DeploymentKeyRelayTests.
    private sealed class NoOpKeyRelay : IDeploymentKeyRelay
    {
        public string BeginReceive() => "Foundry.DeploymentKey.00000000000000000000000000000000";
        public void ExpectSender(int processId) { }
        public Task<bool> EndReceiveAsync() => Task.FromResult(false);
        public string? BeginServe() => null;
        public void ExpectReceiver(int processId) { }
        public Task<bool> EndServeAsync() => Task.FromResult(false);
        public void Dispose() { }
    }

    private sealed class ConnectBoundary : ISystemPreparation, IApplicationLauncher, IBootstrapLogPersistence
    {
        internal List<string> LaunchedContent { get; } = [];
        public Task PrepareNetworkAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task PrepareClockAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task PrepareSystemAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task PersistAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<ApplicationLaunchResult> RunConnectAsync(string executable, string configurationPath,
            IReadOnlyDictionary<string, string?> environment, CancellationToken cancellationToken,
            Action<int>? processStarted = null, Func<Task>? processReleasing = null)
        {
            LaunchedContent.Add(File.ReadAllText(executable));
            return Task.FromResult(new ApplicationLaunchResult(false, 20));
        }
        public Task<ApplicationLaunchResult> StartDeployAsync(string executable,
            IReadOnlyDictionary<string, string?> environment, CancellationToken cancellationToken,
            Action<int>? processStarted = null, Func<Task>? processReleasing = null) =>
            throw new InvalidOperationException("Connect cancellation must prevent deployment.");
    }
}
