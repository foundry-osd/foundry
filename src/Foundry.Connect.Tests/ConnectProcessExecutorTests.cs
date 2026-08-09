// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Connect.Services.System;
using Foundry.Utilities.Processes;
using Microsoft.Extensions.Logging.Abstractions;

namespace Foundry.Connect.Tests;

public sealed class ConnectProcessExecutorTests
{
    [Fact]
    public async Task ExecuteAsync_CapturesOutputAndExitCode()
    {
        var executor = new ConnectProcessExecutor(NullLogger<ConnectProcessExecutor>.Instance);
        string commandProcessor = Environment.GetEnvironmentVariable("ComSpec")
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32", "cmd.exe");

        ProcessExecutionResult result = await executor.ExecuteAsync(
            commandProcessor,
            "/d /c \"echo connected & echo failed 1>&2 & exit /b 7\"",
            TestContext.Current.CancellationToken);

        Assert.Equal(7, result.ExitCode);
        Assert.Contains("connected", result.StandardOutput, StringComparison.Ordinal);
        Assert.Contains("failed", result.StandardError, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExecuteAsync_WhenExecutableCannotStart_ReturnsFailureResult()
    {
        var executor = new ConnectProcessExecutor(NullLogger<ConnectProcessExecutor>.Instance);
        string missingExecutable = Path.Combine(Path.GetTempPath(), $"foundry-missing-{Guid.NewGuid():N}.exe");

        ProcessExecutionResult result = await executor.ExecuteAsync(
            missingExecutable,
            string.Empty,
            TestContext.Current.CancellationToken);

        Assert.Equal(-1, result.ExitCode);
        Assert.Empty(result.StandardOutput);
        Assert.NotEmpty(result.StandardError);
    }
}
