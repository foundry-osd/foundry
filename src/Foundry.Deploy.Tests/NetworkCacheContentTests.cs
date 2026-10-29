// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Deploy.Models;
using Foundry.Deploy.Services.Cache;
using Foundry.Deploy.Services.Deployment;
using Foundry.Deploy.Services.Deployment.PreOobe;
using Foundry.Deploy.Services.Deployment.Steps;
using Foundry.Deploy.Services.Hardware;
using Foundry.Deploy.Services.Logging;
using Foundry.Utilities.Networking;

namespace Foundry.Deploy.Tests;

/// <summary>Covers how content imported to the network cache share is reused and kept separate from the target disk.</summary>
public sealed class NetworkCacheContentTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "foundry-netcache-content-" + Guid.NewGuid().ToString("N"));
    private CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ResolveCacheStrategy_ReusesTheSessionResolvedAtStartup()
    {
        string share = Directory.CreateDirectory(Path.Combine(root, "share")).FullName;
        using NetworkCacheSession session = NetworkCacheSession.Connected(share, canWrite: false, new NoopConnector());
        var accessor = new NetworkCacheSessionAccessor { Current = session };
        var logs = new RecordingLogService();
        using DeploymentStepExecutionContext context = CreateContext(new UnmappedDisks(), logs);
        var step = new ResolveCacheStrategyStep(new FixedCacheLocator(Path.Combine(root, "cache")), new UnmappedDisks(), accessor);

        DeploymentStepResult result = await step.ExecuteAsync(context, Cancellation);

        Assert.Equal(DeploymentStepState.Succeeded, result.State);
        Assert.Same(session, accessor.Current);
        Assert.Equal(share, context.RuntimeState.NetworkCacheSharePath);
        Assert.Contains(logs.Messages, message => message.StartsWith("Network cache:", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ResolveCacheStrategy_WithoutAStartupSession_DoesNotUseTheNetworkCache()
    {
        // Only the startup check applies the published index, so the step never resolves a share of its own.
        var accessor = new NetworkCacheSessionAccessor();
        var logs = new RecordingLogService();
        using DeploymentStepExecutionContext context = CreateContext(new UnmappedDisks(), logs);
        var step = new ResolveCacheStrategyStep(new FixedCacheLocator(Path.Combine(root, "cache")), new UnmappedDisks(), accessor);

        DeploymentStepResult result = await step.ExecuteAsync(context, Cancellation);

        Assert.Equal(DeploymentStepState.Succeeded, result.State);
        Assert.Null(accessor.Current);
        Assert.Null(context.RuntimeState.NetworkCacheSharePath);
        Assert.Contains("Network cache not used (not resolved at startup)", logs.Messages);
    }

    [Fact]
    public async Task ResolveCacheStrategy_DryRun_RecordsTheStartupShareSoTheSimulatedPlanFollowsTheLiveOrder()
    {
        string share = Directory.CreateDirectory(Path.Combine(root, "share")).FullName;
        using NetworkCacheSession session = NetworkCacheSession.Connected(share, canWrite: false, new NoopConnector());
        var accessor = new NetworkCacheSessionAccessor { Current = session };
        using DeploymentStepExecutionContext context = CreateContext(new UnmappedDisks(), new RecordingLogService(), isDryRun: true);
        var step = new ResolveCacheStrategyStep(new FixedCacheLocator(Path.Combine(root, "cache")), new UnmappedDisks(), accessor);

        DeploymentStepResult result = await step.ExecuteAsync(context, Cancellation);

        Assert.Equal(DeploymentStepState.Succeeded, result.State);
        Assert.Equal(share, context.RuntimeState.NetworkCacheSharePath);
    }

    [Fact]
    public async Task IsExternalStorage_AcceptsAPathUnderTheSessionShareWithoutDiskMapping()
    {
        string share = Directory.CreateDirectory(Path.Combine(root, "share")).FullName;
        using DeploymentStepExecutionContext context = CreateContext(new ThrowingDisks(), new RecordingLogService());
        context.RuntimeState.NetworkCacheSharePath = share;

        bool external = await context.IsExternalStorageAsync(Path.Combine(share, "Foundry", "Cache", "PreOobe", "file.ps1"), Cancellation);

        Assert.True(external);
    }

    [Theory]
    [InlineData(@"\\server\share\Foundry\Cache\PreOobe\Packages\file.ps1", true)]
    [InlineData(@"\\SERVER\Share\Foundry", true)]
    [InlineData(@"\\server\share2\Foundry\file.ps1", false)]
    [InlineData(@"\\other\share\Foundry\file.ps1", false)]
    public void IsOnNetworkCacheShare_MatchesOnlyTheConnectedShare(string path, bool expected)
    {
        using DeploymentStepExecutionContext context = CreateContext(new ThrowingDisks(), new RecordingLogService());
        context.RuntimeState.NetworkCacheSharePath = @"\\server\share";

        Assert.Equal(expected, context.IsOnNetworkCacheShare(path));
    }

    [Fact]
    public async Task PostInstallSeparation_AcceptsAUncPathUnderTheSessionShare()
    {
        using DeploymentStepExecutionContext context = CreateContext(new ThrowingDisks(), new RecordingLogService());
        context.RuntimeState.NetworkCacheSharePath = @"\\server\share";

        await PreOobeContentResolver.RequireSeparateSourceAsync(context, @"\\server\share\Foundry\Cache\PreOobe\Packages\abc\files\script.ps1", Cancellation);
    }

    [Fact]
    public async Task PostInstallSeparation_AcceptsALocalFolderUsedAsTheSessionShare()
    {
        string share = Directory.CreateDirectory(Path.Combine(root, "share")).FullName;
        using DeploymentStepExecutionContext context = CreateContext(new UnmappedDisks(), new RecordingLogService());
        context.RuntimeState.NetworkCacheSharePath = share;

        await PreOobeContentResolver.RequireSeparateSourceAsync(context, Path.Combine(share, "Foundry", "Cache", "PreOobe", "manifests", "id.json"), Cancellation);
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            PreOobeContentResolver.RequireSeparateSourceAsync(context, Path.Combine(root, "elsewhere", "file.ps1"), Cancellation));
    }

    [Fact]
    public async Task CustomDriverSeparation_AcceptsAUncPathUnderTheSessionShare()
    {
        using DeploymentStepExecutionContext context = CreateContext(new ThrowingDisks(), new RecordingLogService());
        context.RuntimeState.NetworkCacheSharePath = @"\\server\share";

        bool separate = await ExtractDriverPackStep.IsCustomDriverSourceSeparateAsync(context,
            @"\\server\share\Foundry\Cache\DriverPacks\Custom\Packages\abc\files", Cancellation);

        Assert.True(separate);
    }

    private DeploymentStepExecutionContext CreateContext(ITargetDiskService disks, IDeploymentLogService logs, bool isDryRun = false) =>
        DeploymentStepExecutionContextTests.CreateExecutionContext(Path.Combine(root, "workspace"), Path.Combine(root, "cache"),
            DeploymentMode.Iso, logService: logs, targetDiskService: disks, isDryRun: isDryRun);

    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }

    private sealed class FixedCacheLocator(string path) : ICacheLocatorService
    {
        public Task<CacheResolution> ResolveAsync(DeploymentMode mode, string? preferredRootPath = null, CancellationToken cancellationToken = default) =>
            Task.FromResult(new CacheResolution { RootPath = path, Source = "test" });
    }

    /// <summary>Maps no path to a disk, so only the share rule can prove separation.</summary>
    private sealed class UnmappedDisks : ITargetDiskService
    {
        public Task<IReadOnlyList<TargetDiskInfo>> GetDisksAsync(CancellationToken cancellationToken = default, bool includeExcludedDisks = false) =>
            Task.FromResult<IReadOnlyList<TargetDiskInfo>>([]);
        public Task<int?> GetDiskNumberForPathAsync(string path, CancellationToken cancellationToken = default) => Task.FromResult<int?>(null);
    }

    /// <summary>Fails any disk lookup, proving the share rule runs before disk mapping.</summary>
    private sealed class ThrowingDisks : ITargetDiskService
    {
        public Task<IReadOnlyList<TargetDiskInfo>> GetDisksAsync(CancellationToken cancellationToken = default, bool includeExcludedDisks = false) =>
            Task.FromResult<IReadOnlyList<TargetDiskInfo>>([]);
        public Task<int?> GetDiskNumberForPathAsync(string path, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Disk mapping must not be queried for a share path.");
    }

    /// <summary>Records messages without writing files, so background state persistence cannot race the folder cleanup.</summary>
    private sealed class RecordingLogService : IDeploymentLogService
    {
        private readonly DeploymentLogService inner = new();
        public List<string> Messages { get; } = [];
        public DeploymentLogSession Initialize(string rootPath) => inner.Initialize(rootPath);
        public Task SaveStateAsync<T>(DeploymentLogSession session, T state, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task AppendAsync(DeploymentLogSession session, DeploymentLogLevel level, string message, CancellationToken cancellationToken = default)
        {
            lock (Messages) Messages.Add(message);
            return Task.CompletedTask;
        }
    }

    private sealed class NoopConnector : ISmbConnector
    {
        public SmbConnectResult Connect(string uncPath, string? userName, ReadOnlySpan<char> password) => new(SmbConnectStatus.Connected, 0);
        public void Disconnect(string uncPath) { }
    }
}
