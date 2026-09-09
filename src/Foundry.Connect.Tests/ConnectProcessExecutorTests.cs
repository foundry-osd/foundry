// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Connect.Services.System;
using Foundry.Utilities.Processes;
using Microsoft.Extensions.Logging;

namespace Foundry.Connect.Tests;

public sealed class ConnectProcessExecutorTests
{
    [Fact]
    public async Task ExecuteAsync_CapturesOutputAndExitCode()
    {
        var logger = new RecordingLogger();
        var executor = new ConnectProcessExecutor(logger);
        string commandProcessor = Environment.GetEnvironmentVariable("ComSpec")
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32", "cmd.exe");

        ProcessExecutionResult result = await executor.ExecuteAsync(
            commandProcessor,
            "/d /c \"echo connected & echo failed 1>&2 & exit /b 7\"",
            TestContext.Current.CancellationToken);

        Assert.Equal(7, result.ExitCode);
        Assert.Contains("connected", result.StandardOutput, StringComparison.Ordinal);
        Assert.Contains("failed", result.StandardError, StringComparison.Ordinal);
        Assert.Equal(1, logger.WarningCount);
        Assert.Equal(7, logger.LastScope["ExitCode"]);
        Assert.Equal("cmd.exe", logger.LastScope["ToolName"]);
        Assert.Equal(true, logger.LastScope["ProcessOutputOmitted"]);
        Assert.DoesNotContain("ProcessStdout", logger.LastScope.Keys);
        Assert.DoesNotContain("Arguments", logger.LastScope.Keys);
    }

    [Fact]
    public async Task ExecuteAsync_WhenExecutableCannotStart_ReturnsFailureResult()
    {
        var logger = new RecordingLogger();
        var executor = new ConnectProcessExecutor(logger);
        string missingExecutable = Path.Combine(Path.GetTempPath(), $"foundry-missing-{Guid.NewGuid():N}.exe");

        ProcessExecutionResult result = await executor.ExecuteAsync(
            missingExecutable,
            string.Empty,
            TestContext.Current.CancellationToken);

        Assert.Equal(-1, result.ExitCode);
        Assert.Empty(result.StandardOutput);
        Assert.NotEmpty(result.StandardError);
        Assert.Equal(1, logger.WarningCount);
        Assert.Equal(Path.GetFileName(missingExecutable), logger.LastScope["ToolName"]);
        Assert.Equal("process_start_failed", logger.LastScope["FailureReason"]);
        Assert.True(logger.LastScope.ContainsKey("FailureCode"));
        Assert.DoesNotContain("ExitCode", logger.LastScope.Keys);
    }

    private sealed class RecordingLogger : ILogger
    {
        public int WarningCount { get; private set; }
        public IReadOnlyDictionary<string, object> LastScope { get; private set; } = new Dictionary<string, object>();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull
        {
            if (state is IReadOnlyDictionary<string, object> properties)
            {
                LastScope = properties;
            }
            return null;
        }

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (logLevel == LogLevel.Warning)
            {
                WarningCount++;
            }
        }
    }
}
