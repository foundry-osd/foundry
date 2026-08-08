// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.ComponentModel;
using Foundry.Core.Services.WinPe;
using Foundry.Core.Tests.TestUtilities;

namespace Foundry.Core.Tests.WinPe;

public sealed class WinPeProcessRunnerTests
{
    [Fact]
    public async Task RunWithOutputAsync_PreservesRawExecutionAndCallbacks()
    {
        using var workspace = new TemporaryDirectory();
        var outputLines = new List<string>();
        var errorLines = new List<string>();
        const string arguments = "/d /s /c \"echo stdout & echo stderr 1>&2 & exit /b 7\"";

        WinPeProcessExecution result = await new WinPeProcessRunner().RunWithOutputAsync(
            GetCommandProcessor(),
            arguments,
            workspace.Path,
            outputLines.Add,
            errorLines.Add,
            TestContext.Current.CancellationToken);

        Assert.Equal(7, result.ExitCode);
        Assert.Equal(arguments, result.Arguments);
        Assert.Equal("stdout", result.StandardOutput.Trim());
        Assert.Equal("stderr", result.StandardError.Trim());
        Assert.Equal(["stdout "], outputLines);
        Assert.Equal(["stderr  "], errorLines);
    }

    [Fact]
    public async Task RunAsync_FiltersReservedEnvironmentOverrides()
    {
        using var workspace = new TemporaryDirectory();
        string normalName = $"CORE_PROCESS_{Guid.NewGuid():N}";
        string reservedName = $"FOUNDRY_CORE_PROCESS_{Guid.NewGuid():N}";
        string arguments = $"/d /s /c \"echo %{normalName}% & if defined {reservedName} (echo leaked) else echo filtered\"";
        var environment = new Dictionary<string, string>
        {
            [normalName] = "normal-value",
            [reservedName] = "reserved-value"
        };

        WinPeProcessExecution result = await new WinPeProcessRunner().RunAsync(
            GetCommandProcessor(),
            arguments,
            workspace.Path,
            TestContext.Current.CancellationToken,
            environment);

        Assert.True(result.IsSuccess);
        Assert.Contains("normal-value", result.StandardOutput, StringComparison.Ordinal);
        Assert.Contains("filtered", result.StandardOutput, StringComparison.Ordinal);
        Assert.DoesNotContain("leaked", result.StandardOutput, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunAsync_WithPreCanceledToken_DoesNotCreateWorkingDirectory()
    {
        using var workspace = new TemporaryDirectory();
        string workingDirectory = Path.Combine(workspace.Path, "must-not-exist");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new WinPeProcessRunner().RunAsync(
                GetCommandProcessor(),
                "/d /c exit",
                workingDirectory,
                cancellation.Token));

        Assert.False(Directory.Exists(workingDirectory));
    }

    [Fact]
    public async Task RunAsync_WhenExecutableCannotStart_PreservesWin32Exception()
    {
        using var workspace = new TemporaryDirectory();
        string executablePath = Path.Combine(workspace.Path, "missing.exe");

        await Assert.ThrowsAsync<Win32Exception>(() =>
            new WinPeProcessRunner().RunAsync(
                executablePath,
                string.Empty,
                workspace.Path,
                TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData(false, "/d /s /c \"call {0} value\"")]
    [InlineData(true, "/d /c \"{0} value\"")]
    public async Task RunCmdScriptAsync_PreservesCommandConstruction(bool direct, string expectedArgumentsFormat)
    {
        using var workspace = new TemporaryDirectory();
        string scriptPath = Path.Combine(workspace.Path, "script with spaces.cmd");
        await File.WriteAllTextAsync(
            scriptPath,
            "@echo off\r\necho script:%~1\r\n",
            TestContext.Current.CancellationToken);
        var runner = new WinPeProcessRunner();

        WinPeProcessExecution result = direct
            ? await runner.RunCmdScriptDirectAsync(
                scriptPath,
                "value",
                workspace.Path,
                TestContext.Current.CancellationToken)
            : await runner.RunCmdScriptAsync(
                scriptPath,
                "value",
                workspace.Path,
                TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal(
            string.Format(expectedArgumentsFormat, WinPeProcessRunner.Quote(scriptPath)),
            result.Arguments);
        Assert.Equal("script:value", result.StandardOutput.Trim());
    }

    private static string GetCommandProcessor()
    {
        string? commandProcessor = Environment.GetEnvironmentVariable("ComSpec");
        return string.IsNullOrWhiteSpace(commandProcessor) ? @"C:\Windows\System32\cmd.exe" : commandProcessor;
    }
}
