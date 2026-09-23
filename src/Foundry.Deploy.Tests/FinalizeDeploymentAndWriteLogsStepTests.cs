// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Deploy.Services.Deployment;
using Foundry.Deploy.Services.Deployment.Steps;
using Foundry.Deploy.Services.Logging;
using Foundry.Utilities.Diagnostics;
using Serilog;

namespace Foundry.Deploy.Tests;

[Collection(nameof(SerilogCollection))]
public sealed class FinalizeDeploymentAndWriteLogsStepTests
{
    [Theory]
    [InlineData("initialize")]
    [InlineData("copy")]
    [InlineData("state")]
    public async Task Finalize_WhenRequiredArtifactCannotBePublished_PreservesSourceAndPrimaryOutcome(string failure)
    {
        var stateFailure = failure == "state" ? new LockedDestinationStateService(1) : null;
        using Fixture fixture = new(stateFailure);
        DeploymentStorageLayout layout = DeploymentStorageLayout.FromPartitionRoot(fixture.Root);
        if (failure == "initialize")
        {
            Directory.CreateDirectory(Path.GetDirectoryName(layout.Root)!);
            File.WriteAllText(layout.Root, "blocked");
        }
        else if (failure == "copy")
        {
            Directory.CreateDirectory(Path.Combine(layout.LogsDeployment, "Startup", "evidence.json"));
        }

        DeploymentStepResult result = await new FinalizeDeploymentAndWriteLogsStep().ExecuteAsync(fixture.Context, TestContext.Current.CancellationToken);

        Assert.Equal(DeploymentStepState.Succeeded, result.State);
        Assert.Contains("diagnostic", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(fixture.Source, fixture.Context.LogSession.RootPath);
        Assert.True(File.Exists(fixture.Evidence));
        Assert.True(File.Exists(fixture.Context.RuntimeState.DeploymentSummaryPath));
        if (stateFailure is not null) Assert.True(stateFailure.FailureInjected);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Finalize_InheritedBootstrapEvidence_IsRequiredAndIncludedInSupport(bool blockCopy)
    {
        using Fixture fixture = new();
        string inherited = Path.Combine(fixture.Root, "usb-cache", "Logs", "SESSION01");
        string startup = Path.Combine(inherited, "Startup", "launch-1");
        string pending = Path.Combine(inherited, "PendingLogs");
        Directory.CreateDirectory(startup);
        Directory.CreateDirectory(pending);
        File.WriteAllText(Path.Combine(startup, "status.json"), "bootstrap launch evidence");
        File.WriteAllText(Path.Combine(inherited, "FoundryBootstrap.log"), "bootstrap log");
        File.WriteAllText(Path.Combine(inherited, "unrelated-config.json"), "not a diagnostic");
        File.WriteAllText(Path.Combine(pending, "pending.json"), "delivery state");
        DeploymentStorageLayout layout = DeploymentStorageLayout.FromPartitionRoot(fixture.Root);
        string finalStatus = Path.Combine(layout.Root, "Logs", "Bootstrap", "Startup", "launch-1", "status.json");
        if (blockCopy) Directory.CreateDirectory(finalStatus);
        else
        {
            string previousStatus = Path.Combine(fixture.Source, "Logs", "Bootstrap", "Startup", "launch-1", "status.json");
            Directory.CreateDirectory(Path.GetDirectoryName(previousStatus)!);
            File.WriteAllText(previousStatus, "older staging snapshot");
        }
        string? prior = Environment.GetEnvironmentVariable(DiagnosticSessionContext.PersistenceDirectoryEnvironmentVariableName);
        try
        {
            Environment.SetEnvironmentVariable(DiagnosticSessionContext.PersistenceDirectoryEnvironmentVariableName, inherited);
            string[] inheritedFiles = DeploymentLogService.EnumerateSupportFiles([], inherited);
            Assert.Contains(Path.Combine(startup, "status.json"), inheritedFiles);
            Assert.DoesNotContain(Path.Combine(inherited, "unrelated-config.json"), inheritedFiles);
            Assert.DoesNotContain(Path.Combine(pending, "pending.json"), inheritedFiles);
            DeploymentStepResult result = await new FinalizeDeploymentAndWriteLogsStep().ExecuteAsync(fixture.Context, TestContext.Current.CancellationToken);
            Assert.Equal(DeploymentStepState.Succeeded, result.State);
            if (blockCopy)
            {
                Assert.Equal(fixture.Source, fixture.Context.LogSession.RootPath);
                Assert.True(File.Exists(fixture.Evidence));
                Assert.Contains("Diagnostic handoff incomplete", result.Message, StringComparison.Ordinal);
            }
            else
            {
                Assert.Equal("bootstrap launch evidence", File.ReadAllText(finalStatus));
                Assert.Contains(finalStatus, DeploymentLogService.EnumerateSupportFiles([layout.LogsDeployment]));
                Assert.True(File.Exists(Path.Combine(layout.LogsDeployment, "FoundryBootstrap.log")));
                Assert.False(File.Exists(Path.Combine(layout.LogsDeployment, "unrelated-config.json")));
                Assert.Empty(Directory.GetDirectories(layout.Root, "PendingLogs", SearchOption.AllDirectories));
            }
            Assert.True(File.Exists(Path.Combine(pending, "pending.json")));
        }
        finally { Environment.SetEnvironmentVariable(DiagnosticSessionContext.PersistenceDirectoryEnvironmentVariableName, prior); }
    }

    [Fact]
    public async Task Finalize_CopiesNestedEvidenceAndRetiresBothRegistrationsBeforeShutdown()
    {
        using Fixture fixture = new();
        string startup = Path.Combine(fixture.Root, "startup", FoundryDeployLogging.LogFileName);
        Directory.CreateDirectory(Path.GetDirectoryName(startup)!);
        File.WriteAllText(Path.Combine(Path.GetDirectoryName(startup)!, "runtime.dll"), "executable payload");
        string? prior = Environment.GetEnvironmentVariable(DiagnosticSessionContext.PersistenceDirectoryEnvironmentVariableName);
        try
        {
            using var logger = (IDisposable)FoundryDeployLogging.CreateLogger(startup);
            FoundryDeployLogging.RegisterPersistenceDirectory(fixture.Context.LogSession.LogsDirectoryPath);
            Environment.SetEnvironmentVariable(DiagnosticSessionContext.PersistenceDirectoryEnvironmentVariableName, fixture.Context.LogSession.LogsDirectoryPath);
            string autopilot = Path.Combine(fixture.Source, "Logs", "AutopilotHash", "status.json");
            string tool = Path.Combine(fixture.Source, "Logs", "Tools", "DISM", "diagnostic.txt");
            Directory.CreateDirectory(Path.GetDirectoryName(autopilot)!);
            Directory.CreateDirectory(Path.GetDirectoryName(tool)!);
            File.WriteAllText(autopilot, "autopilot evidence");
            File.WriteAllText(tool, "tool evidence");
            await new FinalizeDeploymentAndWriteLogsStep().ExecuteAsync(fixture.Context, TestContext.Current.CancellationToken);
            DeploymentStorageLayout layout = DeploymentStorageLayout.FromPartitionRoot(fixture.Root);

            Assert.Equal(layout.LogsDeployment, fixture.Context.LogSession.LogsDirectoryPath);
            Assert.True(File.Exists(Path.Combine(layout.LogsDeployment, "Startup", "evidence.json")));
            Assert.Equal(Path.Combine(layout.StateDeployment, "deployment-summary.json"), fixture.Context.RuntimeState.DeploymentSummaryPath);
            Assert.True(File.Exists(Path.Combine(layout.LogsAutopilotHash, "status.json")));
            Assert.True(File.Exists(Path.Combine(layout.Root, "Logs", "Tools", "DISM", "diagnostic.txt")));
            Assert.False(File.Exists(Path.Combine(layout.Root, "Logs", FoundryDeployLogging.LogFileName)));
            Assert.False(File.Exists(Path.Combine(layout.LogsDeployment, "runtime.dll")));
            Assert.False(Directory.Exists(fixture.Source));
            FoundryDeployLogging.PersistCurrentLogs();
            Assert.False(Directory.Exists(fixture.Source));
        }
        finally
        {
            Environment.SetEnvironmentVariable(DiagnosticSessionContext.PersistenceDirectoryEnvironmentVariableName, prior);
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Finalize_DoesNotDeleteActiveSinkOrPendingDeliveryState(bool activeSink)
    {
        using Fixture fixture = new();
        IDisposable? logger = null;
        try
        {
            if (activeSink)
                logger = (IDisposable)FoundryDeployLogging.CreateLogger(Path.Combine(fixture.Source, "Logs", FoundryDeployLogging.LogFileName));
            else
            {
                string queue = Path.Combine(fixture.Source, "Logs", "PendingLogs");
                Directory.CreateDirectory(queue);
                File.WriteAllText(Path.Combine(queue, "pending.json"), "delivery");
            }
            await new FinalizeDeploymentAndWriteLogsStep().ExecuteAsync(fixture.Context, TestContext.Current.CancellationToken);
            Assert.True(Directory.Exists(fixture.Source));
            Assert.Empty(Directory.GetDirectories(fixture.Context.LogSession.RootPath, "PendingLogs", SearchOption.AllDirectories));
        }
        finally { logger?.Dispose(); }
    }

    [Fact]
    public async Task Finalize_WhenSummaryCannotBePublished_PreservesSourceEvidence()
    {
        using Fixture fixture = new();
        DeploymentStorageLayout layout = DeploymentStorageLayout.FromPartitionRoot(fixture.Root);
        Directory.CreateDirectory(Path.Combine(layout.StateDeployment, "deployment-summary.json"));
        DeploymentStepResult result = await new FinalizeDeploymentAndWriteLogsStep().ExecuteAsync(fixture.Context, TestContext.Current.CancellationToken);
        Assert.Equal(DeploymentStepState.Succeeded, result.State);
        Assert.Contains("Diagnostic persistence incomplete", result.Message, StringComparison.Ordinal);
        Assert.True(Directory.Exists(fixture.Source));
        Assert.Null(fixture.Context.RuntimeState.DeploymentSummaryPath);
        Assert.Equal(layout.Root, fixture.Context.LogSession.RootPath);
    }

    [Fact]
    public async Task Finalize_PreservesPendingFirstBootStateAndPayloads()
    {
        using Fixture fixture = new();
        DeploymentStorageLayout layout = DeploymentStorageLayout.FromPartitionRoot(fixture.Root);
        Directory.CreateDirectory(layout.StatePreOobe);
        Directory.CreateDirectory(layout.RuntimePreOobe);
        File.WriteAllText(Path.Combine(layout.StatePreOobe, "status.json"), "{\"status\":\"pending\"}");
        File.WriteAllText(Path.Combine(layout.RuntimePreOobe, "run.cmd"), "pending runtime");
        await new FinalizeDeploymentAndWriteLogsStep().ExecuteAsync(fixture.Context, TestContext.Current.CancellationToken);
        Assert.True(File.Exists(Path.Combine(layout.StatePreOobe, "status.json")));
        Assert.True(File.Exists(Path.Combine(layout.RuntimePreOobe, "run.cmd")));
    }

    [Fact]
    public async Task Rebind_WaitsForQueuedStateBeforeSwitchingSession()
    {
        var service = new DelayedStateService();
        using Fixture fixture = new(service);
        fixture.Context.SetCurrentOperation("pending-write");
        await service.Started.Task;
        Task<DeploymentArtifactHandoffResult> transfer = fixture.Context.RebindLogSessionToTargetAsync(
            DeploymentStorageLayout.FromPartitionRoot(fixture.Root).Root, TestContext.Current.CancellationToken);
        Assert.False(transfer.IsCompleted);
        Assert.Equal(fixture.Source, fixture.Context.LogSession.RootPath);
        service.Release.TrySetResult();
        DeploymentArtifactHandoffResult result = await transfer;
        Assert.Empty(result.Failures);
        Assert.Contains("pending-write", File.ReadAllText(fixture.Context.LogSession.StateFilePath), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Finalize_WhenStateWriteAfterSummaryFails_RetainsSourceAndReportsActualDestination()
    {
        var service = new LockedDestinationStateService(2);
        using Fixture fixture = new(service);
        DeploymentStepResult result = await new FinalizeDeploymentAndWriteLogsStep().ExecuteAsync(fixture.Context, TestContext.Current.CancellationToken);
        Assert.True(service.FailureInjected);
        Assert.Equal(DeploymentStepState.Succeeded, result.State);
        Assert.Contains("Diagnostic persistence incomplete", result.Message, StringComparison.Ordinal);
        Assert.True(Directory.Exists(fixture.Source));
        Assert.True(File.Exists(fixture.Context.RuntimeState.DeploymentSummaryPath));
        Assert.Contains(fixture.Context.LogSession.RootPath, result.Message, StringComparison.Ordinal);
    }

    private sealed class LockedDestinationStateService(int failAtWrite) : IDeploymentLogService
    {
        private readonly DeploymentLogService _inner = new();
        private int _targetWrites;
        public bool FailureInjected { get; private set; }
        public DeploymentLogSession Initialize(string root) => _inner.Initialize(root);
        public Task AppendAsync(DeploymentLogSession session, DeploymentLogLevel level, string message, CancellationToken cancellationToken = default) =>
            _inner.AppendAsync(session, level, message, cancellationToken);
        public async Task SaveStateAsync<T>(DeploymentLogSession session, T state, CancellationToken cancellationToken = default)
        {
            if (session.RootPath.EndsWith(Path.Combine("Windows", "Temp", "Foundry"), StringComparison.OrdinalIgnoreCase) &&
                Interlocked.Increment(ref _targetWrites) == failAtWrite)
            {
                FailureInjected = true;
                using FileStream locked = new(session.StateFilePath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
                await _inner.SaveStateAsync(session, state, cancellationToken);
            }
            else await _inner.SaveStateAsync(session, state, cancellationToken);
        }
    }

    private sealed class DelayedStateService : IDeploymentLogService
    {
        private readonly DeploymentLogService _inner = new();
        private int _writes;
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public DeploymentLogSession Initialize(string root) => _inner.Initialize(root);
        public Task AppendAsync(DeploymentLogSession session, DeploymentLogLevel level, string message, CancellationToken cancellationToken = default) =>
            _inner.AppendAsync(session, level, message, cancellationToken);
        public async Task SaveStateAsync<T>(DeploymentLogSession session, T state, CancellationToken cancellationToken = default)
        {
            if (Interlocked.Increment(ref _writes) == 1)
            {
                Started.TrySetResult();
                await Release.Task.WaitAsync(cancellationToken);
            }
            await _inner.SaveStateAsync(session, state, cancellationToken);
        }
    }

    private sealed class Fixture : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "foundry-finalize-" + Guid.NewGuid().ToString("N"));
        public string Source => Path.Combine(Root, "Foundry");
        public string Evidence => Path.Combine(Context.LogSession.RootPath == Source ? Context.LogSession.LogsDirectoryPath : Path.Combine(Source, "Logs", "Deployment"), "Startup", "evidence.json");
        public DeploymentStepExecutionContext Context { get; }
        public Fixture(IDeploymentLogService? logService = null)
        {
            Context = DeploymentStepExecutionContextTests.CreateExecutionContext(Source, Path.Combine(Source, "Cache"), targetFoundryRoot: Source, logService: logService ?? new DeploymentLogService());
            Context.RuntimeState.TargetWindowsPartitionRoot = Root;
            Directory.CreateDirectory(Path.GetDirectoryName(Evidence)!);
            File.WriteAllText(Evidence, "startup evidence");
            Context.SetCurrentStep(new FinalizeDeploymentAndWriteLogsStep(), 1);
        }
        public void Dispose() { Context.Dispose(); if (Directory.Exists(Root)) Directory.Delete(Root, true); }
    }
}
