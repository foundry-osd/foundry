using Foundry.Deploy.Models;
using Foundry.Deploy.Models.Configuration;
using Foundry.Deploy.Services.Cache;
using Foundry.Deploy.Services.Deployment;
using Foundry.Deploy.Services.Deployment.Steps;
using Foundry.Deploy.Services.Hardware;
using Foundry.Deploy.Services.Logging;
using Foundry.Deploy.Services.Operations;

namespace Foundry.Deploy.Tests;

public sealed class PrepareTargetDiskLayoutStepTests
{
    [Fact]
    public async Task ExecuteAsync_WhenIsoMode_PreparesTargetWorkspaceWithoutEagerPayloadCacheFolders()
    {
        using TempDeploymentWorkspace workspace = TempDeploymentWorkspace.Create();
        var step = new PrepareTargetDiskLayoutStep(new FakeWindowsDeploymentService(workspace));
        DeploymentStepExecutionContext context = CreateExecutionContext(workspace, DeploymentMode.Iso);

        DeploymentStepResult result = await step.ExecuteAsync(context, CancellationToken.None);

        Assert.Equal(DeploymentStepState.Succeeded, result.State);
        string targetFoundryRoot = Path.Combine(workspace.WindowsRoot, "Foundry");
        Assert.Equal(targetFoundryRoot, context.RuntimeState.TargetFoundryRoot);
        Assert.True(Directory.Exists(Path.Combine(targetFoundryRoot, "Logs")));
        Assert.True(Directory.Exists(Path.Combine(targetFoundryRoot, "State")));
        Assert.False(Directory.Exists(Path.Combine(targetFoundryRoot, "Cache", "OperatingSystems")));
        Assert.False(Directory.Exists(Path.Combine(targetFoundryRoot, "Cache", "DriverPacks")));
    }

    private static DeploymentStepExecutionContext CreateExecutionContext(
        TempDeploymentWorkspace workspace,
        DeploymentMode mode)
    {
        var request = new DeploymentContext
        {
            Mode = mode,
            CacheRootPath = workspace.CacheRuntimeRoot,
            TargetDiskNumber = 1,
            TargetComputerName = "LAB01",
            OperatingSystem = new OperatingSystemCatalogItem(),
            DriverPackSelectionKind = DriverPackSelectionKind.None,
            IsDryRun = false
        };

        var runtimeState = new DeploymentRuntimeState
        {
            WorkspaceRoot = workspace.WorkspaceRoot,
            Mode = mode,
            ResolvedCache = new CacheResolution
            {
                RootPath = workspace.CacheRuntimeRoot,
                Source = "test"
            }
        };

        return new DeploymentStepExecutionContext(
            request,
            runtimeState,
            [],
            new FakeOperationProgressService(),
            new FakeDeploymentLogService(),
            new FakeTargetDiskService(),
            _ => { });
    }

    private sealed class FakeWindowsDeploymentService : IWindowsDeploymentService
    {
        private readonly TempDeploymentWorkspace _workspace;

        public FakeWindowsDeploymentService(TempDeploymentWorkspace workspace)
        {
            _workspace = workspace;
        }

        public Task<DeploymentTargetLayout> PrepareTargetDiskAsync(
            int diskNumber,
            string workingDirectory,
            CancellationToken cancellationToken = default)
        {
            Directory.CreateDirectory(workingDirectory);

            return Task.FromResult(new DeploymentTargetLayout
            {
                DiskNumber = diskNumber,
                SystemPartitionRoot = _workspace.SystemRoot,
                WindowsPartitionRoot = _workspace.WindowsRoot,
                RecoveryPartitionRoot = _workspace.RecoveryRoot,
                RecoveryPartitionLetter = 'R'
            });
        }

        public Task<int> ResolveImageIndexAsync(
            string imagePath,
            string requestedEdition,
            string workingDirectory,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task ApplyImageAsync(
            string imagePath,
            int imageIndex,
            string windowsPartitionRoot,
            string scratchDirectory,
            string workingDirectory,
            CancellationToken cancellationToken = default,
            IProgress<double>? progress = null)
        {
            throw new NotSupportedException();
        }

        public Task<string?> GetAppliedWindowsEditionAsync(
            string windowsPartitionRoot,
            string workingDirectory,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task ConfigureOfflineComputerNameAsync(
            string windowsPartitionRoot,
            string computerName,
            string processorArchitecture,
            string? defaultTimeZoneId = null,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task ConfigureOfflineOobeAsync(
            string windowsPartitionRoot,
            DeployOobeSettings settings,
            string processorArchitecture,
            string workingDirectory,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task ConfigureRecoveryEnvironmentAsync(
            string windowsPartitionRoot,
            string recoveryPartitionRoot,
            string workingDirectory,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task SealRecoveryPartitionAsync(
            string recoveryPartitionRoot,
            char recoveryPartitionLetter,
            string workingDirectory,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task ApplyOfflineDriversAsync(
            string windowsPartitionRoot,
            string driverRoot,
            string scratchDirectory,
            string workingDirectory,
            CancellationToken cancellationToken = default,
            IProgress<double>? progress = null)
        {
            throw new NotSupportedException();
        }

        public Task ApplyRecoveryDriversAsync(
            string recoveryPartitionRoot,
            string driverRoot,
            string scratchDirectory,
            string workingDirectory,
            CancellationToken cancellationToken = default,
            IProgress<double>? mountProgress = null,
            IProgress<double>? applyProgress = null,
            IProgress<double>? unmountProgress = null,
            Action? onMountStarted = null,
            Action? onApplyStarted = null,
            Action? onUnmountStarted = null)
        {
            throw new NotSupportedException();
        }

        public Task ConfigureBootAsync(
            string windowsPartitionRoot,
            string systemPartitionRoot,
            int operatingSystemBuildMajor,
            string workingDirectory,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }
    }

    private sealed class FakeDeploymentLogService : IDeploymentLogService
    {
        public DeploymentLogSession Initialize(string rootPath)
        {
            string logsDirectory = Path.Combine(rootPath, "Logs");
            string stateDirectory = Path.Combine(rootPath, "State");
            Directory.CreateDirectory(logsDirectory);
            Directory.CreateDirectory(stateDirectory);

            return new DeploymentLogSession
            {
                RootPath = rootPath,
                LogsDirectoryPath = logsDirectory,
                StateDirectoryPath = stateDirectory,
                LogFilePath = Path.Combine(logsDirectory, "FoundryDeploy.log"),
                StateFilePath = Path.Combine(stateDirectory, "deployment-state.json")
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

        public void Release(DeploymentLogSession session)
        {
        }
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

    private sealed class FakeTargetDiskService : ITargetDiskService
    {
        public Task<IReadOnlyList<TargetDiskInfo>> GetDisksAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<TargetDiskInfo>>(
            [
                new TargetDiskInfo
                {
                    DiskNumber = 1,
                    FriendlyName = "Disk 1",
                    IsSelectable = true
                }
            ]);
        }

        public Task<int?> GetDiskNumberForPathAsync(string path, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<int?>(null);
        }
    }

    private sealed class TempDeploymentWorkspace : IDisposable
    {
        private TempDeploymentWorkspace(string rootPath)
        {
            RootPath = rootPath;
            WorkspaceRoot = Path.Combine(rootPath, "Workspace");
            CacheRuntimeRoot = Path.Combine(rootPath, "Foundry Cache", "Runtime");
            SystemRoot = Path.Combine(rootPath, "System");
            WindowsRoot = Path.Combine(rootPath, "Windows");
            RecoveryRoot = Path.Combine(rootPath, "Recovery");
            Directory.CreateDirectory(WorkspaceRoot);
            Directory.CreateDirectory(CacheRuntimeRoot);
        }

        public string RootPath { get; }
        public string WorkspaceRoot { get; }
        public string CacheRuntimeRoot { get; }
        public string SystemRoot { get; }
        public string WindowsRoot { get; }
        public string RecoveryRoot { get; }

        public static TempDeploymentWorkspace Create()
        {
            string rootPath = Path.Combine(Path.GetTempPath(), $"foundry-prepare-target-{Guid.NewGuid():N}");
            return new TempDeploymentWorkspace(rootPath);
        }

        public void Dispose()
        {
            Directory.Delete(RootPath, recursive: true);
        }
    }
}
