// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Connect.Network.Native;
using Foundry.Connect.Tests.Support;
using Microsoft.Extensions.Logging;

namespace Foundry.Connect.Tests.Network;

public sealed class ConnectProcessRunnerTests
{
    private static readonly string CommandProcessor = Environment.GetEnvironmentVariable("ComSpec")
        ?? Path.Combine(Environment.SystemDirectory, "cmd.exe");

    [Fact]
    public async Task RunAsync_CapturesExitCodeAndLogsStandardErrorLocally()
    {
        var logger = new RecordingLogger<ConnectProcessRunner>();
        var runner = new ConnectProcessRunner(TimeProvider.System, logger);

        ConnectProcessResult result = await runner.RunAsync(
            CommandProcessor,
            "/d /c \"echo connected & echo failed 1>&2 & exit /b 7\"",
            TimeSpan.FromSeconds(30),
            TestContext.Current.CancellationToken);

        Assert.Equal(ConnectProcessStatus.Completed, result.Status);
        Assert.Equal(7, result.ExitCode);
        Assert.Contains("failed", result.StandardError, StringComparison.Ordinal);
        RecordedLogEntry warning = Assert.Single(logger.Entries, static entry => entry.Level == LogLevel.Warning);
        Assert.Equal(7, warning.Properties["ExitCode"]);
        Assert.Contains("failed", Assert.IsType<string>(warning.Properties["StandardError"]), StringComparison.Ordinal);
        Assert.Equal("cmd.exe", warning.Properties["ToolName"]);
    }

    [Fact]
    public async Task RunAsync_WhenTimeoutElapses_KillsProcessAndReportsTimedOut()
    {
        var clock = new ManualTimeProvider();
        var runner = new ConnectProcessRunner(clock, new RecordingLogger<ConnectProcessRunner>());

        Task<ConnectProcessResult> run = runner.RunAsync(
            CommandProcessor,
            "/d /c \"ping -n 30 127.0.0.1 >nul\"",
            TimeSpan.FromSeconds(5),
            TestContext.Current.CancellationToken);
        clock.Advance(TimeSpan.FromSeconds(5));
        ConnectProcessResult result = await run.WaitAsync(TimeSpan.FromSeconds(20), TestContext.Current.CancellationToken);

        Assert.Equal(ConnectProcessStatus.TimedOut, result.Status);
    }

    [Fact]
    public async Task RunAsync_WhenExecutableIsMissing_ReturnsStartFailed()
    {
        var runner = new ConnectProcessRunner(TimeProvider.System, new RecordingLogger<ConnectProcessRunner>());

        ConnectProcessResult result = await runner.RunAsync(
            Path.Combine(Path.GetTempPath(), $"foundry-missing-{Guid.NewGuid():N}.exe"),
            string.Empty,
            TimeSpan.FromSeconds(5),
            TestContext.Current.CancellationToken);

        Assert.Equal(ConnectProcessStatus.StartFailed, result.Status);
    }

    [Fact]
    public async Task RunAsync_WhenCallerCancels_Throws()
    {
        var runner = new ConnectProcessRunner(TimeProvider.System, new RecordingLogger<ConnectProcessRunner>());
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => runner.RunAsync(
            CommandProcessor, "/d /c exit /b 0", TimeSpan.FromSeconds(5), cancellation.Token));
    }
}
