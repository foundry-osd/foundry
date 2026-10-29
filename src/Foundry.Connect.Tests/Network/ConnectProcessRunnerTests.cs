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
            ConnectProcessLogging.EveryRun,
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

    // A status read repeated on every monitor pass logs its first outcome and then only an outcome that differs.
    [Fact]
    public async Task RunAsync_OnChange_LogsOnlyWhenTheOutcomeChanges()
    {
        using var directory = new TemporaryDirectory();
        string marker = Path.Combine(directory.Path, "failing.marker");
        string arguments = $"/d /c \"if exist \"{marker}\" (exit /b 7) else (exit /b 0)\"";
        var logger = new RecordingLogger<ConnectProcessRunner>();
        var runner = new ConnectProcessRunner(TimeProvider.System, logger);

        async Task<int> RunAsync() => (await runner.RunAsync(
            CommandProcessor, arguments, TimeSpan.FromSeconds(30), ConnectProcessLogging.OnChange, TestContext.Current.CancellationToken)).ExitCode;

        int[] healthy = [await RunAsync(), await RunAsync()];
        Assert.Equal([0, 0], healthy);
        File.WriteAllText(marker, string.Empty);
        int[] failing = [await RunAsync(), await RunAsync()];
        Assert.Equal([7, 7], failing);
        File.Delete(marker);
        Assert.Equal(0, await RunAsync());

        Assert.Equal(
            [(LogLevel.Debug, 0), (LogLevel.Warning, 7), (LogLevel.Debug, 0)],
            logger.Entries.Select(static entry => (entry.Level, (int)entry.Properties["ExitCode"]!)));
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
            ConnectProcessLogging.EveryRun,
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
            ConnectProcessLogging.EveryRun,
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
            CommandProcessor, "/d /c exit /b 0", TimeSpan.FromMilliseconds(milliseconds), ConnectProcessLogging.EveryRun, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task RunAsync_WhenCallerCancels_Throws()
    {
        var runner = new ConnectProcessRunner(TimeProvider.System, new RecordingLogger<ConnectProcessRunner>());
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => runner.RunAsync(
            CommandProcessor, "/d /c exit /b 0", TimeSpan.FromSeconds(5), ConnectProcessLogging.EveryRun, cancellation.Token));
    }
}
