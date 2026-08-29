// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Deploy.Models;
using Foundry.Deploy.Models.Configuration;
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
    public void Constructor_WhenStepsAreRegisteredOutOfOrder_UsesCanonicalExecutionOrder()
    {
        string[] expectedOrder =
        [
            DeploymentStepNames.GatherDeploymentVariables,
            DeploymentStepNames.InitializeDeploymentWorkspace,
            DeploymentStepNames.ValidateTargetConfiguration,
            DeploymentStepNames.ResolveCacheStrategy,
            DeploymentStepNames.PrepareTargetDiskLayout,
            DeploymentStepNames.DownloadOperatingSystemImage,
            DeploymentStepNames.ApplyOperatingSystemImage,
            DeploymentStepNames.DownloadDriverPack,
            DeploymentStepNames.ExtractDriverPack,
            DeploymentStepNames.ApplyDriverPack,
            DeploymentStepNames.DownloadFirmwareUpdate,
            DeploymentStepNames.ApplyFirmwareUpdate,
            DeploymentStepNames.ConfigureTargetComputerName,
            DeploymentStepNames.ConfigureOobeSettings,
            DeploymentStepNames.ConfigureWindowsOptionalFeatures,
            DeploymentStepNames.StagePreOobeCustomization,
            DeploymentStepNames.ConfigureRecoveryEnvironment,
            DeploymentStepNames.ApplyRecoveryDrivers,
            DeploymentStepNames.SealRecoveryPartition,
            DeploymentStepNames.ProvisionAutopilot,
            DeploymentStepNames.FinalizeDeploymentAndWriteLogs
        ];
        IDeploymentStep[] registeredSteps = expectedOrder
            .Reverse()
            .Select(name => (IDeploymentStep)new SucceedingStep(name))
            .ToArray();

        var orchestrator = new DeploymentOrchestrator(
            new FakeOperationProgressService(),
            new FakeDeploymentLogService(),
            new FakeTargetDiskService(),
            registeredSteps,
            new RecordingTelemetryService(),
            NullLogger<DeploymentOrchestrator>.Instance);

        Assert.Equal(expectedOrder, orchestrator.PlannedSteps);
    }

    [Fact]
    public void Constructor_WhenStepRegistrationIsDuplicated_Throws()
    {
        IDeploymentStep[] steps = DeploymentStepNames.ExecutionOrder
            .Select(name => (IDeploymentStep)new SucceedingStep(name))
            .Append(new SucceedingStep(DeploymentStepNames.ApplyDriverPack))
            .ToArray();

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() => CreateOrchestrator(steps));

        Assert.Contains("Duplicate deployment step registration", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Constructor_WhenStepRegistrationIsMissing_Throws()
    {
        IDeploymentStep[] steps = DeploymentStepNames.ExecutionOrder
            .Where(name => name != DeploymentStepNames.ApplyRecoveryDrivers)
            .Select(name => (IDeploymentStep)new SucceedingStep(name))
            .ToArray();

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() => CreateOrchestrator(steps));

        Assert.Contains(DeploymentStepNames.ApplyRecoveryDrivers, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Constructor_WhenStepRegistrationIsUnexpected_Throws()
    {
        IDeploymentStep[] steps = DeploymentStepNames.ExecutionOrder
            .Select(name => (IDeploymentStep)new SucceedingStep(name))
            .Append(new SucceedingStep("Unexpected deployment step"))
            .ToArray();

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() => CreateOrchestrator(steps));

        Assert.Contains("Unexpected deployment step", exception.Message, StringComparison.Ordinal);
    }

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
                MediaDate = new DateOnly(2026, 7, 10),
                Architecture = "x64",
                LanguageCode = "en-US",
                Edition = "Pro",
                LicenseChannel = "RET"
            },
            DriverPackSelectionKind = DriverPackSelectionKind.OemCatalog,
            DriverPack = new DriverPackCatalogItem
            {
                Manufacturer = "Dell",
                Name = "pc14255-x20jr_win11_1.0_a05.exe",
                ModelNames = ["Latitude 5450"]
            },
            ApplyFirmwareUpdates = true,
            IsAutopilotEnabled = true,
            AutopilotProvisioningMode = AutopilotProvisioningMode.HardwareHashUpload,
            AutopilotHardwareHashUpload = new DeployAutopilotHardwareHashUploadSettings
            {
                DefaultGroupTag = "Sales"
            }
        });

        TelemetryEvent telemetryEvent = Assert.Single(telemetryService.Events);
        Assert.Equal(TelemetryEvents.DeploySessionFinished, telemetryEvent.Name);
        Assert.False((bool)telemetryEvent.Properties["deploy_session_success"]!);
        Assert.False((bool)telemetryEvent.Properties["deploy_session_cancelled"]!);
        Assert.Equal(DeploymentStepNames.DownloadOperatingSystemImage, telemetryEvent.Properties["deploy_session_failed_step_name"]);
        Assert.Equal("os_image.download", telemetryEvent.Properties["deploy_session_failed_operation_name"]);
        Assert.Equal("validation", telemetryEvent.Properties["deploy_session_failure_kind"]);
        Assert.Equal("invalid_state", telemetryEvent.Properties["deploy_session_failure_reason"]);
        Assert.Equal("synthetic_failure", telemetryEvent.Properties["deploy_session_failure_code"]);
        Assert.Equal("windows_11", telemetryEvent.Properties["deploy_os_product"]);
        Assert.Equal("2026-07", telemetryEvent.Properties["deploy_os_update_month"]);
        Assert.Equal("pro", telemetryEvent.Properties["deploy_os_edition"]);
        Assert.Equal("ret", telemetryEvent.Properties["deploy_os_license_channel"]);
        Assert.Equal(6, telemetryEvent.Properties["deploy_os_image_index"]);
        Assert.Equal("dell", telemetryEvent.Properties["deploy_driver_pack_vendor"]);
        Assert.Equal("latitude 5450", telemetryEvent.Properties["deploy_driver_pack_model"]);
        Assert.True((bool)telemetryEvent.Properties["deploy_firmware_updates_enabled"]!);
        Assert.True((bool)telemetryEvent.Properties["deploy_autopilot_enabled"]!);
        Assert.Equal("hardware_hash_upload", telemetryEvent.Properties["deploy_autopilot_provisioning_mode"]);
        Assert.Equal("planned", telemetryEvent.Properties["deploy_autopilot_hash_upload_state"]);
        Assert.True((bool)telemetryEvent.Properties["deploy_autopilot_hash_group_tag_selected"]!);
        Assert.False(telemetryEvent.Properties.ContainsKey("success"));
        Assert.False(telemetryEvent.Properties.ContainsKey("autopilot_enabled"));
    }

    [Theory]
    [InlineData(false, 42, "manual", null)]
    [InlineData(true, 0, "immediate", null)]
    [InlineData(true, 42, "countdown", 42)]
    public async Task RunAsync_TracksConfiguredCompletionRebootTelemetry(
        bool automaticRebootEnabled,
        int delaySeconds,
        string expectedMode,
        int? expectedDelaySeconds)
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
            OperatingSystem = new OperatingSystemCatalogItem(),
            DriverPackSelectionKind = DriverPackSelectionKind.None,
            Completion = new DeployCompletionSettings
            {
                AutomaticRebootEnabled = automaticRebootEnabled,
                AutomaticRebootDelaySeconds = delaySeconds
            }
        });

        TelemetryEvent telemetryEvent = Assert.Single(telemetryService.Events);

        Assert.Equal(expectedMode, telemetryEvent.Properties["deploy_completion_reboot_mode"]);
        if (expectedDelaySeconds is null)
        {
            Assert.False(telemetryEvent.Properties.ContainsKey("deploy_completion_reboot_delay_seconds"));
        }
        else
        {
            Assert.Equal(expectedDelaySeconds, telemetryEvent.Properties["deploy_completion_reboot_delay_seconds"]);
        }
    }

    private static IDeploymentStep[] CreateSteps(string targetWindowsRoot)
    {
        return DeploymentStepNames.ExecutionOrder
            .Select(name => (IDeploymentStep)(name switch
            {
                DeploymentStepNames.PrepareTargetDiskLayout => new PrepareTargetLayoutStep(targetWindowsRoot),
                DeploymentStepNames.DownloadOperatingSystemImage => new FailingStep(name),
                _ => new SucceedingStep(name)
            }))
            .ToArray();
    }

    private static DeploymentOrchestrator CreateOrchestrator(IEnumerable<IDeploymentStep> steps)
    {
        return new DeploymentOrchestrator(
            new FakeOperationProgressService(),
            new FakeDeploymentLogService(),
            new FakeTargetDiskService(),
            steps,
            new RecordingTelemetryService(),
            NullLogger<DeploymentOrchestrator>.Instance);
    }

    private sealed class PrepareTargetLayoutStep(string targetWindowsRoot) : IDeploymentStep
    {
        public string Name => DeploymentStepNames.PrepareTargetDiskLayout;

        public async Task<DeploymentStepResult> ExecuteAsync(
            DeploymentStepExecutionContext context,
            CancellationToken cancellationToken)
        {
            context.RuntimeState.TargetWindowsPartitionRoot = targetWindowsRoot;
            context.RuntimeState.TargetFoundryRoot = Path.Combine(targetWindowsRoot, "Foundry");
            context.RuntimeState.AppliedImageIndex = 6;
            Directory.CreateDirectory(context.RuntimeState.TargetFoundryRoot);
            await context.RebindLogSessionToTargetAsync(context.RuntimeState.TargetFoundryRoot, cancellationToken);
            return DeploymentStepResult.Succeeded("Prepared target layout.");
        }
    }

    private sealed class FailingStep(string name) : IDeploymentStep
    {
        public string Name { get; } = name;

        public Task<DeploymentStepResult> ExecuteAsync(
            DeploymentStepExecutionContext context,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(DeploymentStepResult.Failed(
                "Synthetic failure after target layout.",
                new DeploymentFailure(
                    "os_image.download",
                    DeploymentFailureKinds.Validation,
                    DeploymentFailureReasons.InvalidState,
                    "synthetic_failure")));
        }
    }

    private sealed class SucceedingStep(string name) : IDeploymentStep
    {
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
