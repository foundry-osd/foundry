using Foundry.Deploy.Models;
using Foundry.Deploy.Services.Deployment;
using Foundry.Deploy.Services.Hardware;
using Foundry.Deploy.Services.Logging;
using Foundry.Deploy.Services.Operations;
using Foundry.Telemetry;
using Microsoft.Extensions.Logging.Abstractions;

namespace Foundry.Deploy.Tests;

public sealed class DeploymentOrchestratorTests
{
    [Fact]
    public async Task RunAsync_WhenDeploymentFailsAfterTargetLayout_ReturnsActualReboundLogPath()
    {
        using TempDeploymentWorkspace workspace = TempDeploymentWorkspace.Create();
        string targetWindowsRoot = Path.Combine(workspace.RootPath, "TargetWindows");
        IDeploymentStep[] steps = CreateSteps(targetWindowsRoot);
        var logService = new FakeDeploymentLogService();
        var orchestrator = new DeploymentOrchestrator(
            new FakeOperationProgressService(),
            logService,
            new FakeTargetDiskService(),
            steps,
            new RecordingTelemetryService(),
            NullLogger<DeploymentOrchestrator>.Instance);

        DeploymentResult result = await orchestrator.RunAsync(new DeploymentContext
        {
            Mode = DeploymentMode.Iso,
            IsDryRun = false,
            CacheRootPath = workspace.RootPath,
            TargetDiskNumber = 1,
            TargetComputerName = "LAB01",
            OperatingSystem = new OperatingSystemCatalogItem(),
            DriverPackSelectionKind = DriverPackSelectionKind.None
        });

        string expectedFinalLogsPath = Path.Combine(targetWindowsRoot, "Windows", "Temp", "Foundry", "Logs");
        Assert.False(result.IsSuccess);
        Assert.Equal(expectedFinalLogsPath, result.LogsDirectoryPath);
        Assert.True(Directory.Exists(expectedFinalLogsPath));
    }

    [Fact]
    public async Task RunAsync_WhenDeploymentFails_TracksCompletionTelemetry()
    {
        using TempDeploymentWorkspace workspace = TempDeploymentWorkspace.Create();
        var telemetryService = new RecordingTelemetryService();
        var orchestrator = new DeploymentOrchestrator(
            new FakeOperationProgressService(),
            new FakeDeploymentLogService(),
            new FakeTargetDiskService(),
            CreateSteps(Path.Combine(workspace.RootPath, "TargetWindows")),
            telemetryService,
            NullLogger<DeploymentOrchestrator>.Instance);

        await orchestrator.RunAsync(new DeploymentContext
        {
            Mode = DeploymentMode.Iso,
            IsDryRun = false,
            CacheRootPath = workspace.RootPath,
            TargetDiskNumber = 1,
            TargetComputerName = "LAB01",
            OperatingSystem = new OperatingSystemCatalogItem
            {
                WindowsRelease = "11",
                ReleaseId = "24H2",
                Build = "26100",
                Architecture = "x64",
                LanguageCode = "en-US"
            },
            DriverPackSelectionKind = DriverPackSelectionKind.OemCatalog,
            DriverPack = new DriverPackCatalogItem
            {
                Manufacturer = "Dell",
                Name = "pc14255-x20jr_win11_1.0_a05.exe",
                ModelNames = ["Latitude 5450"]
            },
            ApplyFirmwareUpdates = true,
            IsAutopilotEnabled = true
        });

        TelemetryEvent telemetryEvent = Assert.Single(telemetryService.Events);
        Assert.Equal(TelemetryEvents.DeploySessionFinished, telemetryEvent.Name);
        Assert.False((bool)telemetryEvent.Properties["success"]!);
        Assert.False((bool)telemetryEvent.Properties["cancelled"]!);
        Assert.Equal(DeploymentStepNames.DownloadOperatingSystemImage, telemetryEvent.Properties["failed_step_name"]);
        Assert.Equal("windows_11", telemetryEvent.Properties["os_product"]);
        Assert.Equal("dell", telemetryEvent.Properties["driver_pack_vendor"]);
        Assert.Equal("latitude 5450", telemetryEvent.Properties["driver_pack_model"]);
        Assert.True((bool)telemetryEvent.Properties["firmware_updates_enabled"]!);
        Assert.True((bool)telemetryEvent.Properties["autopilot_enabled"]!);
    }

    private static IDeploymentStep[] CreateSteps(string targetWindowsRoot)
    {
        return DeploymentStepNames.All
            .Select((name, index) => (IDeploymentStep)(name switch
            {
                DeploymentStepNames.PrepareTargetDiskLayout => new PrepareTargetLayoutStep(index + 1, targetWindowsRoot),
                DeploymentStepNames.DownloadOperatingSystemImage => new FailingStep(index + 1, name),
                _ => new SucceedingStep(index + 1, name)
            }))
            .ToArray();
    }

    private sealed class PrepareTargetLayoutStep(int order, string targetWindowsRoot) : IDeploymentStep
    {
        public int Order => order;
        public string Name => DeploymentStepNames.PrepareTargetDiskLayout;

        public async Task<DeploymentStepResult> ExecuteAsync(
            DeploymentStepExecutionContext context,
            CancellationToken cancellationToken)
        {
            context.RuntimeState.TargetWindowsPartitionRoot = targetWindowsRoot;
            context.RuntimeState.TargetFoundryRoot = Path.Combine(targetWindowsRoot, "Foundry");
            Directory.CreateDirectory(context.RuntimeState.TargetFoundryRoot);
            await context.RebindLogSessionToTargetAsync(context.RuntimeState.TargetFoundryRoot, cancellationToken);
            return DeploymentStepResult.Succeeded("Prepared target layout.");
        }
    }

    private sealed class FailingStep(int order, string name) : IDeploymentStep
    {
        public int Order => order;
        public string Name { get; } = name;

        public Task<DeploymentStepResult> ExecuteAsync(
            DeploymentStepExecutionContext context,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(DeploymentStepResult.Failed("Synthetic failure after target layout."));
        }
    }

    private sealed class SucceedingStep(int order, string name) : IDeploymentStep
    {
        public int Order => order;
        public string Name { get; } = name;

        public Task<DeploymentStepResult> ExecuteAsync(
            DeploymentStepExecutionContext context,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(DeploymentStepResult.Succeeded($"Completed {Name}."));
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

        public async Task AppendAsync(
            DeploymentLogSession session,
            DeploymentLogLevel level,
            string message,
            CancellationToken cancellationToken = default)
        {
            Directory.CreateDirectory(session.LogsDirectoryPath);
            await File.AppendAllTextAsync(session.LogFilePath, $"{level}: {message}{Environment.NewLine}", cancellationToken);
        }

        public async Task SaveStateAsync<TState>(
            DeploymentLogSession session,
            TState state,
            CancellationToken cancellationToken = default)
        {
            Directory.CreateDirectory(session.StateDirectoryPath);
            await File.WriteAllTextAsync(session.StateFilePath, "{}", cancellationToken);
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
            return Task.FromResult<IReadOnlyList<TargetDiskInfo>>([]);
        }

        public Task<int?> GetDiskNumberForPathAsync(string path, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<int?>(null);
        }
    }

    private sealed class RecordingTelemetryService : ITelemetryService
    {
        public List<TelemetryEvent> Events { get; } = [];

        public Task TrackAsync(
            string eventName,
            IReadOnlyDictionary<string, object?> properties,
            CancellationToken cancellationToken = default)
        {
            Events.Add(new TelemetryEvent(eventName, new Dictionary<string, object?>(properties)));
            return Task.CompletedTask;
        }

        public Task FlushAsync(CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }

    private sealed class TempDeploymentWorkspace : IDisposable
    {
        private TempDeploymentWorkspace(string rootPath)
        {
            RootPath = rootPath;
        }

        public string RootPath { get; }

        public static TempDeploymentWorkspace Create()
        {
            string rootPath = Path.Combine(Path.GetTempPath(), $"foundry-orchestrator-{Guid.NewGuid():N}");
            Directory.CreateDirectory(rootPath);
            return new TempDeploymentWorkspace(rootPath);
        }

        public void Dispose()
        {
            Directory.Delete(RootPath, recursive: true);
        }
    }
}
