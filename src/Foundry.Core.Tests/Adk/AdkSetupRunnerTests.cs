// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.ComponentModel;
using Foundry.Core.Services.Adk;

namespace Foundry.Core.Tests.Adk;

public sealed class AdkSetupRunnerTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "FoundryAdkTests", Guid.NewGuid().ToString("N"));
    private readonly string setupPath;
    private readonly string logPath;

    public AdkSetupRunnerTests()
    {
        Directory.CreateDirectory(root);
        setupPath = Path.Combine(root, "setup.exe");
        logPath = Path.Combine(root, "logs with spaces", "setup.log");
        File.WriteAllText(setupPath, string.Empty);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3010)]
    public async Task RunAsync_PreservesSuccessAndRebootCodesAndRequestsSetupLog(int exitCode)
    {
        AdkSetupRunner runner = new(info =>
        {
            Assert.True(Directory.Exists(Path.GetDirectoryName(logPath)));
            Assert.Equal(setupPath, info.FileName);
            Assert.Equal($"/quiet /log \"{logPath}\"", info.Arguments);
            Assert.True(info.UseShellExecute);
            Assert.Equal("runas", info.Verb);
            return new SetupProcess(Task.CompletedTask, exitCode);
        });

        Assert.Equal(exitCode, await runner.RunAsync(setupPath, "/quiet", logPath, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task RunToolAsync_RunsHiddenHelperWithoutSetupLogSwitch()
    {
        AdkSetupRunner runner = new(info =>
        {
            Assert.Equal(setupPath, info.FileName);
            Assert.Equal("/uninstall", info.Arguments);
            Assert.Equal("runas", info.Verb);
            Assert.Equal(System.Diagnostics.ProcessWindowStyle.Hidden, info.WindowStyle);
            return new SetupProcess(Task.CompletedTask, 0);
        });

        Assert.Equal(0, await runner.RunToolAsync(setupPath, "/uninstall", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task RunToolAsync_ClassifiesHelperFailureExit()
    {
        AdkSetupRunner runner = new(_ => new SetupProcess(Task.CompletedTask, 1));
        AdkSetupException error = await Assert.ThrowsAsync<AdkSetupException>(() => runner.RunToolAsync(setupPath, "/uninstall", TestContext.Current.CancellationToken));
        Assert.Equal("installer_exit_failed", error.Reason);
        Assert.Equal(1, error.ExitCode);
    }

    [Theory]
    [InlineData(1223, "elevation_cancelled")]
    [InlineData(5, "launch_failed")]
    [InlineData(2, "setup_not_found")]
    [InlineData(3, "setup_not_found")]
    public async Task RunAsync_ClassifiesNativeLaunchFailures(int nativeCode, string reason)
    {
        AdkSetupRunner runner = new(_ => throw new Win32Exception(nativeCode));
        AdkSetupException error = await Assert.ThrowsAsync<AdkSetupException>(() => runner.RunAsync(setupPath, "/quiet", logPath, TestContext.Current.CancellationToken));
        Assert.Equal(reason, error.Reason);
        Assert.Equal(nativeCode, error.NativeErrorCode);
        Assert.Equal(setupPath, error.SetupPath);
        Assert.Equal(logPath, error.LogPath);
    }

    [Fact]
    public async Task RunAsync_ReportsMissingSetupWithoutLaunching()
    {
        File.Delete(setupPath);
        AdkSetupRunner runner = new(_ => throw new Xunit.Sdk.XunitException("Must not launch missing setup."));
        AdkSetupException error = await Assert.ThrowsAsync<AdkSetupException>(() => runner.RunAsync(setupPath, "/quiet", logPath, TestContext.Current.CancellationToken));
        Assert.Equal("setup_not_found", error.Reason);
    }

    [Fact]
    public async Task RunAsync_RejectsMissingProcessInsteadOfAssumingSuccess()
    {
        AdkSetupRunner runner = new(_ => null);
        AdkSetupException error = await Assert.ThrowsAsync<AdkSetupException>(() => runner.RunAsync(setupPath, "/quiet", logPath, TestContext.Current.CancellationToken));
        Assert.Equal("process_not_started", error.Reason);
    }

    [Fact]
    public async Task RunAsync_StopsBeforeLaunchWhenLogDirectoryCannotBePrepared()
    {
        File.WriteAllText(Path.GetDirectoryName(logPath)!, "not a directory");
        AdkSetupRunner runner = new(_ => throw new Xunit.Sdk.XunitException("Must not launch without diagnostics."));
        AdkSetupException error = await Assert.ThrowsAsync<AdkSetupException>(() => runner.RunAsync(setupPath, "/quiet", logPath, TestContext.Current.CancellationToken));
        Assert.Equal("log_preparation_failed", error.Reason);
    }

    [Theory]
    [InlineData(-2146889721)]
    [InlineData(1618)]
    public async Task RunAsync_PreservesInstallerFailureCode(int exitCode)
    {
        AdkSetupRunner runner = new(_ => new SetupProcess(Task.CompletedTask, exitCode));
        AdkSetupException error = await Assert.ThrowsAsync<AdkSetupException>(() => runner.RunAsync(setupPath, "/quiet", logPath, TestContext.Current.CancellationToken));
        Assert.Equal("installer_exit_failed", error.Reason);
        Assert.Equal(exitCode, error.ExitCode);
    }

    [Fact]
    public async Task RunAsync_DoesNotLaunchWhenAlreadyCancelled()
    {
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();
        AdkSetupRunner runner = new(_ => throw new Xunit.Sdk.XunitException("Must not launch cancelled operation."));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => runner.RunAsync(setupPath, "/quiet", logPath, cancellation.Token));
    }

    [Fact]
    public async Task RunAsync_WaitsForInstallerExitBeforeHonoringCancellation()
    {
        using CancellationTokenSource cancellation = new();
        TaskCompletionSource exited = new(TaskCreationOptions.RunContinuationsAsynchronously);
        SetupProcess process = new(exited.Task, 0);
        AdkSetupRunner runner = new(_ => process);
        Task<int> running = runner.RunAsync(setupPath, "/quiet", logPath, cancellation.Token);
        cancellation.Cancel();
        Assert.False(running.IsCompleted);
        Assert.False(process.Disposed);
        exited.SetResult();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => running);
        Assert.True(process.Disposed);
    }

    [Fact]
    public async Task RunAsync_PreservesInstallerFailureWhenCancellationArrivesDuringSetup()
    {
        using CancellationTokenSource cancellation = new();
        TaskCompletionSource exited = new(TaskCreationOptions.RunContinuationsAsynchronously);
        AdkSetupRunner runner = new(_ => new SetupProcess(exited.Task, -2146889721));
        Task<int> running = runner.RunAsync(setupPath, "/quiet", logPath, cancellation.Token);
        cancellation.Cancel();
        exited.SetResult();
        AdkSetupException error = await Assert.ThrowsAsync<AdkSetupException>(() => running);
        Assert.Equal("installer_exit_failed", error.Reason);
        Assert.Equal(-2146889721, error.ExitCode);
    }

    public void Dispose() => Directory.Delete(root, recursive: true);

    private sealed class SetupProcess(Task exit, int exitCode) : IAdkSetupProcess
    {
        public bool Disposed { get; private set; }
        public int ExitCode => exitCode;
        public Task WaitForExitAsync() => exit;
        public void Dispose() => Disposed = true;
    }
}
