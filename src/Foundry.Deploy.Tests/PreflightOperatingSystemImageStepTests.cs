// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.IO;
using Foundry.Deploy.Models;
using Foundry.Deploy.Services.Cache;
using Foundry.Deploy.Services.Deployment;
using Foundry.Deploy.Services.Deployment.PreOobe;
using Foundry.Deploy.Services.Deployment.Steps;
using Foundry.Deploy.Services.Hardware;
using Foundry.Deploy.Services.Logging;
using Foundry.Deploy.Services.Operations;

namespace Foundry.Deploy.Tests;

public sealed class PreflightOperatingSystemImageStepTests
{
    [Fact]
    public async Task Live_ChangedTargetStopsBeforeToolsOrCacheLookup()
    {
        using var fixture = new Fixture(3);
        fixture.Disks.Available = false;
        var step = new PreflightOperatingSystemImageStep((_, _, _, _, _) => throw new InvalidOperationException("Unexpected preparation"),
            fixture.Disks, () => throw new InvalidOperationException("Unexpected tool lookup"));
        DeploymentStepResult result = await step.ExecuteAsync(fixture.Context, TestContext.Current.CancellationToken);
        Assert.Equal(DeploymentStepState.Failed, result.State);
        Assert.Equal(0, fixture.Disks.PathQueries);
        Assert.Null(fixture.Context.RuntimeState.ImagePreflight);
    }

    [Fact]
    public async Task Live_MissingNativeToolStopsBeforeCacheLookupOrPreparation()
    {
        using var fixture = new Fixture(3);
        var step = new PreflightOperatingSystemImageStep((_, _, _, _, _) => throw new InvalidOperationException("Unexpected preparation"),
            fixture.Disks, () => throw new FileNotFoundException("Fixture tool missing"));
        await Assert.ThrowsAsync<FileNotFoundException>(() => step.ExecuteAsync(fixture.Context, TestContext.Current.CancellationToken));
        Assert.Equal(0, fixture.Disks.PathQueries);
        Assert.Null(fixture.Context.RuntimeState.ImagePreflight);
    }

    [Fact]
    public async Task Live_FullVerificationIsRecordedWithoutExposingImagePathInMessage()
    {
        using var fixture = new Fixture(3);
        var expected = new DeploymentPreflightResult(ImagePreflightLevel.CompleteImageVerified, 100,
            "private-image-path", null, null);
        var step = new PreflightOperatingSystemImageStep((_, _, _, _, _) => Task.FromResult(expected), fixture.Disks, () => { });
        DeploymentStepResult result = await step.ExecuteAsync(fixture.Context, TestContext.Current.CancellationToken);
        Assert.Same(expected, fixture.Context.RuntimeState.ImagePreflight);
        Assert.Contains("complete", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("private-image-path", result.Message);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(2)]
    [InlineData(3)]
    public async Task Live_ProvesIndependentCacheBeforePreparing(int? cacheDisk)
    {
        using var fixture = new Fixture(cacheDisk);
        string? receivedCache = "not-called";
        var step = new PreflightOperatingSystemImageStep((request, disk, cache, work, token) =>
        {
            Assert.Equal(2, disk.DiskNumber);
            Assert.Equal(fixture.Context.ResolveWorkspaceTempPath("Deployment"), work);
            receivedCache = cache;
            return Task.FromResult(new DeploymentPreflightResult(ImagePreflightLevel.TargetBackedMetadataOnly, 100, null, null, "fixture"));
        }, fixture.Disks, () => { });
        DeploymentStepResult result = await step.ExecuteAsync(fixture.Context, TestContext.Current.CancellationToken);
        Assert.Equal(DeploymentStepState.Succeeded, result.State);
        Assert.Equal(cacheDisk == 3 ? fixture.Context.ResolveOperatingSystemCacheRoot() : null, receivedCache);
        Assert.NotNull(fixture.Context.RuntimeState.ImagePreflight);
        Assert.Contains("metadata", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("x86", true)]
    [InlineData("unknown", true)]
    [InlineData("x64", false)]
    public async Task Live_RejectsArchitectureOrMissingFirstBootBeforePreparation(string architecture, bool hasPlan)
    {
        using var fixture = new Fixture(3, architecture: architecture, hasPlan: hasPlan);
        bool called = false;
        var step = new PreflightOperatingSystemImageStep((_, _, _, _, _) => { called = true; throw new InvalidOperationException(); }, fixture.Disks, () => { });
        DeploymentStepResult result = await step.ExecuteAsync(fixture.Context, TestContext.Current.CancellationToken);
        Assert.Equal(DeploymentStepState.Failed, result.State);
        Assert.False(called);
        Assert.Null(fixture.Context.RuntimeState.ImagePreflight);
    }

    [Fact]
    public async Task DryRun_DoesNotProbeOrRunNativeToolsAndNeverClaimsVerification()
    {
        using var fixture = new Fixture(3, dryRun: true);
        var step = new PreflightOperatingSystemImageStep((_, _, _, _, _) => throw new InvalidOperationException("Unexpected preflight"),
            fixture.Disks, () => throw new InvalidOperationException("Unexpected native gate"));
        DeploymentStepResult result = await step.ExecuteAsync(fixture.Context, TestContext.Current.CancellationToken);
        Assert.Equal(DeploymentStepState.Succeeded, result.State);
        Assert.Contains("simulat", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Null(fixture.Context.RuntimeState.ImagePreflight);
        Assert.Equal(0, fixture.Disks.PathQueries);
    }

    private sealed class Fixture : IDisposable
    {
        private readonly string root = Path.Combine(Path.GetTempPath(), "FoundryPreflightTests", Guid.NewGuid().ToString("N"));
        public Disks Disks { get; }
        public DeploymentStepExecutionContext Context { get; }
        public Fixture(int? cacheDisk, string architecture = "amd64", bool hasPlan = true, bool dryRun = false)
        {
            var disk = new TargetDiskInfo { DiskNumber = 2, UniqueId = "fixture-disk", SerialNumber = "serial", BusType = "NVMe", SizeBytes = 256UL * 1024 * 1024 * 1024, IsSelectable = true };
            Disks = new Disks(disk, cacheDisk);
            var request = new DeploymentContext
            {
                Mode = DeploymentMode.Iso,
                CacheRootPath = Path.Combine(root, "Cache"),
                TargetDiskNumber = 2,
                ConfirmedTargetDisk = TargetDiskIdentity.FromDisk(disk),
                TargetComputerName = "TEST-PC",
                DriverPackSelectionKind = DriverPackSelectionKind.None,
                IsDryRun = dryRun,
                OperatingSystem = new OperatingSystemCatalogItem
                {
                    CatalogRevision = "revision",
                    SourceId = "source",
                    Architecture = architecture,
                    Edition = "Pro",
                    LanguageCode = "en-US",
                    Build = "26100.1",
                    BuildMajor = 26100,
                    BuildUbr = 1,
                    WindowsRelease = "11",
                    ReleaseId = "24H2",
                    LicenseChannel = "RET",
                    FileName = "install.esd",
                    Url = "https://example.test/install.esd",
                    SizeBytes = 1024,
                    Sha256 = new string('A', 64)
                }
            };
            var state = new DeploymentRuntimeState
            {
                WorkspaceRoot = root,
                Mode = request.Mode,
                IsDryRun = dryRun,
                HardwareProfile = new HardwareProfile { Architecture = "x64" },
                ResolvedCache = new CacheResolution { RootPath = request.CacheRootPath, Source = "fixture" },
                FirstBootExecutionPlan = hasPlan ? new FirstBootExecutionPlan(FirstBootEntryPoint.None, false, null) : null
            };
            Context = new DeploymentStepExecutionContext(request, state, [], new OperationProgressService(), new Logs(), Disks, _ => { });
        }
        public void Dispose() { Context.Dispose(); Directory.Delete(root, true); }
    }

    private sealed class Disks(TargetDiskInfo disk, int? cacheDisk) : ITargetDiskService
    {
        public bool Available { get; set; } = true;
        public int PathQueries { get; private set; }
        public Task<IReadOnlyList<TargetDiskInfo>> GetDisksAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<TargetDiskInfo>>(Available ? [disk] : []);
        public Task<int?> GetDiskNumberForPathAsync(string path, CancellationToken cancellationToken = default) { PathQueries++; return Task.FromResult(cacheDisk); }
    }
    private sealed class Logs : IDeploymentLogService
    {
        public DeploymentLogSession Initialize(string rootPath) => new() { RootPath = rootPath, LogsDirectoryPath = rootPath, StateDirectoryPath = rootPath, StateFilePath = Path.Combine(rootPath, "state.json") };
        public Task AppendAsync(DeploymentLogSession session, DeploymentLogLevel level, string message, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task SaveStateAsync<TState>(DeploymentLogSession session, TState state, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
