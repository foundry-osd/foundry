// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Text.Json;
using Foundry.Core.Models.PreOobe;
using Foundry.Deploy.Models;
using Foundry.Deploy.Models.Configuration;
using Foundry.Deploy.Services.Configuration;
using Foundry.Deploy.Services.Deployment;
using Foundry.Deploy.Services.Deployment.PreOobe;
using Foundry.Deploy.Services.Hardware;

namespace Foundry.Deploy.Tests;

public sealed class PreOobeContentResolverTests
{
    [Fact]
    public void DefaultDescriptorPath_UsesExecutableDirectoryForSelfExtractingApplications()
    {
        var resolver = new PreOobeContentResolver(new(), new ReadOnlyStorage());
        Assert.Equal(Path.Combine(Path.GetDirectoryName(Environment.ProcessPath)!, PreOobeRuntimeResolver.DescriptorFileName),
            resolver.DescriptorPath);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CorruptFirstRuntimeCandidate_UsesNextVerifiedOfflineCandidate(bool truncated)
    {
        string root = Path.Combine(Path.GetTempPath(), "Foundry.Deploy.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            using var runtime = NativeRuntimeFixture.Create(root);
            string invalidMedia = Path.Combine(root, "invalid-media");
            string validMedia = Path.Combine(root, "valid-media");
            string relative = Path.Combine("Cache", "PreOobe", "Runtimes", "win-x64", runtime.RuntimeAsset.ArchiveSha256, "runtime.zip");
            string invalidArchive = Path.Combine(invalidMedia, relative);
            string validArchive = Path.Combine(validMedia, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(invalidArchive)!);
            Directory.CreateDirectory(Path.GetDirectoryName(validArchive)!);
            byte[] validBytes = await File.ReadAllBytesAsync(runtime.RuntimeArchivePath, TestContext.Current.CancellationToken);
            byte[] corruptBytes = truncated ? validBytes[..^1] : new byte[validBytes.Length];
            await File.WriteAllBytesAsync(invalidArchive, corruptBytes, TestContext.Current.CancellationToken);
            await File.WriteAllBytesAsync(validArchive, validBytes, TestContext.Current.CancellationToken);
            string descriptorPath = Path.Combine(root, "descriptor.json");
            var descriptor = new PreOobeRuntimeDescriptor
            {
                ReleaseTag = "local",
                Assets = [runtime.RuntimeAsset, runtime.RuntimeAsset with { RuntimeIdentifier = "win-arm64", AssetName = "Foundry.PostInstall-win-arm64.zip" }]
            };
            await File.WriteAllTextAsync(descriptorPath, JsonSerializer.Serialize(descriptor, ConfigurationJsonDefaults.SerializerOptions),
                TestContext.Current.CancellationToken);
            using var context = new DeploymentStepExecutionContext(
                new DeploymentContext
                {
                    Mode = DeploymentMode.Iso,
                    CacheRootPath = Path.Combine(root, "cache"),
                    TargetDiskNumber = 1,
                    TargetComputerName = "LAB01",
                    DriverPackSelectionKind = DriverPackSelectionKind.None,
                    OperatingSystem = new OperatingSystemCatalogItem { Architecture = "x64", LicenseChannel = "RET" }
                },
                new DeploymentRuntimeState { WorkspaceRoot = Path.Combine(root, "workspace") }, [],
                new DriverApplicationOperationProgressService(), new DriverApplicationLogService(), new ExternalDiskService(), _ => { });
            using var http = new HttpClient(new RejectingHandler());
            var resolver = new PreOobeContentResolver(new PreOobeRuntimeResolver(http), new ReadOnlyStorage())
            {
                DescriptorPath = descriptorPath,
                MediaRoots = () => [invalidMedia, validMedia]
            };

            using var prepared = await resolver.PrepareAsync(context, TestContext.Current.CancellationToken);

            Assert.NotNull(prepared);
            Assert.Equal(validArchive, prepared.RuntimeArchivePath);
            Assert.Null(prepared.TemporaryRuntimeDirectory);
            Assert.Throws<IOException>(() => File.Delete(validArchive));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private sealed class ExternalDiskService : ITargetDiskService
    {
        public Task<IReadOnlyList<TargetDiskInfo>> GetDisksAsync(CancellationToken cancellationToken = default, bool includeExcludedDisks = false) =>
            Task.FromResult<IReadOnlyList<TargetDiskInfo>>([]);
        public Task<int?> GetDiskNumberForPathAsync(string path, CancellationToken cancellationToken = default) => Task.FromResult<int?>(0);
    }

    private sealed class ReadOnlyStorage : IDeploymentStorageService
    {
        public long? GetAvailableBytes(string path) => throw new InvalidOperationException("A verified offline candidate needs no writable cache.");
        public bool CanWriteDirectory(string path, string? existingFilePath = null) => false;
    }

    private sealed class RejectingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("A verified offline candidate needs no download.");
    }
}
