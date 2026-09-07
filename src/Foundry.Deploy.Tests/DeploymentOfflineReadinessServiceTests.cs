// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Security.Cryptography;
using Foundry.Core.Services.Catalog;
using Foundry.Deploy.Models;
using Foundry.Deploy.Services.Catalog;
using Foundry.Deploy.Services.Download;
using Foundry.Deploy.Services.Startup;

namespace Foundry.Deploy.Tests;

public sealed class DeploymentOfflineReadinessServiceTests
{
    [Fact]
    public async Task EvaluateAsync_RejectsReparseCandidateBeforeHashing()
    {
        string root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "FoundryTests", Guid.NewGuid().ToString("N"));
        string storage = System.IO.Path.Combine(root, "other-storage");
        string cache = System.IO.Path.Combine(root, "cache");
        string link = System.IO.Path.Combine(cache, "child");
        System.IO.Directory.CreateDirectory(storage);
        System.IO.Directory.CreateDirectory(cache);
        try
        {
            await System.IO.File.WriteAllBytesAsync(System.IO.Path.Combine(storage, "install.wim"), [1], TestContext.Current.CancellationToken);
            try { System.IO.Directory.CreateSymbolicLink(link, storage); }
            catch (UnauthorizedAccessException) { Assert.Skip("This host does not permit managed symbolic-link creation."); }
            var request = Request();
            request = request with { RequiredArtifacts = [new(request.RequiredArtifacts[0].Identity, [System.IO.Path.Combine(link, "install.wim")])] };
            var downloads = new FakeDownloads();
            var result = await new DeploymentOfflineReadinessService(downloads).EvaluateAsync(request, TestContext.Current.CancellationToken);
            Assert.False(result.CanContinue);
            Assert.Equal(0, downloads.CacheCalls);
        }
        finally
        {
            if (System.IO.Directory.Exists(link)) System.IO.Directory.Delete(link);
            System.IO.Directory.Delete(root, true);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task EvaluateAsync_RehashesRealCachedBytes(bool tamper)
    {
        string root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "FoundryTests", Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(root);
        try
        {
            byte[] good = [1, 2, 3];
            string path = System.IO.Path.Combine(root, "install.wim");
            await System.IO.File.WriteAllBytesAsync(path, tamper ? [3, 2, 1] : good);
            var request = Request();
            var os = request.OperatingSystem! with { SizeBytes = 3, Sha256 = Convert.ToHexString(SHA256.HashData(good)) };
            request = request with
            {
                OperatingSystem = os,
                Catalogs = request.Catalogs with { OperatingSystems = [os], OperatingSystemSnapshot = request.Catalogs.OperatingSystemSnapshot! with { Items = [os] } },
                RequiredArtifacts = [new(ArtifactIntegrityPolicy.FromOperatingSystem(os), [path])]
            };
            var downloads = new ArtifactDownloadService(Microsoft.Extensions.Logging.Abstractions.NullLogger<ArtifactDownloadService>.Instance);
            Assert.Equal(!tamper, (await new DeploymentOfflineReadinessService(downloads).EvaluateAsync(request)).CanContinue);
        }
        finally { System.IO.Directory.Delete(root, true); }
    }

    [Fact]
    public async Task EvaluateAsync_RequiresSelectedOemArtifactEvenWithCompleteImage()
    {
        var request = Request();
        request = request with { SelectedDriverPacks = [new() { Id = "selected-driver" }] };
        Assert.False((await new DeploymentOfflineReadinessService(new FakeDownloads()).EvaluateAsync(request)).CanContinue);
    }

    [Theory]
    [InlineData("complete", true)]
    [InlineData("missing", false)]
    [InlineData("unselected", false)]
    [InlineData("foreign-selection", false)]
    [InlineData("media", false)]
    [InlineData("configuration", false)]
    [InlineData("architecture", false)]
    [InlineData("online", false)]
    [InlineData("unresolved", false)]
    [InlineData("untrusted-catalog", false)]
    public async Task EvaluateAsync_RequiresCompleteCurrentTrustedSelection(string scenario, bool ready)
    {
        var request = Request();
        request = scenario switch
        {
            "unselected" => request with { OperatingSystem = null },
            "foreign-selection" => request with { OperatingSystem = request.OperatingSystem! with { Edition = "Other" } },
            "media" => request with { ExpectedMediaId = Guid.NewGuid() },
            "configuration" => request with { ConfigurationBytes = [2] },
            "architecture" => request with { RuntimeIdentifier = "win-x86" },
            "online" => request with { OnlineOnlyCapabilities = ["Graph registration"] },
            "unresolved" => request with { SelectionsResolved = false },
            "untrusted-catalog" => request with { Catalogs = request.Catalogs with { OperatingSystemSnapshot = request.Catalogs.OperatingSystemSnapshot! with { Revision = "untrusted" } } },
            _ => request
        };
        var downloads = new FakeDownloads { Missing = scenario == "missing" };
        var result = await new DeploymentOfflineReadinessService(downloads).EvaluateAsync(request);
        Assert.Equal(ready, result.CanContinue);
        Assert.Equal(ready, result.BlockingReasons.Count == 0);
        Assert.Equal(0, downloads.NetworkCalls);
    }

    [Fact]
    public async Task EvaluateAsync_PreservesCallerCancellation()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new DeploymentOfflineReadinessService(new FakeDownloads()).EvaluateAsync(Request(), cancellation.Token));
    }

    internal static DeploymentOfflineReadinessRequest Request()
    {
        var document = CatalogSnapshotStoreTests.Document();
        var manifest = CatalogSnapshotStoreTests.Manifest(document);
        var os = new OperatingSystemCatalogItem { CatalogRevision = document.Revision, SourceId = "source", Architecture = "x64", Edition = "Pro", WindowsRelease = "11", ReleaseId = "25H2", Build = "26200.1", BuildMajor = 26200, BuildUbr = 1, LanguageCode = "en-US", LicenseChannel = "RET", FileName = "install.wim", SizeBytes = 1, Url = "https://example.test/install.wim", Sha256 = new string('A', 64) };
        return new()
        {
            TrustedManifest = manifest,
            ExpectedMediaId = manifest.MediaId,
            RuntimeIdentifier = "win-x64",
            ConfigurationBytes = [1],
            ExpectedConfigurationDigest = Convert.ToHexString(SHA256.HashData([1])),
            Catalogs = new([os], []) { OperatingSystemSnapshot = new([os], document.Revision, document.SourceUri, document.RetrievedUtc, true), CanContinue = true },
            OperatingSystem = os,
            SelectionsResolved = true,
            RequiredArtifacts = [new(ArtifactIntegrityPolicy.FromOperatingSystem(os), ["owned-fixture/install.wim"])]
        };
    }

    private sealed class FakeDownloads : IArtifactDownloadService
    {
        public bool Missing { get; init; }
        public int NetworkCalls { get; private set; }
        public int CacheCalls { get; private set; }
        public Task<ArtifactDownloadResult?> TryUseCachedAsync(ArtifactIdentity artifact, string path, CancellationToken cancellationToken = default)
        {
            CacheCalls++;
            return Task.FromResult<ArtifactDownloadResult?>(Missing ? null : new() { DestinationPath = path, Downloaded = false, Method = "cache-hit", SizeBytes = 1 });
        }
        public Task<ArtifactDownloadResult> DownloadAsync(ArtifactIdentity artifact, string path, CancellationToken cancellationToken = default, IProgress<DownloadProgress>? progress = null)
        {
            NetworkCalls++;
            throw new InvalidOperationException("Offline readiness cannot download.");
        }
    }
}
