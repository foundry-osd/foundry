// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.PostInstall.Execution;
using Foundry.Core.Services.Configuration;

namespace Foundry.PostInstall.Tests;

public sealed class ProcessExecutorTests
{
    [Fact]
    public async Task CustomCommandLineExecutesQuotedExecutableAndCompoundCommands()
    {
        string executable = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "cmd.exe");
        string command = "\"" + executable + "\" /c echo quoted-value & exit /b 7";
        var result = await new PreOobeProcessExecutor().RunAsync(new(executable, [], Path.GetTempPath(), TimeSpan.FromSeconds(10),
            PreOobeCommandLine.BuildArguments(new() { Kind = Foundry.Core.Models.Configuration.PreOobeActionKind.Command, Command = command }, "", "")),
            TestContext.Current.CancellationToken);
        Assert.Equal(7, result.ExitCode);
        Assert.Contains("quoted-value", result.StandardOutput);
        Assert.False(result.TerminationUncertain);
    }

    [Fact]
    public async Task DetachedOrdinaryChild_RemainsSupervisedAfterParentExits()
    {
        string system = Environment.GetFolderPath(Environment.SpecialFolder.System);
        ProcessOutcome outcome = await new PreOobeProcessExecutor().RunAsync(new(Path.Combine(system, "cmd.exe"), [],
            Path.GetTempPath(), TimeSpan.FromMilliseconds(300),
            RawArguments: "/d /s /c \"start \"\" /b ping.exe -n 20 127.0.0.1 >nul 2>nul\""), TestContext.Current.CancellationToken);
        Assert.True(outcome.TimedOut);
        Assert.True(outcome.TerminationUncertain);
    }

    [Fact]
    public async Task Execute_PreservesExitCodeAndCapturesOutput()
    {
        var result = await new PreOobeProcessExecutor().RunAsync(new ProcessCommand(
            Environment.GetEnvironmentVariable("ComSpec")!, ["/d", "/c", "echo observed & exit /b 7"],
            Path.GetTempPath(), TimeSpan.FromSeconds(10)), TestContext.Current.CancellationToken);
        Assert.Equal(7, result.ExitCode);
        Assert.Contains("observed", result.StandardOutput);
        Assert.False(result.TerminationUncertain);
    }

    [Fact]
    public async Task Execute_TimeoutNeverReportsSuccessfulCompletion()
    {
        var result = await new PreOobeProcessExecutor().RunAsync(new ProcessCommand(
            Environment.GetEnvironmentVariable("ComSpec")!, ["/d", "/c", "ping -n 30 127.0.0.1 >nul"],
            Path.GetTempPath(), TimeSpan.FromMilliseconds(200)), TestContext.Current.CancellationToken);
        Assert.True(result.TimedOut);
        Assert.True(result.TerminationUncertain);
        Assert.Null(result.ExitCode);
    }

    [Theory]
    [InlineData("plain", "\"plain\"")]
    [InlineData("C:\\folder with spaces\\", "\"C:\\folder with spaces\\\\\"")]
    [InlineData("a\"b", "\"a\\\"b\"")]
    public void QuoteWindowsArgument_PreservesTokenBoundary(string token, string expected) =>
        Assert.Equal(expected, WindowsArguments.Quote(token));
}
