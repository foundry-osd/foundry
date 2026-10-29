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
    public async Task RunAsync_CapturesExitCodeAndLogsStandardErrorLengthOnly()
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
        Assert.Equal(result.StandardError.Length, warning.Properties["StandardErrorLength"]);
        Assert.DoesNotContain("StandardError", warning.Properties.Keys);
        Assert.DoesNotContain("failed", warning.Message, StringComparison.Ordinal);
        Assert.Equal("cmd.exe", warning.Properties["ToolName"]);
    }

    [Fact]
    public async Task RunAsync_WhenTimeoutElapses_KillsProcessAndReportsTimedOut()
    {
        var clock = new ManualTimeProvider();
        var logger = new RecordingLogger<ConnectProcessRunner>();
        var runner = new ConnectProcessRunner(clock, logger);

        Task<ConnectProcessResult> run = runner.RunAsync(
            CommandProcessor,
            "/d /c \"ping -n 30 127.0.0.1 >nul\"",
            TimeSpan.FromSeconds(5),
            TestContext.Current.CancellationToken);
        clock.Advance(TimeSpan.FromSeconds(5));
        ConnectProcessResult result = await run.WaitAsync(TimeSpan.FromSeconds(20), TestContext.Current.CancellationToken);

        Assert.Equal(ConnectProcessStatus.TimedOut, result.Status);
        Assert.Equal(-1, result.ExitCode);
        RecordedLogEntry warning = Assert.Single(logger.Entries, static entry => entry.Level == LogLevel.Warning);
        Assert.Equal(5d, warning.Properties["TimeoutSeconds"]);
        Assert.Equal("cmd.exe", warning.Properties["ToolName"]);
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

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-1000)]
    public async Task RunAsync_WhenTimeoutIsNotPositive_Throws(int milliseconds)
    {
        var runner = new ConnectProcessRunner(TimeProvider.System, new RecordingLogger<ConnectProcessRunner>());

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => runner.RunAsync(
            CommandProcessor, "/d /c exit /b 0", TimeSpan.FromMilliseconds(milliseconds), TestContext.Current.CancellationToken));
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
