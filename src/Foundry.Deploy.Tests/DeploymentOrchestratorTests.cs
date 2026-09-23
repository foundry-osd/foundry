// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Deploy.Models;
using Foundry.Deploy.Models.Configuration;
using Foundry.Deploy.Services.Deployment;
using Foundry.Deploy.Services.Hardware;
using Foundry.Deploy.Services.Http;
using Foundry.Deploy.Services.Logging;
using Foundry.Deploy.Services.Operations;
using Foundry.Deploy.ViewModels;
using Foundry.Telemetry;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using DeployUnattendFile = Foundry.Core.Models.Configuration.Deploy.DeployUnattendFile;

namespace Foundry.Deploy.Tests;

public sealed class DeploymentOrchestratorTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task RunAsync_WhenPayloadDiscoveryRefinesPlan_PreservesExecutionAndTimeline(bool hasDrivers, bool hasFirmware)
    {
        using TempDeploymentWorkspace workspace = TempDeploymentWorkspace.Create();
        var request = CreateCancellationContext(workspace.RootPath) with
        {
            DriverPackSelectionKind = DriverPackSelectionKind.MicrosoftUpdateCatalog,
            ApplyFirmwareUpdates = true
        };
        var driverStep = new PayloadDiscoveryStep(DeploymentStepNames.DownloadDriverPack, hasDrivers);
        var firmwareStep = new PayloadDiscoveryStep(DeploymentStepNames.DownloadFirmwareUpdate, hasFirmware);
        var orchestrator = CreateOrchestrator(DeploymentStepNames.ExecutionOrder.Select(name =>
            name == driverStep.Name ? (IDeploymentStep)driverStep :
            name == firmwareStep.Name ? firmwareStep : new SucceedingStep(name)));
        var tracker = new DeploymentTimelineTracker(name => name, state => state.ToString());
        tracker.Reconcile(DeploymentPlan.Build(request));
        var updates = new List<DeploymentStepProgress>();
        orchestrator.StepProgressChanged += (_, update) =>
        {
            updates.Add(update);
            tracker.Apply(update);
        };

        DeploymentResult result = await orchestrator.RunAsync(request, TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        DeploymentRuntimeState state = Assert.IsType<DeploymentRuntimeState>(driverStep.RuntimeState);
        string[] executed = state.StepOutcomes.Select(outcome => outcome.Name).ToArray();
        Assert.Equal(executed.Length, executed.Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(executed, tracker.Entries.Select(entry => entry.RawName));
        Assert.Equal(DeploymentStepNames.FinalizeDeploymentAndWriteLogs, executed[^1]);
        Assert.Equal(hasDrivers, executed.Contains(DeploymentStepNames.ExtractDriverPack));
        Assert.Equal(hasDrivers, executed.Contains(DeploymentStepNames.ApplyDriverPack));
        Assert.Equal(hasDrivers, executed.Contains(DeploymentStepNames.ApplyRecoveryDrivers));
        Assert.Equal(hasFirmware, executed.Contains(DeploymentStepNames.ExtractFirmwareUpdate));
        Assert.Equal(hasFirmware, executed.Contains(DeploymentStepNames.ApplyFirmwareUpdate));
        Assert.All(tracker.Entries, entry => Assert.Equal(
            entry.RawName == driverStep.Name || entry.RawName == firmwareStep.Name
                ? DeploymentStepState.Skipped : DeploymentStepState.Succeeded, entry.State));
        Assert.DoesNotContain(driverStep.Name, state.CompletedSteps);
        Assert.DoesNotContain(firmwareStep.Name, state.CompletedSteps);
        Assert.Equal(hasDrivers ? "Reused verified payload." : "No matching payload.",
            tracker.Entries.Single(entry => entry.RawName == driverStep.Name).DetailText);
        Assert.Equal(hasFirmware ? "Reused verified payload." : "No matching payload.",
            tracker.Entries.Single(entry => entry.RawName == firmwareStep.Name).DetailText);
        Assert.All(updates, update =>
        {
            Assert.NotNull(update.Plan);
            Assert.Equal(update.Plan.Count, update.StepCount);
            Assert.Equal(update.StepName, update.Plan[update.StepIndex - 1].Name);
        });
        Assert.True(updates.Zip(updates.Skip(1)).All(pair => pair.First.ProgressPercent <= pair.Second.ProgressPercent));
    }

    private sealed class PayloadDiscoveryStep(string name, bool hasPayload) : IDeploymentStep
    {
        public string Name { get; } = name;
        public DeploymentRuntimeState? RuntimeState { get; private set; }

        public Task<DeploymentStepResult> ExecuteAsync(DeploymentStepExecutionContext context, CancellationToken cancellationToken)
        {
            RuntimeState = context.RuntimeState;
            if (Name == DeploymentStepNames.DownloadDriverPack)
                RuntimeState.MicrosoftUpdateCatalogDriverPaths = hasPayload ? ["cached-driver.cab"] : [];
            else
                RuntimeState.DownloadedFirmwarePath = hasPayload ? "cached-firmware.cab" : null;
            return Task.FromResult(DeploymentStepResult.Skipped(hasPayload ? "Reused verified payload." : "No matching payload."));
        }
    }

    [Fact]
    public async Task RunAsync_WhenCompletionTrackingIsPending_ClosesCancellationAndKeepsOperationBusy()
    {
        using TempDeploymentWorkspace workspace = TempDeploymentWorkspace.Create();
        var trackingStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseTracking = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var telemetry = new RecordingTelemetryService
        {
            BeforeTrack = async () =>
            {
                trackingStarted.SetResult();
                await releaseTracking.Task.WaitAsync(TestContext.Current.CancellationToken);
            }
        };
        var progress = new FakeOperationProgressService();
        var orchestrator = new DeploymentOrchestrator(progress, new FakeDeploymentLogService(), new FakeTargetDiskService(),
            DeploymentStepNames.ExecutionOrder.Select(name => new SucceedingStep(name)), telemetry, NullLogger<DeploymentOrchestrator>.Instance);
        bool cancellationClosed = false;
        orchestrator.CompletionStarting += (_, _) => cancellationClosed = true;

        Task<DeploymentResult> run = orchestrator.RunAsync(CreateCancellationContext(workspace.RootPath), TestContext.Current.CancellationToken);
        await trackingStarted.Task.WaitAsync(TestContext.Current.CancellationToken);
        bool wasCancellationClosed = cancellationClosed;
        bool wasBusy = progress.IsOperationInProgress && !run.IsCompleted;
        releaseTracking.SetResult();
        DeploymentResult result = await run;

        Assert.True(wasCancellationClosed);
        Assert.True(wasBusy);
        Assert.True(result.IsSuccess);
        Assert.False(progress.IsOperationInProgress);
    }

    [Fact]
    public async Task RunAsync_WhenCancellationWasAcceptedBeforeCompletionCloses_ReportsOnlyCancellation()
    {
        using TempDeploymentWorkspace workspace = TempDeploymentWorkspace.Create();
        using var cancellation = new CancellationTokenSource();
        var telemetry = new RecordingTelemetryService();
        var logger = new RecordingLogger<DeploymentOrchestrator>();
        var orchestrator = new DeploymentOrchestrator(new FakeOperationProgressService(), new FakeDeploymentLogService(), new FakeTargetDiskService(),
            DeploymentStepNames.ExecutionOrder.Select(name => new SucceedingStep(name)), telemetry, logger);
        int completionNotifications = 0;
        orchestrator.CompletionStarting += (_, _) =>
        {
            completionNotifications++;
            cancellation.Cancel();
        };

        DeploymentResult result = await orchestrator.RunAsync(CreateCancellationContext(workspace.RootPath), cancellation.Token);

        Assert.True(result.IsCancelled);
        Assert.False(result.IsSuccess);
        Assert.Equal(1, completionNotifications);
        Assert.True((bool)Assert.Single(telemetry.Events).Properties["deploy_session_cancelled"]!);
        Assert.Equal("cancelled", Assert.Single(logger.Entries, entry => entry.Properties.ContainsKey("Outcome")).Properties["Outcome"]);
    }

    [Fact]
    public async Task RunAsync_WhenCancellationNeedsTerminalPersistence_KeepsOperationGateUntilCleanupFinishes()
    {
        using TempDeploymentWorkspace workspace = TempDeploymentWorkspace.Create();
        using var cancellation = new CancellationTokenSource();
        var cleanupStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseCleanup = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var logService = new FakeDeploymentLogService
        {
            BeforeSave = async token =>
            {
                if (cancellation.IsCancellationRequested && !token.CanBeCanceled)
                {
                    cleanupStarted.TrySetResult();
                    await releaseCleanup.Task.WaitAsync(TestContext.Current.CancellationToken);
                }
            }
        };
        var progress = new FakeOperationProgressService();
        var orchestrator = new DeploymentOrchestrator(progress, logService, new FakeTargetDiskService(),
            DeploymentStepNames.ExecutionOrder.Select(name => name == DeploymentStepNames.DownloadOperatingSystemImage
                ? (IDeploymentStep)new CallerCancellingStep(name, cancellation) : new SucceedingStep(name)),
            new RecordingTelemetryService(), NullLogger<DeploymentOrchestrator>.Instance);

        Task<DeploymentResult> run = orchestrator.RunAsync(CreateCancellationContext(workspace.RootPath), cancellation.Token);
        await cleanupStarted.Task.WaitAsync(TestContext.Current.CancellationToken);
        bool wasBusy = progress.IsOperationInProgress;
        bool wasPending = !run.IsCompleted;
        releaseCleanup.SetResult();
        DeploymentResult result = await run;

        Assert.True(wasBusy);
        Assert.True(wasPending);
        Assert.True(result.IsCancelled);
        Assert.False(progress.IsOperationInProgress);
    }

    [Theory]
    [InlineData(DeploymentStepNames.PrepareTargetDiskLayout)]
    [InlineData(DeploymentStepNames.ApplyFirmwareUpdate)]
    [InlineData(DeploymentStepNames.ApplyRecoveryDrivers)]
    [InlineData(DeploymentStepNames.FinalizeDeploymentAndWriteLogs)]
    public async Task RunAsync_WhenLiveMutationIsCancelled_WaitsForSafeCompletionAndStartsNoFollowingStep(string heldStepName)
    {
        using TempDeploymentWorkspace workspace = TempDeploymentWorkspace.Create();
        using var cancellation = new CancellationTokenSource();
        var heldStep = new HeldStep(heldStepName, Path.Combine(workspace.RootPath, "TargetWindows"));
        var steps = DeploymentStepNames.ExecutionOrder.Select(name => name == heldStepName ? (IDeploymentStep)heldStep : new SucceedingStep(name)).ToArray();
        var progress = new FakeOperationProgressService();
        var logService = new FakeDeploymentLogService();
        var telemetry = new RecordingTelemetryService();
        var logger = new RecordingLogger<DeploymentOrchestrator>();
        var orchestrator = new DeploymentOrchestrator(progress, logService, new FakeTargetDiskService(), steps, telemetry, logger);
        var startedSteps = new List<string>();
        orchestrator.StepProgressChanged += (_, update) =>
        {
            if (update.State == DeploymentStepState.Running)
            {
                startedSteps.Add(update.StepName);
            }
        };

        Task<DeploymentResult> run = orchestrator.RunAsync(CreateMutationContext(workspace.RootPath), cancellation.Token);
        await heldStep.Started.Task.WaitAsync(TestContext.Current.CancellationToken);
        cancellation.Cancel();

        bool wasBusy = progress.IsOperationInProgress;
        bool wasPending = !run.IsCompleted;
        bool stepWasCancellable = heldStep.ReceivedToken.CanBeCanceled;
        heldStep.Release.SetResult();
        DeploymentResult result = await run;

        Assert.True(wasBusy);
        Assert.True(wasPending);
        Assert.False(stepWasCancellable);
        Assert.False(progress.IsOperationInProgress);
        Assert.False(result.IsSuccess);
        Assert.True(result.IsCancelled);
        Assert.Equal(heldStepName, startedSteps.Last());
        Assert.Contains(heldStepName, heldStep.RuntimeState!.CompletedSteps);
        Assert.Equal(DeploymentFailureKinds.Cancelled, logService.SavedStates.Last().LastFailureKind);
        Assert.Equal(Path.Combine(heldStep.TargetWindowsRoot, "Windows", "Temp", "Foundry", "Logs"), result.LogsDirectoryPath);
        Assert.True((bool)Assert.Single(telemetry.Events).Properties["deploy_session_cancelled"]!);
        Assert.Equal("cancelled", Assert.Single(logger.Entries, entry => entry.Properties.ContainsKey("Outcome")).Properties["Outcome"]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RunAsync_WhenMutationFailsAfterCancellation_PreservesRealFailure(bool throwFailure)
    {
        using TempDeploymentWorkspace workspace = TempDeploymentWorkspace.Create();
        using var cancellation = new CancellationTokenSource();
        var heldStep = new HeldStep(DeploymentStepNames.ApplyRecoveryDrivers, Path.Combine(workspace.RootPath, "TargetWindows"))
        {
            Fail = true,
            ThrowFailure = throwFailure
        };
        var orchestrator = CreateOrchestrator(DeploymentStepNames.ExecutionOrder.Select(name =>
            name == heldStep.Name ? (IDeploymentStep)heldStep : new SucceedingStep(name)));

        Task<DeploymentResult> run = orchestrator.RunAsync(CreateMutationContext(workspace.RootPath), cancellation.Token);
        await heldStep.Started.Task.WaitAsync(TestContext.Current.CancellationToken);
        cancellation.Cancel();
        heldStep.Release.SetResult();
        DeploymentResult result = await run;

        Assert.False(result.IsSuccess);
        Assert.False(result.IsCancelled);
        Assert.Equal("Servicing failed.", result.Message);
    }

    [Fact]
    public async Task RunAsync_WhenAllStepsSucceed_ReportsOneSuccessfulOutcome()
    {
        using TempDeploymentWorkspace workspace = TempDeploymentWorkspace.Create();
        var telemetry = new RecordingTelemetryService();
        var progress = new FakeOperationProgressService();
        var orchestrator = new DeploymentOrchestrator(progress, new FakeDeploymentLogService(), new FakeTargetDiskService(),
            DeploymentStepNames.ExecutionOrder.Select(name => new SucceedingStep(name)), telemetry, NullLogger<DeploymentOrchestrator>.Instance);

        DeploymentResult result = await orchestrator.RunAsync(CreateCancellationContext(workspace.RootPath), TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.False(result.IsCancelled);
        Assert.False(progress.IsOperationInProgress);
        Assert.True((bool)Assert.Single(telemetry.Events).Properties["deploy_session_success"]!);
    }

    private static DeploymentContext CreateCancellationContext(string rootPath) => new()
    {
        Mode = DeploymentMode.Iso,
        CacheRootPath = rootPath,
        TargetDiskNumber = 1,
        TargetComputerName = "LAB01",
        DriverPackSelectionKind = DriverPackSelectionKind.None,
        OperatingSystem = new OperatingSystemCatalogItem()
    };

    private static DeploymentContext CreateMutationContext(string rootPath) => CreateCancellationContext(rootPath) with
    {
        DriverPackSelectionKind = DriverPackSelectionKind.OemCatalog,
        DriverPack = new DriverPackCatalogItem { Manufacturer = "Dell", FileName = "drivers.cab" }
    };

    private sealed class HeldStep(string name, string targetWindowsRoot) : IDeploymentStep
    {
        public string Name { get; } = name;
        public string TargetWindowsRoot { get; } = targetWindowsRoot;
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public CancellationToken ReceivedToken { get; private set; }
        public DeploymentRuntimeState? RuntimeState { get; private set; }
        public bool Fail { get; init; }
        public bool ThrowFailure { get; init; }

        public async Task<DeploymentStepResult> ExecuteAsync(DeploymentStepExecutionContext context, CancellationToken cancellationToken)
        {
            ReceivedToken = cancellationToken;
            RuntimeState = context.RuntimeState;
            Started.SetResult();
            await Release.Task.WaitAsync(TestContext.Current.CancellationToken);
            context.RuntimeState.TargetWindowsPartitionRoot = TargetWindowsRoot;
            if (ThrowFailure)
            {
                throw new IOException("Servicing failed.");
            }

            return Fail ? DeploymentStepResult.Failed("Servicing failed.") : DeploymentStepResult.Succeeded("Servicing complete.");
        }
    }

    [Fact]
    public async Task RunAsync_WhenTlsFails_ReturnsActionableMessage()
    {
        using TempDeploymentWorkspace workspace = TempDeploymentWorkspace.Create();
        var orchestrator = CreateOrchestrator(DeploymentStepNames.ExecutionOrder.Select(name =>
            name == DeploymentStepNames.DownloadOperatingSystemImage
                ? (IDeploymentStep)new TlsFailingStep()
                : new SucceedingStep(name)));

        DeploymentResult result = await orchestrator.RunAsync(new DeploymentContext
        {
            Mode = DeploymentMode.Iso,
            CacheRootPath = workspace.RootPath,
            TargetDiskNumber = 1,
            TargetComputerName = "LAB01",
            DriverPackSelectionKind = DriverPackSelectionKind.None,
            OperatingSystem = new OperatingSystemCatalogItem()
        }, TestContext.Current.CancellationToken);

        Assert.False(result.IsSuccess);
        Assert.Equal(HttpConnectionFailure.SecureConnectionMessage, result.Message);
    }

    private sealed class TlsFailingStep : IDeploymentStep
    {
        public string Name => DeploymentStepNames.DownloadOperatingSystemImage;

        public Task<DeploymentStepResult> ExecuteAsync(DeploymentStepExecutionContext context, CancellationToken cancellationToken)
        {
            return Task.FromException<DeploymentStepResult>(new HttpRequestException(HttpRequestError.SecureConnectionError, "TLS handshake failed"));
        }
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
    public async Task RunAsync_WhenStepsAreRegisteredOutOfOrder_ExecutesApplicablePlanOrder()
    {
        using TempDeploymentWorkspace workspace = TempDeploymentWorkspace.Create();
        var orchestrator = CreateOrchestrator(DeploymentStepNames.ExecutionOrder.Reverse().Select(name => new SucceedingStep(name)));
        var started = new List<string>();
        orchestrator.StepProgressChanged += (_, update) =>
        {
            if (update.State == DeploymentStepState.Running) started.Add(update.StepName);
        };

        DeploymentResult result = await orchestrator.RunAsync(
            CreateCancellationContext(workspace.RootPath) with { ApplyFirmwareUpdates = false }, TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal([
            DeploymentStepNames.ValidateTargetConfiguration,
            DeploymentStepNames.PrepareTargetDiskLayout,
            DeploymentStepNames.DownloadOperatingSystemImage,
            DeploymentStepNames.CheckWindowsImage,
            DeploymentStepNames.ApplyOperatingSystemImage,
            DeploymentStepNames.ConfigureWindowsBoot,
            DeploymentStepNames.ConfigureTargetComputerName,
            DeploymentStepNames.ConfigureRecoveryEnvironment,
            DeploymentStepNames.SealRecoveryPartition,
            DeploymentStepNames.FinalizeDeploymentAndWriteLogs
        ], started);
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
            state => state.CurrentOperation == DeploymentOperationNames.ValidateTarget && state.LastFailureCode is null);
        DeploymentStateSnapshot terminal = logService.SavedStates.Last(state => state.LastFailureCode == "missing_target_partition");
        Assert.Equal(DeploymentOperationNames.ValidateTargetDisk, terminal.CurrentOperation);
        Assert.Equal(DeploymentStepNames.ValidateTargetConfiguration, terminal.LastFailureStep);
        Assert.Equal(DeploymentFailureKinds.Validation, terminal.LastFailureKind);
        Assert.Equal(DeploymentFailureReasons.MissingResource, terminal.LastFailureReason);
        Assert.Contains(logService.Messages, message => message.Contains("[GATHER] Deployment variables snapshot:", StringComparison.Ordinal));

        TelemetryEvent telemetryEvent = Assert.Single(telemetryService.Events);
        string operationId = Assert.IsType<string>(telemetryEvent.Properties["operation_id"]);
        Assert.False(string.IsNullOrWhiteSpace(operationId));
        LogEntry terminalLog = Assert.Single(logger.Entries, entry => entry.Properties.ContainsKey("Outcome"));
        Assert.Equal(operationId, terminalLog.Properties["OperationId"]);
        Assert.Equal("failed", terminalLog.Properties["Outcome"]);
        Assert.Equal(DeploymentOperationNames.ValidateTargetDisk, terminalLog.Properties["FailedOperationName"]);
        Assert.Equal("missing_target_partition", terminalLog.Properties["FailureCode"]);
        Assert.Contains(logger.Entries, entry =>
            entry.Properties.ContainsKey("Mode") && !entry.Properties.ContainsKey("Outcome"));
        Assert.Contains(logger.Entries, entry =>
            entry.Properties.TryGetValue("StepName", out object? step) && Equals(step, DeploymentStepNames.ValidateTargetConfiguration));
        Assert.All(logger.Entries.Where(entry => entry.Properties.ContainsKey("OperationId")),
            entry => Assert.Equal(operationId, entry.Properties["OperationId"]));
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
    [InlineData(1, DeploymentStepNames.ValidateTargetConfiguration, DeploymentOperationNames.ValidateTarget)]
    [InlineData(3, DeploymentStepNames.ValidateTargetConfiguration, DeploymentOperationNames.DownloadOperatingSystemImage)]
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
            if (Name == DeploymentStepNames.DownloadFirmwareUpdate)
                context.RuntimeState.DownloadedFirmwarePath = "simulated-firmware";
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
        public List<string> Messages { get; } = [];

        public bool ThrowOnSave { get; init; }
        public Func<CancellationToken, Task>? BeforeSave { get; init; }

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
            Messages.Add(message);
            Directory.CreateDirectory(session.LogsDirectoryPath);
            string logFilePath = Path.Combine(session.LogsDirectoryPath, FoundryDeployLogging.LogFileName);
            await File.AppendAllTextAsync(logFilePath, $"{level}: {message}{Environment.NewLine}", cancellationToken);
        }

        public async Task SaveStateAsync<TState>(
            DeploymentLogSession session,
            TState state,
            CancellationToken cancellationToken = default)
        {
            if (BeforeSave is not null)
            {
                await BeforeSave(cancellationToken);
            }

            if (ThrowOnSave)
            {
                throw new IOException("Synthetic state persistence failure.");
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
        private readonly LoggerExternalScopeProvider _scopeProvider = new();
        public List<LogEntry> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => _scopeProvider.Push(state);

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            var properties = new Dictionary<string, object?>();
            _scopeProvider.ForEachScope((scope, values) =>
            {
                if (scope is IEnumerable<KeyValuePair<string, object?>> scopeProperties)
                {
                    foreach ((string key, object? value) in scopeProperties)
                    {
                        values[key] = value;
                    }
                }
            }, properties);
            if (state is IEnumerable<KeyValuePair<string, object?>> eventProperties)
            {
                foreach ((string key, object? value) in eventProperties)
                {
                    properties[key] = value;
                }
            }
            Entries.Add(new LogEntry(logLevel, properties));
        }
    }

    private sealed class FakeOperationProgressService : IOperationProgressService
    {
        public bool IsOperationInProgress { get; private set; }
        public int Progress => 0;
        public string? Status => null;
        public OperationKind? CurrentOperation => null;
        public bool CanStartOperation => !IsOperationInProgress;
        public event EventHandler? ProgressChanged;
        public bool TryStart(OperationKind kind, string initialStatus, int initialProgress = 0)
        {
            if (IsOperationInProgress)
            {
                return false;
            }

            IsOperationInProgress = true;
            return true;
        }
        public void Report(int progress, string? status = null) => ProgressChanged?.Invoke(this, EventArgs.Empty);
        public void Complete(string? status = null) => ResetToIdle();
        public void Fail(string status) => ResetToIdle();
        public void ResetToIdle()
        {
            IsOperationInProgress = false;
            ProgressChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private sealed class FakeTargetDiskService : ITargetDiskService
    {
        public Task<IReadOnlyList<TargetDiskInfo>> GetDisksAsync(CancellationToken cancellationToken = default, bool includeExcludedDisks = false)
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

        public Func<Task>? BeforeTrack { get; init; }

        public async Task TrackAsync(
            string eventName,
            IReadOnlyDictionary<string, object?> properties,
            CancellationToken cancellationToken = default)
        {
            if (BeforeTrack is not null)
            {
                await BeforeTrack();
            }
            Events.Add(new TelemetryEvent(eventName, new Dictionary<string, object?>(properties)));
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
