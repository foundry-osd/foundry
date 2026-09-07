// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Security.Cryptography;
using Foundry.Deploy.Models;
using Foundry.Deploy.Services.Cache;
using Foundry.Deploy.Services.Deployment;
using Foundry.Deploy.Services.Deployment.Steps;
using Foundry.Deploy.Services.Download;
using Foundry.Deploy.Services.DriverPacks;
using Foundry.Deploy.Services.Networking;
using Microsoft.Extensions.Logging.Abstractions;

namespace Foundry.Deploy.Tests;

public sealed class DownloadDriverPackStepTests
{
    [Theory]
    [InlineData("keyed")]
    [InlineData("legacy")]
    [InlineData("missing")]
    [InlineData("corrupt")]
    public async Task ExecuteAsync_OfflineIsoAfterTargetLayout_UsesOnlyOriginalMediaCache(string cacheState)
    {
        string root = Path.Combine(Path.GetTempPath(), "foundry-offline-driver-" + Guid.NewGuid().ToString("N"));
        string mediaRoot = Path.Combine(root, "IndependentMedia");
        string targetRoot = Path.Combine(root, "Target", "Foundry");
        byte[] bytes = [1, 2, 3, 4];
        var driver = new DriverPackCatalogItem
        {
            Id = "synthetic",
            PackageId = "synthetic",
            Manufacturer = "Dell",
            Name = "Synthetic driver pack",
            CatalogRevision = "sha256:" + new string('b', 64),
            FileName = "drivers.zip",
            SizeBytes = bytes.Length,
            DownloadUrl = "https://example.com/drivers.zip",
            Sha256 = Convert.ToHexString(SHA256.HashData(bytes))
        };
        ArtifactIdentity identity = ArtifactIntegrityPolicy.FromDriverPack(driver);
        string originalDirectory = Path.Combine(mediaRoot, "Cache", "DriverPacks", "Dell");
        string keyed = Path.Combine(originalDirectory, identity.CacheKey, identity.FileName);
        string legacy = Path.Combine(originalDirectory, identity.FileName);
        string targetCached = Path.Combine(targetRoot, "Cache", "DriverPacks", "Dell", identity.CacheKey, identity.FileName);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(keyed)!);
            Directory.CreateDirectory(Path.GetDirectoryName(targetCached)!);
            // A valid target copy must not disguise loss of the independently verified source.
            await File.WriteAllBytesAsync(targetCached, bytes, TestContext.Current.CancellationToken);
            if (cacheState is "keyed" or "corrupt")
                await File.WriteAllBytesAsync(keyed, cacheState == "corrupt" ? [4, 3, 2, 1] : bytes, TestContext.Current.CancellationToken);
            if (cacheState == "legacy") await File.WriteAllBytesAsync(legacy, bytes, TestContext.Current.CancellationToken);
            var request = new DeploymentContext
            {
                Mode = DeploymentMode.Iso,
                CacheRootPath = Path.Combine(mediaRoot, "Runtime"),
                TargetDiskNumber = 1,
                TargetComputerName = "SYNTHETIC",
                OperatingSystem = new(),
                DriverPackSelectionKind = DriverPackSelectionKind.OemCatalog,
                DriverPack = driver
            };
            var state = new DeploymentRuntimeState
            {
                Mode = DeploymentMode.Iso,
                WorkspaceRoot = Path.Combine(root, "Workspace"),
                TargetFoundryRoot = targetRoot,
                TargetWindowsPartitionRoot = Path.Combine(root, "Target")
            };
            using var context = new DeploymentStepExecutionContext(request, state, [],
                new DriverApplicationOperationProgressService(), new DriverApplicationLogService(),
                new DriverApplicationTargetDiskService(), _ => { });
            Assert.Equal(Path.Combine(targetRoot, "Cache", "DriverPacks"), context.ResolveDriverPackCacheRoot());
            var downloads = new RecordingDownloads();
            var storage = new NoStorageProbe();
            var step = new DownloadDriverPackStep(new NoMicrosoftUpdate(), downloads,
                new PayloadCachePlacementService(downloads, storage), new(true));

            if (cacheState is "missing" or "corrupt")
            {
                await Assert.ThrowsAsync<IOException>(() => step.ExecuteAsync(context, TestContext.Current.CancellationToken));
                Assert.Null(state.DownloadedDriverPackPath);
            }
            else
            {
                DeploymentStepResult result = await step.ExecuteAsync(context, TestContext.Current.CancellationToken);
                Assert.Equal(DeploymentStepState.Succeeded, result.State);
                Assert.Equal(cacheState == "keyed" ? keyed : legacy, state.DownloadedDriverPackPath);
            }
            Assert.Equal(cacheState == "keyed" ? new[] { keyed } : new[] { keyed, legacy }, downloads.ReadPaths);
            Assert.Equal(0, downloads.DownloadCalls);
            Assert.Equal(0, storage.Calls);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    private sealed class RecordingDownloads : IArtifactDownloadService
    {
        private readonly ArtifactDownloadService verifier = new(NullLogger<ArtifactDownloadService>.Instance, new(true));
        public List<string> ReadPaths { get; } = [];
        public int DownloadCalls { get; private set; }
        public Task<ArtifactDownloadResult?> TryUseCachedAsync(ArtifactIdentity artifact, string path, CancellationToken cancellationToken = default)
        { ReadPaths.Add(path); return verifier.TryUseCachedAsync(artifact, path, cancellationToken); }
        public Task<ArtifactDownloadResult> DownloadAsync(ArtifactIdentity artifact, string path,
            CancellationToken cancellationToken = default, IProgress<DownloadProgress>? progress = null)
        { DownloadCalls++; throw new InvalidOperationException("Offline cache misses must not invoke acquisition."); }
    }

    private sealed class NoStorageProbe : IVolumeStorageProbe
    {
        public int Calls { get; private set; }
        public VolumeStorageStatus Inspect(string directory)
        { Calls++; throw new InvalidOperationException("Offline reuse must not probe writable storage."); }
    }

    private sealed class NoMicrosoftUpdate : IMicrosoftUpdateCatalogDriverService
    {
        public Task<MicrosoftUpdateCatalogDriverResult> DownloadAsync(HardwareProfile hardwareProfile, OperatingSystemCatalogItem operatingSystem,
            string destinationDirectory, string cacheDirectory, CancellationToken cancellationToken = default, IProgress<double>? progress = null)
            => throw new InvalidOperationException("OEM reuse must not call Microsoft Update.");
        public Task<MicrosoftUpdateCatalogDriverResult> ExpandAsync(string sourceDirectory, string destinationDirectory,
            CancellationToken cancellationToken = default, IProgress<double>? progress = null)
            => throw new InvalidOperationException("The download step must not expand payloads.");
    }
}
