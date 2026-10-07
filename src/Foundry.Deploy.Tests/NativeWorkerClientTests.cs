// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Text.Json;
using Foundry.Deploy.Services.Deployment;
using Foundry.Deploy.Services.Deployment.Native;
using Foundry.Deploy.Services.System;
using Foundry.Utilities.Processes;

namespace Foundry.Deploy.Tests;

public sealed class NativeWorkerClientTests
{
    [Fact]
    public async Task SuccessfulMutation_FinalizesProgressAfterWorkerExit()
    {
        using var workspace = new Workspace();
        var runner = new WorkerRunner(new NativeWorkerMessage { Percent = 25 }, new() { Kind = "complete", Result = new() });
        var client = new NativeWorkerClient(runner, "Foundry.Deploy.exe");
        var reports = new List<double>();
        var progress = new SynchronousProgress(value =>
        {
            if (value == 100) Assert.True(runner.Completed);
            reports.Add(value);
        });

        await client.ExecuteAsync(new() { Operation = NativeWorkerOperation.MountImage }, workspace.Path,
            DeploymentOperationNames.MountRecoveryImage, progress, TestContext.Current.CancellationToken);

        Assert.Equal([25d, 100d], reports);
    }

    [Fact]
    public async Task NativeError_PreservesStatusAndCleanupErrorsWithoutReplay()
    {
        using var workspace = new Workspace();
        var runner = new WorkerRunner(new NativeWorkerMessage
        {
            Kind = "complete",
            Error = new("DismAddDriver", unchecked((int)0x80070005), "Access denied", ["DismCloseSession failed"])
        });
        var client = new NativeWorkerClient(runner, "Foundry.Deploy.exe");

        DeploymentOperationException exception = await Assert.ThrowsAsync<DeploymentOperationException>(() =>
            client.ExecuteAsync(new() { Operation = NativeWorkerOperation.AddDrivers }, workspace.Path,
                DeploymentOperationNames.ApplyDriverPack, null, TestContext.Current.CancellationToken));

        Assert.Equal("0x80070005", exception.Failure.Code);
        NativeOperationException native = Assert.IsType<NativeOperationException>(exception.InnerException);
        Assert.Equal("DismAddDriver", native.Function);
        Assert.Contains("DismCloseSession failed", native.CleanupErrors);
        Assert.Equal(1, runner.Calls);
        Assert.False(File.Exists(runner.RequestPath));
    }

    [Fact]
    public async Task ProgressFailure_IsDeferredUntilWorkerCompletionAndRequestRelease()
    {
        using var workspace = new Workspace();
        var runner = new WorkerRunner(new NativeWorkerMessage { Percent = 25 }, new() { Kind = "complete", Result = new() });
        var client = new NativeWorkerClient(runner, "Foundry.Deploy.exe");

        await Assert.ThrowsAsync<InvalidOperationException>(() => client.ExecuteAsync(new() { Operation = NativeWorkerOperation.ApplyWim },
            workspace.Path, DeploymentOperationNames.ApplyOperatingSystemImage, new ThrowingProgress(), TestContext.Current.CancellationToken));

        Assert.True(runner.Completed);
        Assert.False(runner.Token.CanBeCanceled);
        Assert.False(File.Exists(runner.RequestPath));
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("duplicate")]
    [InlineData("malformed")]
    public async Task InvalidProtocol_FailsClosed(string failure)
    {
        using var workspace = new Workspace();
        var runner = new WorkerRunner { ProtocolFailure = failure };
        var client = new NativeWorkerClient(runner, "Foundry.Deploy.exe");

        DeploymentOperationException exception = await Assert.ThrowsAsync<DeploymentOperationException>(() =>
            client.ExecuteAsync(new() { Operation = NativeWorkerOperation.InspectMount }, workspace.Path,
                DeploymentOperationNames.MountRecoveryImage, null, TestContext.Current.CancellationToken));

        Assert.Equal("native_worker_protocol", exception.Failure.Code);
        Assert.Equal(1, runner.Calls);
    }

    [Fact]
    public async Task CancellationDuringMutation_WaitsForWorkerBeforeSurfacingCancellation()
    {
        using var workspace = new Workspace();
        using var cancellation = new CancellationTokenSource();
        var runner = new WorkerRunner(new NativeWorkerMessage { Kind = "complete", Result = new() }) { BeforeCompletion = cancellation.Cancel };
        var client = new NativeWorkerClient(runner, "Foundry.Deploy.exe");

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.ExecuteAsync(new() { Operation = NativeWorkerOperation.MountImage },
            workspace.Path, DeploymentOperationNames.MountRecoveryImage, null, cancellation.Token));

        Assert.True(runner.Completed);
        Assert.True(runner.CancelSignaled);
        Assert.False(runner.Token.CanBeCanceled);
        Assert.False(File.Exists(runner.RequestPath));
    }

    [Fact]
    public async Task NativeErrorAfterCancellation_SurfacesCancellationAndKeepsNativeStatus()
    {
        using var workspace = new Workspace();
        using var cancellation = new CancellationTokenSource();
        var runner = new WorkerRunner(new NativeWorkerMessage
        {
            Kind = "complete",
            Error = new("WIMApplyImage", NativeOperationException.RequestAborted, "WIM application was canceled before completion.", [])
        })
        { BeforeCompletion = cancellation.Cancel };
        var client = new NativeWorkerClient(runner, "Foundry.Deploy.exe");

        OperationCanceledException exception = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.ExecuteAsync(
            new() { Operation = NativeWorkerOperation.ApplyWim }, workspace.Path, DeploymentOperationNames.ApplyOperatingSystemImage, null, cancellation.Token));

        Assert.Equal(cancellation.Token, exception.CancellationToken);
        Assert.Equal(NativeOperationException.RequestAborted, Assert.IsType<NativeOperationException>(exception.InnerException).ErrorCode);
        Assert.False(runner.Token.CanBeCanceled);
    }

    [Fact]
    public async Task UncancelableCaller_DoesNotRequestACancelEvent()
    {
        using var workspace = new Workspace();
        var runner = new WorkerRunner(new NativeWorkerMessage { Kind = "complete", Result = new() });
        var client = new NativeWorkerClient(runner, "Foundry.Deploy.exe");

        await client.ExecuteAsync(new() { Operation = NativeWorkerOperation.UnmountImage }, workspace.Path,
            DeploymentOperationNames.UnmountRecoveryImage, null, CancellationToken.None);

        Assert.Null(runner.Request!.CancelEventName);
    }

    private sealed class ThrowingProgress : IProgress<double>
    {
        public void Report(double value) => throw new InvalidOperationException("Progress consumer failed");
    }

    private sealed class SynchronousProgress(Action<double> report) : IProgress<double>
    {
        public void Report(double value) => report(value);
    }

    private sealed class WorkerRunner(params NativeWorkerMessage[] messages) : IProcessRunner
    {
        public int Calls { get; private set; }
        public bool Completed { get; private set; }
        public CancellationToken Token { get; private set; }
        public string? RequestPath { get; private set; }
        public NativeWorkerRequest? Request { get; private set; }
        public bool CancelSignaled { get; private set; }
        public string? ProtocolFailure { get; init; }
        public Action? BeforeCompletion { get; init; }

        public Task<ProcessExecutionResult> RunAsync(string fileName, string arguments, string workingDirectory, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<ProcessExecutionResult> RunAsync(string fileName, IEnumerable<string> arguments, string workingDirectory, CancellationToken cancellationToken = default) =>
            RunAsync(fileName, arguments, workingDirectory, null, null, cancellationToken);
        public Task<ProcessExecutionResult> RunAsync(string fileName, IEnumerable<string> arguments, string workingDirectory,
            Action<string>? onOutputData, Action<string>? onErrorData, CancellationToken cancellationToken = default)
        {
            Calls++;
            Token = cancellationToken;
            string[] args = arguments.ToArray();
            Assert.Equal("--native-deployment-worker", args[0]);
            RequestPath = args[1];
            Request = JsonSerializer.Deserialize<NativeWorkerRequest>(File.ReadAllText(RequestPath));
            Assert.NotNull(Request);
            foreach (NativeWorkerMessage message in messages) onOutputData?.Invoke(JsonSerializer.Serialize(message));
            if (ProtocolFailure == "duplicate")
            {
                string complete = JsonSerializer.Serialize(new NativeWorkerMessage { Kind = "complete", Result = new() });
                onOutputData?.Invoke(complete);
                onOutputData?.Invoke(complete);
            }
            if (ProtocolFailure == "malformed") onOutputData?.Invoke("broken");
            BeforeCompletion?.Invoke();
            if (!string.IsNullOrEmpty(Request.CancelEventName))
            {
                // The worker observes cancellation through the named event, never through process termination.
                using EventWaitHandle signal = EventWaitHandle.OpenExisting(Request.CancelEventName);
                CancelSignaled = signal.WaitOne(0);
            }
            Completed = true;
            return Task.FromResult(new ProcessExecutionResult { ExitCode = messages.Any(message => message.Error is not null) ? 1 : 0 });
        }
    }

    private sealed class Workspace : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "FoundryNativeWorkerTests", Guid.NewGuid().ToString("N"));
        public Workspace() => Directory.CreateDirectory(Path);
        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
