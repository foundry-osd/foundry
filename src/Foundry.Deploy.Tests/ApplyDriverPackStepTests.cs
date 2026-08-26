// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Deploy.Models;
using Foundry.Deploy.Models.Configuration;
using Foundry.Deploy.Services.Deployment;
using Foundry.Deploy.Services.Deployment.Steps;
using Foundry.Deploy.Services.DriverPacks;
using Foundry.Deploy.Services.Hardware;
using Foundry.Deploy.Services.Logging;
using Foundry.Deploy.Services.Operations;

namespace Foundry.Deploy.Tests;

public sealed class ApplyDriverPackStepTests
{
    [Fact]
    public async Task ExecuteAsync_WhenOfflineInfAndRecoveryConfigured_AppliesDriversOnlyToWindows()
    {
        string rootPath = Path.Combine(Path.GetTempPath(), "Foundry.Deploy.Tests", Guid.NewGuid().ToString("N"));
        string workspaceRoot = Path.Combine(rootPath, "Workspace");
        string windowsRoot = Path.Combine(rootPath, "Windows");
        string recoveryRoot = Path.Combine(rootPath, "Recovery");
        string driverRoot = Path.Combine(rootPath, "Drivers");

        try
        {
            Directory.CreateDirectory(workspaceRoot);
            Directory.CreateDirectory(windowsRoot);
            Directory.CreateDirectory(recoveryRoot);
            Directory.CreateDirectory(driverRoot);
            await File.WriteAllTextAsync(
                Path.Combine(driverRoot, "driver.inf"),
                "[Version]",
                TestContext.Current.CancellationToken);

            var deploymentService = new RecordingWindowsDeploymentService();
            DeploymentStepExecutionContext context = CreateContext(
                workspaceRoot,
                windowsRoot,
                recoveryRoot,
                driverRoot);
            var step = new ApplyDriverPackStep(deploymentService);

            DeploymentStepResult result = await step.ExecuteAsync(
                context,
                TestContext.Current.CancellationToken);

            Assert.Equal(DeploymentStepState.Succeeded, result.State);
            Assert.Equal(1, deploymentService.WindowsApplyCount);
            Assert.Equal(0, deploymentService.RecoveryApplyCount);
        }
        finally
        {
            if (Directory.Exists(rootPath))
            {
                Directory.Delete(rootPath, recursive: true);
            }
        }
    }

    [Fact]
    public async Task RecoveryStep_WhenOfflineInfAndRecoveryConfigured_AppliesDriversOnlyToRecovery()
    {
        string rootPath = Path.Combine(Path.GetTempPath(), "Foundry.Deploy.Tests", Guid.NewGuid().ToString("N"));
        string workspaceRoot = Path.Combine(rootPath, "Workspace");
        string windowsRoot = Path.Combine(rootPath, "Windows");
        string recoveryRoot = Path.Combine(rootPath, "Recovery");
        string driverRoot = Path.Combine(rootPath, "Drivers");

        try
        {
            Directory.CreateDirectory(workspaceRoot);
            Directory.CreateDirectory(windowsRoot);
            Directory.CreateDirectory(recoveryRoot);
            Directory.CreateDirectory(driverRoot);
            await File.WriteAllTextAsync(
                Path.Combine(driverRoot, "driver.inf"),
                "[Version]",
                TestContext.Current.CancellationToken);

            var deploymentService = new RecordingWindowsDeploymentService();
            DeploymentStepExecutionContext context = CreateContext(
                workspaceRoot,
                windowsRoot,
                recoveryRoot,
                driverRoot);
            var step = new ApplyRecoveryDriversStep(deploymentService);

            DeploymentStepResult result = await step.ExecuteAsync(
                context,
                TestContext.Current.CancellationToken);

            Assert.Equal(DeploymentStepState.Succeeded, result.State);
            Assert.Equal(0, deploymentService.WindowsApplyCount);
            Assert.Equal(1, deploymentService.RecoveryApplyCount);
        }
        finally
        {
            if (Directory.Exists(rootPath))
            {
                Directory.Delete(rootPath, recursive: true);
            }
        }
    }

    [Theory]
    [InlineData(DriverPackInstallMode.None)]
    [InlineData(DriverPackInstallMode.DeferredSetupComplete)]
    public async Task RecoveryStep_WhenRecoveryServicingIsNotRequired_Skips(
        DriverPackInstallMode installMode)
    {
        string rootPath = Path.Combine(Path.GetTempPath(), "Foundry.Deploy.Tests", Guid.NewGuid().ToString("N"));
        string workspaceRoot = Path.Combine(rootPath, "Workspace");
        string windowsRoot = Path.Combine(rootPath, "Windows");
        string recoveryRoot = Path.Combine(rootPath, "Recovery");
        string driverRoot = Path.Combine(rootPath, "Drivers");

        try
        {
            Directory.CreateDirectory(workspaceRoot);
            Directory.CreateDirectory(windowsRoot);
            Directory.CreateDirectory(recoveryRoot);
            Directory.CreateDirectory(driverRoot);

            var deploymentService = new RecordingWindowsDeploymentService();
            DeploymentStepExecutionContext context = CreateContext(
                workspaceRoot,
                windowsRoot,
                recoveryRoot,
                driverRoot);
            context.RuntimeState.DriverPackInstallMode = installMode;
            var step = new ApplyRecoveryDriversStep(deploymentService);

            DeploymentStepResult result = await step.ExecuteAsync(
                context,
                TestContext.Current.CancellationToken);

            Assert.Equal(DeploymentStepState.Skipped, result.State);
            Assert.Equal(0, deploymentService.WindowsApplyCount);
            Assert.Equal(0, deploymentService.RecoveryApplyCount);
        }
        finally
        {
            if (Directory.Exists(rootPath))
            {
                Directory.Delete(rootPath, recursive: true);
            }
        }
    }

    [Fact]
    public async Task ExecuteAsync_WhenDriverPackIsDeferred_SkipsWithoutStaging()
    {
        string rootPath = Path.Combine(Path.GetTempPath(), "Foundry.Deploy.Tests", Guid.NewGuid().ToString("N"));
        string workspaceRoot = Path.Combine(rootPath, "Workspace");
        string windowsRoot = Path.Combine(rootPath, "Windows");
        string recoveryRoot = Path.Combine(rootPath, "Recovery");
        string driverRoot = Path.Combine(rootPath, "Drivers");

        try
        {
            Directory.CreateDirectory(workspaceRoot);
            Directory.CreateDirectory(windowsRoot);
            Directory.CreateDirectory(recoveryRoot);
            Directory.CreateDirectory(driverRoot);
            string packagePath = Path.Combine(rootPath, "driver.exe");
            await File.WriteAllBytesAsync(
                packagePath,
                [1, 2, 3],
                TestContext.Current.CancellationToken);

            var deploymentService = new RecordingWindowsDeploymentService();
            DeploymentStepExecutionContext context = CreateContext(
                workspaceRoot,
                windowsRoot,
                recoveryRoot,
                driverRoot);
            context.RuntimeState.DriverPackInstallMode = DriverPackInstallMode.DeferredSetupComplete;
            context.RuntimeState.DownloadedDriverPackPath = packagePath;
            var step = new ApplyDriverPackStep(deploymentService);

            DeploymentStepResult result = await step.ExecuteAsync(
                context,
                TestContext.Current.CancellationToken);

            Assert.Equal(DeploymentStepState.Skipped, result.State);
            Assert.Null(context.RuntimeState.DeferredDriverPackagePath);
            Assert.Equal(0, deploymentService.WindowsApplyCount);
            Assert.Equal(0, deploymentService.RecoveryApplyCount);
        }
        finally
        {
            if (Directory.Exists(rootPath))
            {
                Directory.Delete(rootPath, recursive: true);
            }
        }
    }

    private static DeploymentStepExecutionContext CreateContext(
        string workspaceRoot,
        string windowsRoot,
        string recoveryRoot,
        string driverRoot)
    {
        var request = new DeploymentContext
        {
            Mode = DeploymentMode.Iso,
            CacheRootPath = workspaceRoot,
            TargetDiskNumber = 1,
            TargetComputerName = "LAB01",
            OperatingSystem = new OperatingSystemCatalogItem(),
            DriverPackSelectionKind = DriverPackSelectionKind.OemCatalog
        };
        var runtimeState = new DeploymentRuntimeState
        {
            WorkspaceRoot = workspaceRoot,
            TargetWindowsPartitionRoot = windowsRoot,
            TargetRecoveryPartitionRoot = recoveryRoot,
            TargetFoundryRoot = Path.Combine(windowsRoot, "Foundry"),
            ExtractedDriverPackPath = driverRoot,
            DriverPackInstallMode = DriverPackInstallMode.OfflineInf,
            WinReConfigured = true
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

    private sealed class RecordingWindowsDeploymentService : IWindowsDeploymentService
    {
        public int WindowsApplyCount { get; private set; }

        public int RecoveryApplyCount { get; private set; }

        public Task ApplyOfflineDriversAsync(
            string windowsPartitionRoot,
            string driverRoot,
            string scratchDirectory,
            string workingDirectory,
            CancellationToken cancellationToken = default,
            IProgress<double>? progress = null)
        {
            WindowsApplyCount++;
            return Task.CompletedTask;
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
            RecoveryApplyCount++;
            return Task.CompletedTask;
        }

        public Task<DeploymentTargetLayout> PrepareTargetDiskAsync(int diskNumber, string workingDirectory, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<int> ResolveImageIndexAsync(string imagePath, string requestedEdition, string workingDirectory, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task ApplyImageAsync(string imagePath, int imageIndex, string windowsPartitionRoot, string scratchDirectory, string workingDirectory, CancellationToken cancellationToken = default, IProgress<double>? progress = null) => throw new NotSupportedException();

        public Task<string?> GetAppliedWindowsEditionAsync(string windowsPartitionRoot, string workingDirectory, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task ConfigureBootAsync(string windowsPartitionRoot, string systemPartitionRoot, int operatingSystemBuildMajor, string workingDirectory, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task ConfigureOfflineComputerNameAsync(string windowsPartitionRoot, string computerName, string processorArchitecture, string? defaultTimeZoneId = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task ConfigureOfflineOobeAsync(string windowsPartitionRoot, DeployOobeSettings settings, string processorArchitecture, string workingDirectory, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task ConfigureOfflineAiComponentRemovalAsync(string windowsPartitionRoot, DeployAiComponentRemovalSettings settings, string workingDirectory, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<WindowsOptionalFeatureServicingResult> ConfigureOfflineWindowsOptionalFeaturesAsync(string setupMediaImagePath, string windowsPartitionRoot, int appliedImageIndex, DeployWindowsOptionalFeatureSettings settings, string scratchDirectory, string sourceExtractionDirectory, string workingDirectory, CancellationToken cancellationToken = default, IProgress<double>? progress = null, Action? onInspectionStarted = null, Action? onSourcePreparationStarted = null, Action? onServicingStarted = null) => throw new NotSupportedException();

        public Task ConfigureRecoveryEnvironmentAsync(string windowsPartitionRoot, string recoveryPartitionRoot, string workingDirectory, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task SealRecoveryPartitionAsync(string recoveryPartitionRoot, char recoveryPartitionLetter, string workingDirectory, CancellationToken cancellationToken = default) => throw new NotSupportedException();
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

        public Task AppendAsync(DeploymentLogSession session, DeploymentLogLevel level, string message, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task SaveStateAsync<TState>(DeploymentLogSession session, TState state, CancellationToken cancellationToken = default) => Task.CompletedTask;

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
        public Task<IReadOnlyList<TargetDiskInfo>> GetDisksAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<TargetDiskInfo>>([]);

        public Task<int?> GetDiskNumberForPathAsync(string path, CancellationToken cancellationToken = default) => Task.FromResult<int?>(null);
    }
}
