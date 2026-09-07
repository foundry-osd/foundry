// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Reflection;
using Foundry.Deploy.DependencyInjection;
using Foundry.Deploy.Models;
using Foundry.Deploy.Models.Configuration;
using Foundry.Deploy.Services.Deployment;
using Foundry.Deploy.Services.Hardware;
using Foundry.Deploy.Services.Logging;
using Foundry.Deploy.Services.Operations;
using Foundry.Telemetry;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using DeployUnattendFile = Foundry.Core.Models.Configuration.Deploy.DeployUnattendFile;

namespace Foundry.Deploy.Tests;

public sealed class DeploymentOrchestratorTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task RunAsync_UnresolvedNativeCleanupPersistsRecoveryAndBlocksRetries(bool cancelled, bool uncertainProcess)
    {
        using TempDeploymentWorkspace workspace = TempDeploymentWorkspace.Create();
        using var cancellation = new CancellationTokenSource();
        var diagnostic = uncertainProcess
            ? new RecoveryResourceDiagnostic(RecoveryResourceState.RecoveryRequired, "Native process", workspace.RootPath, null, "native_process_termination_unconfirmed")
            : new RecoveryResourceDiagnostic(RecoveryResourceState.RecoveryRequired,
                "RegistryHive", @"HKLM\FoundrySynthetic_owned", Path.Combine(workspace.RootPath, "SYSTEM"), "hive_cleanup_unresolved");
        var failureStep = new RecoveryFailingStep(DeploymentStepNames.ExecutionOrder[0], diagnostic, cancelled ? cancellation : null, uncertainProcess);
        IDeploymentStep[] steps = DeploymentStepNames.ExecutionOrder
            .Select(name => name == failureStep.Name ? (IDeploymentStep)failureStep : new SucceedingStep(name)).ToArray();
        var logs = new FakeDeploymentLogService();
        DeploymentOrchestrator CreateOrchestrator() => new(new FakeOperationProgressService(), logs,
            new FakeTargetDiskService(), steps, new RecordingTelemetryService(),
            NullLogger<DeploymentOrchestrator>.Instance, _ => workspace.RootPath);
        var request = new DeploymentContext
        {
            OperatingSystem = new(),
            IsDryRun = true,
            Mode = DeploymentMode.Iso,
            CacheRootPath = workspace.RootPath,
            TargetDiskNumber = 1,
            TargetComputerName = "SYNTHETIC",
            DriverPackSelectionKind = DriverPackSelectionKind.None
        };
        DeploymentOrchestrator orchestrator = CreateOrchestrator();

        DeploymentResult failed = await orchestrator.RunAsync(request, cancellation.Token);
        Assert.False(failed.IsSuccess);
        Assert.StartsWith(cancelled ? "Deployment cancelled." : "Synthetic primary failure.", failed.Message);
        Assert.Contains("Manual recovery is required", failed.Message);
        Assert.Contains(logs.RecoveryDiagnostics, item => item == diagnostic);
        Assert.Equal(diagnostic, new DeploymentRecoveryJournal(workspace.RootPath).Read());

        string evidence = Path.Combine(workspace.RootPath, "Logs", "Foundry.Dism.log");
        File.WriteAllText(evidence, "preserved evidence");
        foreach (DeploymentOrchestrator retry in new[] { orchestrator, CreateOrchestrator() })
        {
            DeploymentResult blocked = await retry.RunAsync(request, TestContext.Current.CancellationToken);
            Assert.False(blocked.IsSuccess);
            Assert.Contains("Manual recovery is required", blocked.Message);
        }
        Assert.Equal(1, failureStep.ExecutionCount);
        Assert.Equal("preserved evidence", File.ReadAllText(evidence));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task RunAsync_OptionalCaptureFailurePreservesOutcome(bool succeeds)
    {
        using TempDeploymentWorkspace workspace = TempDeploymentWorkspace.Create();
        var progress = new FakeOperationProgressService();
        var telemetry = new RecordingTelemetryService { FailCapture = true };
        var orchestrator = new DeploymentOrchestrator(progress, new FakeDeploymentLogService(),
            new FakeTargetDiskService(), CreateSteps(Path.Combine(workspace.RootPath, "TargetWindows"))
                .Select(step => succeeds && step is FailingStep ? new SucceedingStep(step.Name) : step),
            telemetry, NullLogger<DeploymentOrchestrator>.Instance);
        DeploymentResult result = await orchestrator.RunAsync(new DeploymentContext
        {
            Mode = DeploymentMode.Iso,
            CacheRootPath = workspace.RootPath,
            TargetDiskNumber = 1,
            TargetComputerName = "SYNTHETIC",
            OperatingSystem = new(),
            DriverPackSelectionKind = DriverPackSelectionKind.None
        }, TestContext.Current.CancellationToken);
        Assert.Equal(succeeds, result.IsSuccess);
        Assert.DoesNotContain("capture failure", result.Message);
        Assert.Single(telemetry.Events);
    }

    [Theory]
    [InlineData(false, "native")]
    [InlineData(true, "custom")]
    public async Task RunAsync_ReportsActualUnattendModeWithoutNativeOobeUsageOrFileMetadata(bool useCustom, string expectedMode)
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
            CacheRootPath = workspace.RootPath,
            TargetDiskNumber = 1,
            TargetComputerName = "private-file-computer",
            OperatingSystem = new OperatingSystemCatalogItem(),
            DriverPackSelectionKind = DriverPackSelectionKind.None,
            Unattend = useCustom ? new UnattendSelection(new DeployUnattendFile
            {
                Id = "private-file-id",
                DisplayName = "private-file-label",
                ContentHash = "private-file-hash"
            }, @"C:\private-file-asset.xml") : null,
            Oobe = new DeployOobeSettings
            {
                IsEnabled = true,
                EnableAdministratorAccount = true,
                AdditionalAccounts = [new DeployOobeAdditionalAccountSettings { UserName = "private-file-user" }]
            }
        }, TestContext.Current.CancellationToken);

        TelemetryEvent telemetryEvent = Assert.Single(telemetryService.Events);
        Assert.Equal(TelemetryEvents.DeploySessionFinished, telemetryEvent.Name);
        Assert.Equal(expectedMode, telemetryEvent.Properties["deploy_unattend_mode"]);
        Assert.Equal(!useCustom, telemetryEvent.Properties["deploy_oobe_enabled"]);
        Assert.Equal(!useCustom, telemetryEvent.Properties["deploy_oobe_administrator_enabled"]);
        Assert.Equal(useCustom ? 0 : 1, telemetryEvent.Properties["deploy_oobe_additional_account_count"]);
        Assert.Equal(!useCustom, telemetryEvent.Properties["deploy_oobe_account_creation_skipped"]);
        Assert.DoesNotContain(telemetryEvent.Properties.Values, value => value?.ToString()?.Contains("private-file", StringComparison.Ordinal) == true);
        IReadOnlyDictionary<string, object?> sanitized = TelemetryEventPropertyPolicy.Sanitize(telemetryEvent.Name, telemetryEvent.Properties);
        Assert.Equal(expectedMode, sanitized["deploy_unattend_mode"]);
    }

    [Fact]
    public void Constructor_WhenStepsAreRegisteredOutOfOrder_UsesCanonicalExecutionOrder()
    {
        IDeploymentStep[] registeredSteps = DeploymentStepNames.ExecutionOrder
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

        Assert.Equal(DeploymentStepNames.ExecutionOrder, orchestrator.PlannedSteps);
    }

    [Fact]
    public void ApplicationServices_RegisterEveryCanonicalStepExactlyOnce()
    {
        IServiceCollection services = new ServiceCollection().AddFoundryDeployApplicationServices(offlineOnly: true);
        ServiceDescriptor[] registrations = services
            .Where(descriptor => descriptor.ServiceType == typeof(IDeploymentStep))
            .ToArray();
        Dictionary<string, string> identifiers = typeof(DeploymentStepNames)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field.IsLiteral && field.FieldType == typeof(string))
            .ToDictionary(field => (string)field.GetRawConstantValue()!, field => field.Name, StringComparer.Ordinal);

        Assert.Equal(DeploymentStepNames.ExecutionOrder.Count, registrations.Length);
        foreach (string stepName in DeploymentStepNames.ExecutionOrder)
        {
            string implementationName = $"Foundry.Deploy.Services.Deployment.Steps.{identifiers[stepName]}Step";
            Type? implementation = typeof(DeploymentOrchestrator).Assembly.GetType(implementationName);
            Assert.NotNull(implementation);
            Assert.True(typeof(IDeploymentStep).IsAssignableFrom(implementation));
            ServiceDescriptor registration = Assert.Single(registrations,
                descriptor => descriptor.ImplementationType == implementation);
            Assert.Equal(ServiceLifetime.Singleton, registration.Lifetime);
        }
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
        }, TestContext.Current.CancellationToken);

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
            },
            Oobe = new DeployOobeSettings
            {
                IsEnabled = true,
                EnableAdministratorAccount = true,
                AdditionalAccounts =
                [
                    new DeployOobeAdditionalAccountSettings
                    {
                        Id = "account-1",
                        UserName = "PrivateTelemetryUser"
                    }
                ]
            }
        }, TestContext.Current.CancellationToken);

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
        Assert.True((bool)telemetryEvent.Properties["deploy_oobe_enabled"]!);
        Assert.True((bool)telemetryEvent.Properties["deploy_oobe_administrator_enabled"]!);
        Assert.Equal(1, telemetryEvent.Properties["deploy_oobe_additional_account_count"]);
        Assert.True((bool)telemetryEvent.Properties["deploy_oobe_account_creation_skipped"]!);
        Assert.DoesNotContain(
            telemetryEvent.Properties.Values,
            value => value?.ToString()?.Contains("PrivateTelemetryUser", StringComparison.Ordinal) == true);
        Assert.False(telemetryEvent.Properties.ContainsKey("success"));
        Assert.False(telemetryEvent.Properties.ContainsKey("autopilot_enabled"));
    }

    [Fact]
    public async Task RunAsync_WhenStepFails_PersistsCurrentOperationAndTerminalFailure()
    {
        using TempDeploymentWorkspace workspace = TempDeploymentWorkspace.Create();
        var logService = new FakeDeploymentLogService();
        var telemetryService = new RecordingTelemetryService();
        var logger = new RecordingLogger<DeploymentOrchestrator>();
        var orchestrator = new DeploymentOrchestrator(
            new FakeOperationProgressService(),
            logService,
            new FakeTargetDiskService(),
            DeploymentStepNames.ExecutionOrder.Select(name => (IDeploymentStep)(name == DeploymentStepNames.ValidateTargetConfiguration
                ? new OperationFailingStep(name)
                : new SucceedingStep(name))),
            telemetryService,
            logger);

        DeploymentResult result = await orchestrator.RunAsync(new DeploymentContext
        {
            Mode = DeploymentMode.Iso,
            IsDryRun = true,
            CacheRootPath = workspace.RootPath,
            TargetDiskNumber = 1,
            TargetComputerName = "LAB01",
            DriverPackSelectionKind = DriverPackSelectionKind.None,
            OperatingSystem = new OperatingSystemCatalogItem()
        }, TestContext.Current.CancellationToken);

        Assert.False(result.IsSuccess);
        Assert.Contains(
            logService.SavedStates,
            state => state.CurrentOperation == DeploymentOperationNames.ValidateTargetDisk && state.LastFailureCode is null);
        DeploymentStateSnapshot terminal = logService.SavedStates.Last(state => state.LastFailureCode == "missing_target_partition");
        Assert.Equal(DeploymentOperationNames.ValidateTargetDisk, terminal.CurrentOperation);
        Assert.Equal(DeploymentStepNames.ValidateTargetConfiguration, terminal.LastFailureStep);
        Assert.Equal(DeploymentFailureKinds.Validation, terminal.LastFailureKind);
        Assert.Equal(DeploymentFailureReasons.MissingResource, terminal.LastFailureReason);

        TelemetryEvent telemetryEvent = Assert.Single(telemetryService.Events);
        string operationId = Assert.IsType<string>(telemetryEvent.Properties["operation_id"]);
        Assert.False(string.IsNullOrWhiteSpace(operationId));
        LogEntry terminalLog = Assert.Single(logger.Entries, entry => entry.Properties.ContainsKey("Outcome"));
        Assert.Equal(operationId, terminalLog.Properties["OperationId"]);
        Assert.Equal("failed", terminalLog.Properties["Outcome"]);
        Assert.Equal(DeploymentOperationNames.ValidateTargetDisk, terminalLog.Properties["FailedOperationName"]);
        Assert.Equal("missing_target_partition", terminalLog.Properties["FailureCode"]);
        Assert.Equal(true, terminalLog.Properties["RemoteDiagnostic"]);
    }

    [Fact]
    public async Task RunAsync_WhenStatePersistenceFails_PreservesDeploymentFailure()
    {
        using TempDeploymentWorkspace workspace = TempDeploymentWorkspace.Create();
        var logService = new FakeDeploymentLogService { ThrowOnSave = true };
        var telemetryService = new RecordingTelemetryService();
        var orchestrator = new DeploymentOrchestrator(
            new FakeOperationProgressService(),
            logService,
            new FakeTargetDiskService(),
            DeploymentStepNames.ExecutionOrder.Select(name => (IDeploymentStep)(name == DeploymentStepNames.ValidateTargetConfiguration
                ? new OperationFailingStep(name)
                : new SucceedingStep(name))),
            telemetryService,
            NullLogger<DeploymentOrchestrator>.Instance);

        DeploymentResult result = await orchestrator.RunAsync(new DeploymentContext
        {
            Mode = DeploymentMode.Iso,
            IsDryRun = true,
            CacheRootPath = workspace.RootPath,
            TargetDiskNumber = 1,
            TargetComputerName = "LAB01",
            DriverPackSelectionKind = DriverPackSelectionKind.None,
            OperatingSystem = new OperatingSystemCatalogItem()
        }, TestContext.Current.CancellationToken);

        Assert.False(result.IsSuccess);
        Assert.Equal("Target Windows partition is unavailable.", result.Message);
        TelemetryEvent telemetryEvent = Assert.Single(telemetryService.Events);
        Assert.Equal("missing_target_partition", telemetryEvent.Properties["deploy_session_failure_code"]);
    }

    [Fact]
    public async Task RunAsync_WhenStepThrowsTaskCanceledWithoutCallerCancellation_TracksFailedTimeout()
    {
        using TempDeploymentWorkspace workspace = TempDeploymentWorkspace.Create();
        var telemetryService = new RecordingTelemetryService();
        var logger = new RecordingLogger<DeploymentOrchestrator>();
        var orchestrator = new DeploymentOrchestrator(
            new FakeOperationProgressService(),
            new FakeDeploymentLogService(),
            new FakeTargetDiskService(),
            DeploymentStepNames.ExecutionOrder.Select(name => (IDeploymentStep)(name == DeploymentStepNames.DownloadOperatingSystemImage
                ? new TimeoutFailingStep(name)
                : new SucceedingStep(name))),
            telemetryService,
            logger);

        DeploymentResult result = await orchestrator.RunAsync(new DeploymentContext
        {
            Mode = DeploymentMode.Iso,
            IsDryRun = false,
            CacheRootPath = workspace.RootPath,
            TargetDiskNumber = 1,
            TargetComputerName = "LAB01",
            DriverPackSelectionKind = DriverPackSelectionKind.None,
            OperatingSystem = new OperatingSystemCatalogItem()
        }, TestContext.Current.CancellationToken);

        Assert.False(result.IsSuccess);
        TelemetryEvent telemetryEvent = Assert.Single(telemetryService.Events);
        Assert.False((bool)telemetryEvent.Properties["deploy_session_cancelled"]!);
        Assert.Equal("timeout", telemetryEvent.Properties["deploy_session_failure_kind"]);
        Assert.Equal("deadline_exceeded", telemetryEvent.Properties["deploy_session_failure_reason"]);
        Assert.Equal("os_image.download", telemetryEvent.Properties["deploy_session_failed_operation_name"]);
        LogEntry terminalLog = Assert.Single(logger.Entries, entry => entry.Properties.ContainsKey("Outcome"));
        Assert.Equal("failed", terminalLog.Properties["Outcome"]);
        Assert.Equal(false, terminalLog.Properties["Cancelled"]);
        Assert.Equal("timeout", terminalLog.Properties["FailureKind"]);
    }

    [Fact]
    public async Task RunAsync_WhenCallerCancellationIsRequested_TracksCancelledOutcomeAndPersistsFinalState()
    {
        using TempDeploymentWorkspace workspace = TempDeploymentWorkspace.Create();
        var logService = new FakeDeploymentLogService();
        var telemetryService = new RecordingTelemetryService();
        var logger = new RecordingLogger<DeploymentOrchestrator>();
        using var cancellation = new CancellationTokenSource();
        var orchestrator = new DeploymentOrchestrator(
            new FakeOperationProgressService(),
            logService,
            new FakeTargetDiskService(),
            DeploymentStepNames.ExecutionOrder.Select(name => (IDeploymentStep)(name == DeploymentStepNames.DownloadOperatingSystemImage
                ? new CallerCancellingStep(name, cancellation)
                : new SucceedingStep(name))),
            telemetryService,
            logger);

        DeploymentResult result = await orchestrator.RunAsync(new DeploymentContext
        {
            Mode = DeploymentMode.Iso,
            IsDryRun = false,
            CacheRootPath = workspace.RootPath,
            TargetDiskNumber = 1,
            TargetComputerName = "LAB01",
            DriverPackSelectionKind = DriverPackSelectionKind.None,
            OperatingSystem = new OperatingSystemCatalogItem()
        }, cancellation.Token);

        Assert.False(result.IsSuccess);
        Assert.Contains(logService.SavedStates, state => state.CurrentOperation == "os_image.download");
        DeploymentStateSnapshot terminal = logService.SavedStates.Last();
        Assert.Equal("os_image.download", terminal.CurrentOperation);
        Assert.Equal(DeploymentStepNames.DownloadOperatingSystemImage, terminal.LastFailureStep);
        Assert.Equal(DeploymentFailureKinds.Cancelled, terminal.LastFailureKind);
        Assert.Equal(DeploymentFailureReasons.CallerCancelled, terminal.LastFailureReason);
        Assert.Null(terminal.LastFailureCode);
        TelemetryEvent telemetryEvent = Assert.Single(telemetryService.Events);
        Assert.True((bool)telemetryEvent.Properties["deploy_session_cancelled"]!);
        Assert.False((bool)telemetryEvent.Properties["deploy_session_success"]!);
        Assert.False(telemetryEvent.Properties.ContainsKey("deploy_session_failure_kind"));
        LogEntry terminalLog = Assert.Single(logger.Entries, entry => entry.Properties.ContainsKey("Outcome"));
        Assert.Equal("cancelled", terminalLog.Properties["Outcome"]);
        Assert.Equal(true, terminalLog.Properties["Cancelled"]);
    }

    [Theory]
    [InlineData(1, DeploymentStepNames.GatherDeploymentVariables, DeploymentOperationNames.GatherVariables)]
    [InlineData(3, DeploymentStepNames.GatherDeploymentVariables, DeploymentOperationNames.DownloadOperatingSystemImage)]
    public async Task RunAsync_WhenCancellationOccursDuringRuntimeStatePersistence_RecoversFinalPersistence(
        int cancelledSaveCallNumber,
        string expectedFailureStep,
        string expectedCurrentOperation)
    {
        using TempDeploymentWorkspace workspace = TempDeploymentWorkspace.Create();
        using var cancellation = new CancellationTokenSource();
        var logService = new CancellationDuringSaveDeploymentLogService(cancellation, cancelledSaveCallNumber);
        var telemetryService = new RecordingTelemetryService();
        var logger = new RecordingLogger<DeploymentOrchestrator>();
        var orchestrator = new DeploymentOrchestrator(
            new FakeOperationProgressService(),
            logService,
            new FakeTargetDiskService(),
            DeploymentStepNames.ExecutionOrder.Select(name => (IDeploymentStep)new PersistenceTrackingStep(name)),
            telemetryService,
            logger);

        DeploymentResult result = await orchestrator.RunAsync(new DeploymentContext
        {
            Mode = DeploymentMode.Iso,
            IsDryRun = false,
            CacheRootPath = workspace.RootPath,
            TargetDiskNumber = 1,
            TargetComputerName = "LAB01",
            DriverPackSelectionKind = DriverPackSelectionKind.None,
            OperatingSystem = new OperatingSystemCatalogItem()
        }, cancellation.Token);

        Assert.False(result.IsSuccess);
        TelemetryEvent telemetryEvent = Assert.Single(telemetryService.Events);
        Assert.True((bool)telemetryEvent.Properties["deploy_session_cancelled"]!);
        Assert.False((bool)telemetryEvent.Properties["deploy_session_success"]!);
        LogEntry terminalLog = Assert.Single(logger.Entries, entry => entry.Properties.ContainsKey("Outcome"));
        Assert.Equal("cancelled", terminalLog.Properties["Outcome"]);
        Assert.Equal(true, terminalLog.Properties["Cancelled"]);
        Assert.True(logService.SuccessfulBestEffortSaveCount >= 2);
        DeploymentStateSnapshot terminal = logService.SavedStates.Last();
        Assert.Equal(expectedCurrentOperation, terminal.CurrentOperation);
        Assert.Equal(expectedFailureStep, terminal.LastFailureStep);
        Assert.Equal(DeploymentFailureKinds.Cancelled, terminal.LastFailureKind);
        Assert.Equal(DeploymentFailureReasons.CallerCancelled, terminal.LastFailureReason);
        Assert.Null(terminal.LastFailureCode);
        Assert.Equal(
            [logService.SaveCallCount - 1, logService.SaveCallCount],
            logService.BestEffortSaveCallNumbers.Skip(Math.Max(0, logService.BestEffortSaveCallNumbers.Count - 2)).ToArray());
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
        }, TestContext.Current.CancellationToken);

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

    private sealed class RecoveryFailingStep(string name, RecoveryResourceDiagnostic diagnostic,
        CancellationTokenSource? cancellation, bool uncertainProcess) : IDeploymentStep
    {
        public string Name { get; } = name;
        public int ExecutionCount { get; private set; }

        public Task<DeploymentStepResult> ExecuteAsync(DeploymentStepExecutionContext context, CancellationToken cancellationToken)
        {
            ExecutionCount++;
            cancellation?.Cancel();
            Exception error = cancellation is null ? new IOException("Synthetic primary failure.") : new OperationCanceledException(cancellationToken);
            if (uncertainProcess) error.Data["ProcessTreeTerminationConfirmed"] = false;
            else error.Data["FoundryRecoveryDiagnostic"] = diagnostic;
            error.Data["FoundryCleanupFailure"] = new IOException("Synthetic cleanup failure.");
            return Task.FromException<DeploymentStepResult>(error);
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

    private sealed class OperationFailingStep(string name) : IDeploymentStep
    {
        public string Name { get; } = name;

        public Task<DeploymentStepResult> ExecuteAsync(
            DeploymentStepExecutionContext context,
            CancellationToken cancellationToken)
        {
            context.SetCurrentOperation(DeploymentOperationNames.ValidateTargetDisk);
            return Task.FromResult(DeploymentStepResult.Failed(
                "Target Windows partition is unavailable.",
                DeploymentFailure.Guard(
                    DeploymentOperationNames.ValidateTargetDisk,
                    DeploymentFailureReasons.MissingResource,
                    "missing_target_partition")));
        }
    }

    private sealed class TimeoutFailingStep(string name) : IDeploymentStep
    {
        public string Name { get; } = name;

        public Task<DeploymentStepResult> ExecuteAsync(
            DeploymentStepExecutionContext context,
            CancellationToken cancellationToken)
        {
            context.SetCurrentOperation("os_image.download");
            return Task.FromException<DeploymentStepResult>(new TaskCanceledException("Simulated HTTP timeout."));
        }
    }

    private sealed class CallerCancellingStep(string name, CancellationTokenSource cancellation) : IDeploymentStep
    {
        public string Name { get; } = name;

        public Task<DeploymentStepResult> ExecuteAsync(
            DeploymentStepExecutionContext context,
            CancellationToken cancellationToken)
        {
            context.SetCurrentOperation("os_image.download");
            cancellation.Cancel();
            return Task.FromCanceled<DeploymentStepResult>(cancellationToken);
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

    private sealed class PersistenceTrackingStep(string name) : IDeploymentStep
    {
        public string Name { get; } = name;

        public Task<DeploymentStepResult> ExecuteAsync(
            DeploymentStepExecutionContext context,
            CancellationToken cancellationToken)
        {
            context.SetCurrentOperation("os_image.download");
            return Task.FromResult(DeploymentStepResult.Succeeded("ok"));
        }
    }

    private sealed class FakeDeploymentLogService : IDeploymentLogService
    {
        public List<DeploymentStateSnapshot> SavedStates { get; } = [];
        public List<RecoveryResourceDiagnostic> RecoveryDiagnostics { get; } = [];

        public bool ThrowOnSave { get; init; }

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
            string logFilePath = Path.Combine(session.LogsDirectoryPath, FoundryDeployLogging.LogFileName);
            await File.AppendAllTextAsync(logFilePath, $"{level}: {message}{Environment.NewLine}", cancellationToken);
        }

        public async Task SaveStateAsync<TState>(
            DeploymentLogSession session,
            TState state,
            CancellationToken cancellationToken = default)
        {
            if (ThrowOnSave)
            {
                throw new IOException("Synthetic state persistence failure.");
            }

            if (state is DeploymentRuntimeState runtimeState)
            {
                if (runtimeState.RecoveryDiagnostic is { } recovery) RecoveryDiagnostics.Add(recovery);
                SavedStates.Add(new DeploymentStateSnapshot(
                    runtimeState.CurrentOperation,
                    runtimeState.LastFailureStep,
                    runtimeState.LastFailureKind,
                    runtimeState.LastFailureReason,
                    runtimeState.LastFailureCode));
            }

            Directory.CreateDirectory(session.StateDirectoryPath);
            await File.WriteAllTextAsync(session.StateFilePath, "{}", cancellationToken);
        }

    }

    private sealed class CancellationDuringSaveDeploymentLogService(
        CancellationTokenSource cancellationTokenSource,
        int cancelledSaveCallNumber) : IDeploymentLogService
    {
        public int SaveCallCount { get; private set; }

        public int SuccessfulBestEffortSaveCount { get; private set; }

        public List<int> BestEffortSaveCallNumbers { get; } = [];

        public List<DeploymentStateSnapshot> SavedStates { get; } = [];

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
                StateFilePath = Path.Combine(stateDirectory, "deployment-state.json")
            };
        }

        public Task AppendAsync(
            DeploymentLogSession session,
            DeploymentLogLevel level,
            string message,
            CancellationToken cancellationToken = default) => Task.CompletedTask;

        public async Task SaveStateAsync<TState>(
            DeploymentLogSession session,
            TState state,
            CancellationToken cancellationToken = default)
        {
            SaveCallCount++;
            if (SaveCallCount == cancelledSaveCallNumber)
            {
                cancellationTokenSource.Cancel();
            }

            cancellationToken.ThrowIfCancellationRequested();

            if (!cancellationToken.CanBeCanceled)
            {
                SuccessfulBestEffortSaveCount++;
                BestEffortSaveCallNumbers.Add(SaveCallCount);
            }

            if (state is DeploymentRuntimeState runtimeState)
            {
                SavedStates.Add(new DeploymentStateSnapshot(
                    runtimeState.CurrentOperation,
                    runtimeState.LastFailureStep,
                    runtimeState.LastFailureKind,
                    runtimeState.LastFailureReason,
                    runtimeState.LastFailureCode));
            }

            Directory.CreateDirectory(session.StateDirectoryPath);
            await File.WriteAllTextAsync(session.StateFilePath, "{}", cancellationToken);
        }
    }

    private sealed record DeploymentStateSnapshot(
        string CurrentOperation,
        string? LastFailureStep,
        string? LastFailureKind,
        string? LastFailureReason,
        string? LastFailureCode);

    private sealed record LogEntry(LogLevel Level, IReadOnlyDictionary<string, object?> Properties);

    private sealed class RecordingLogger<T> : ILogger<T>
    {
        public List<LogEntry> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            var properties = state as IEnumerable<KeyValuePair<string, object?>>;
            Entries.Add(new LogEntry(
                logLevel,
                properties?.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal)
                    ?? new Dictionary<string, object?>()));
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
        public void SetEnabled(bool enabled) { }
        public bool FailCapture { get; init; }
        public List<TelemetryEvent> Events { get; } = [];

        public Task TrackAsync(
            string eventName,
            IReadOnlyDictionary<string, object?> properties,
            CancellationToken cancellationToken = default)
        {
            Events.Add(new TelemetryEvent(eventName, new Dictionary<string, object?>(properties)));
            if (FailCapture) throw new IOException("Synthetic optional capture failure.");
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
