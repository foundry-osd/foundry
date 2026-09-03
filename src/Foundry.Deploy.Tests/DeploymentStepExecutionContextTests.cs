// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Deploy.Models;
using Foundry.Deploy.Services.Cache;
using Foundry.Deploy.Services.Deployment;
using Foundry.Deploy.Services.Hardware;
using Foundry.Deploy.Services.Logging;
using Foundry.Deploy.Services.Operations;

namespace Foundry.Deploy.Tests;

public sealed class DeploymentStepExecutionContextTests
{
    [Fact]
    public void ResolvePreferredHash_PrefersPrimaryHashWhenPresent()
    {
        string hash = DeploymentStepExecutionContext.ResolvePreferredHash("  ABC123  ", "DEF456");

        Assert.Equal("ABC123", hash);
    }

    [Fact]
    public void ResolvePreferredHash_FallsBackToSecondaryHash()
    {
        string hash = DeploymentStepExecutionContext.ResolvePreferredHash(null, "  DEF456  ");

        Assert.Equal("DEF456", hash);
    }

    [Fact]
    public void ResolveFileName_WhenPreferredFileNameExists_SanitizesPreferredName()
    {
        string fileName = DeploymentStepExecutionContext.ResolveFileName("  setup<>.wim  ", "https://example.test/ignored.iso");

        Assert.Equal("setup__.wim", fileName);
    }

    [Fact]
    public void ResolveFileName_WhenPreferredFileNameMissing_UsesSourceUrlFileName()
    {
        string fileName = DeploymentStepExecutionContext.ResolveFileName("", "https://example.test/files/install.esd");

        Assert.Equal("install.esd", fileName);
    }

    [Fact]
    public void SanitizePathSegment_WhenValueIsBlank_ReturnsFallbackItem()
    {
        string sanitized = DeploymentStepExecutionContext.SanitizePathSegment("   ");

        Assert.Equal("item", sanitized);
    }

    [Theory]
    [InlineData(DeploymentMode.Usb, true)]
    [InlineData(DeploymentMode.Usb, false)]
    [InlineData(DeploymentMode.Iso, true)]
    [InlineData(DeploymentMode.Iso, false)]
    public void ResolveCacheRoot_UsesModeSpecificLayout(
        DeploymentMode mode,
        bool isOperatingSystemPayload)
    {
        using TempDeploymentWorkspace workspace = TempDeploymentWorkspace.Create();
        string targetFoundryRoot = Path.Combine(workspace.RootPath, "Windows", "Foundry");
        DeploymentStepExecutionContext context = CreateExecutionContext(
            workspace.RootPath,
            resolvedCacheRootPath: Path.Combine(workspace.CacheRootPath, "Runtime"),
            mode: mode,
            targetFoundryRoot: targetFoundryRoot);

        string root = isOperatingSystemPayload
            ? context.ResolveOperatingSystemCacheRoot()
            : context.ResolveDriverPackCacheRoot();
        string expectedCacheRoot = mode == DeploymentMode.Usb
            ? workspace.CacheRootPath
            : targetFoundryRoot;
        string expectedPayloadDirectory = isOperatingSystemPayload
            ? "OperatingSystems"
            : "DriverPacks";

        Assert.Equal(Path.Combine(expectedCacheRoot, "Cache", expectedPayloadDirectory), root);
    }

    [Fact]
    public async Task RebindLogSessionToTargetAsync_WhenTargetCannotBeInitialized_KeepsCurrentSession()
    {
        using TempDeploymentWorkspace workspace = TempDeploymentWorkspace.Create();
        var logService = new ThrowingRebindLogService(workspace.RootPath);
        DeploymentStepExecutionContext context = CreateExecutionContext(
            workspace.RootPath,
            Path.Combine(workspace.CacheRootPath, "Runtime"),
            logService: logService);
        DeploymentLogSession originalSession = context.LogSession;

        await context.RebindLogSessionToTargetAsync(
            Path.Combine(workspace.RootPath, "unavailable-target"),
            TestContext.Current.CancellationToken);

        Assert.Same(originalSession, context.LogSession);
    }

    [Fact]
    public async Task TryGetValidatedTargetDiskAsync_WhenDiskIsMissing_ReturnsStructuredFailure()
    {
        using TempDeploymentWorkspace workspace = TempDeploymentWorkspace.Create();
        DeploymentStepExecutionContext context = CreateExecutionContext(
            workspace.RootPath,
            Path.Combine(workspace.CacheRootPath, "Runtime"),
            targetDiskService: new FakeTargetDiskService([]));

        (_, DeploymentStepResult? result) = await context.TryGetValidatedTargetDiskAsync(TestContext.Current.CancellationToken);

        Assert.NotNull(result?.Failure);
        Assert.Equal(DeploymentOperationNames.ValidateTargetDisk, result.Failure.OperationName);
        Assert.Equal(DeploymentFailureKinds.Validation, result.Failure.Kind);
        Assert.Equal(DeploymentFailureReasons.MissingResource, result.Failure.Reason);
        Assert.Equal("target_disk_not_found", result.Failure.Code);
    }

    [Fact]
    public async Task TryGetValidatedTargetDiskAsync_WhenDiskIsBlocked_ReturnsStructuredFailure()
    {
        using TempDeploymentWorkspace workspace = TempDeploymentWorkspace.Create();
        var disk = new TargetDiskInfo
        {
            DiskNumber = 1,
            IsSelectable = false,
            SelectionWarning = "System disk"
        };
        DeploymentStepExecutionContext context = CreateExecutionContext(
            workspace.RootPath,
            Path.Combine(workspace.CacheRootPath, "Runtime"),
            targetDiskService: new FakeTargetDiskService([disk]));

        (_, DeploymentStepResult? result) = await context.TryGetValidatedTargetDiskAsync(TestContext.Current.CancellationToken);

        Assert.NotNull(result?.Failure);
        Assert.Equal(DeploymentOperationNames.ValidateTargetDisk, result.Failure.OperationName);
        Assert.Equal(DeploymentFailureKinds.Validation, result.Failure.Kind);
        Assert.Equal(DeploymentFailureReasons.InvalidState, result.Failure.Reason);
        Assert.Equal("target_disk_not_selectable", result.Failure.Code);
    }

    private static DeploymentStepExecutionContext CreateExecutionContext(
        string workspaceRoot,
        string resolvedCacheRootPath,
        DeploymentMode mode = DeploymentMode.Usb,
        string? targetFoundryRoot = null,
        IDeploymentLogService? logService = null,
        ITargetDiskService? targetDiskService = null)
    {
        var request = new DeploymentContext
        {
            Mode = mode,
            CacheRootPath = resolvedCacheRootPath,
            TargetDiskNumber = 1,
            TargetComputerName = "LAB01",
            OperatingSystem = new OperatingSystemCatalogItem(),
            DriverPackSelectionKind = DriverPackSelectionKind.None,
            IsDryRun = false
        };

        var runtimeState = new DeploymentRuntimeState
        {
            WorkspaceRoot = workspaceRoot,
            Mode = mode,
            TargetFoundryRoot = targetFoundryRoot,
            ResolvedCache = new CacheResolution
            {
                RootPath = resolvedCacheRootPath,
                Source = "test"
            }
        };

        return new DeploymentStepExecutionContext(
            request,
            runtimeState,
            [],
            new FakeOperationProgressService(),
            logService ?? new FakeDeploymentLogService(),
            targetDiskService ?? new FakeTargetDiskService([]),
            _ => { });
    }

    private sealed class TempDeploymentWorkspace : IDisposable
    {
        private TempDeploymentWorkspace(string rootPath)
        {
            RootPath = rootPath;
            CacheRootPath = Path.Combine(rootPath, "Foundry Cache");
        }

        public string RootPath { get; }
        public string CacheRootPath { get; }

        public static TempDeploymentWorkspace Create()
        {
            string rootPath = Path.Combine(Path.GetTempPath(), $"foundry-deploy-context-{Guid.NewGuid():N}");
            Directory.CreateDirectory(rootPath);
            return new TempDeploymentWorkspace(rootPath);
        }

        public void Dispose()
        {
            Directory.Delete(RootPath, recursive: true);
        }
    }

    private sealed class FakeDeploymentLogService : IDeploymentLogService
    {
        public DeploymentLogSession Initialize(string rootPath)
        {
            return new DeploymentLogSession
            {
                RootPath = rootPath,
                LogsDirectoryPath = Path.Combine(rootPath, "Logs"),
                StateDirectoryPath = Path.Combine(rootPath, "State"),
                StateFilePath = Path.Combine(rootPath, "State", "deployment-state.json")
            };
        }

        public Task AppendAsync(
            DeploymentLogSession session,
            DeploymentLogLevel level,
            string message,
            CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task SaveStateAsync<TState>(
            DeploymentLogSession session,
            TState state,
            CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

    }

    private sealed class ThrowingRebindLogService(string initialRootPath) : IDeploymentLogService
    {
        public DeploymentLogSession Initialize(string rootPath)
        {
            if (!rootPath.Equals(initialRootPath, StringComparison.OrdinalIgnoreCase))
            {
                throw new IOException("Simulated log destination failure.");
            }

            return new DeploymentLogSession
            {
                RootPath = rootPath,
                LogsDirectoryPath = Path.Combine(rootPath, "Logs"),
                StateDirectoryPath = Path.Combine(rootPath, "State"),
                StateFilePath = Path.Combine(rootPath, "State", "deployment-state.json")
            };
        }

        public Task AppendAsync(
            DeploymentLogSession session,
            DeploymentLogLevel level,
            string message,
            CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task SaveStateAsync<TState>(
            DeploymentLogSession session,
            TState state,
            CancellationToken cancellationToken = default) => Task.CompletedTask;

    }

    private sealed class FakeOperationProgressService : IOperationProgressService
    {
        public bool IsOperationInProgress => false;
        public int Progress => 0;
        public string? Status => null;
        public OperationKind? CurrentOperation => null;
        public bool CanStartOperation => true;
        public event EventHandler? ProgressChanged;
        public bool TryStart(OperationKind kind, string initialStatus, int initialProgress = 0) => true;
        public void Report(int progress, string? status = null) => ProgressChanged?.Invoke(this, EventArgs.Empty);
        public void Complete(string? status = null) => ProgressChanged?.Invoke(this, EventArgs.Empty);
        public void Fail(string status) => ProgressChanged?.Invoke(this, EventArgs.Empty);
        public void ResetToIdle() => ProgressChanged?.Invoke(this, EventArgs.Empty);
    }

    private sealed class FakeTargetDiskService(IReadOnlyList<TargetDiskInfo> disks) : ITargetDiskService
    {
        public Task<IReadOnlyList<TargetDiskInfo>> GetDisksAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(disks);
        }

        public Task<int?> GetDiskNumberForPathAsync(string path, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<int?>(null);
        }
    }
}
