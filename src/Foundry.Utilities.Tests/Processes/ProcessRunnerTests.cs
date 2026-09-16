// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Utilities.Processes;
using Foundry.Utilities.Tests.IO;

namespace Foundry.Utilities.Tests.Processes;

public sealed class ProcessRunnerTests
{
    [Fact]
    public async Task RunAsync_WithArgumentList_PreservesWhitespaceInArgument()
    {
        using var workspace = new TemporaryDirectory();
        string searchRoot = Path.Combine(workspace.Path, "folder with spaces");
        Directory.CreateDirectory(searchRoot);
        string markerPath = Path.Combine(searchRoot, "marker.txt");
        string searchArgument = searchRoot + Path.DirectorySeparatorChar;
        await File.WriteAllTextAsync(markerPath, "marker", TestContext.Current.CancellationToken);
        var request = new ProcessExecutionRequest(
            Path.Combine(Environment.SystemDirectory, "where.exe"),
            ["/R", searchArgument, "marker.txt"],
            workspace.Path);

        ProcessExecutionResult result = await new ProcessRunner().RunAsync(request, TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal(markerPath, result.StandardOutput.Trim(), ignoreCase: true);
        Assert.Equal($"/R \"{searchArgument}\\\" marker.txt", result.Arguments);
    }

    [Fact]
    public async Task RunAsync_WithQuoteInArgument_EscapesDiagnosticDisplay()
    {
        using var workspace = new TemporaryDirectory();
        var request = new ProcessExecutionRequest(
            GetCommandProcessor(),
            ["/d", "/s", "/c", "echo", "value \"quoted\""],
            workspace.Path);

        ProcessExecutionResult result = await new ProcessRunner().RunAsync(request, TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal("/d /s /c echo \"value \\\"quoted\\\"\"", result.Arguments);
    }

    [Fact]
    public async Task RunAsync_WithRawArguments_CapturesBothStreamsAndNonZeroExit()
    {
        using var workspace = new TemporaryDirectory();
        ProcessExecutionRequest request = ProcessExecutionRequest.FromRawArguments(
            GetCommandProcessor(),
            "/d /s /c \"echo stdout & echo stderr 1>&2 & exit /b 7\"",
            workspace.Path);

        ProcessExecutionResult result = await new ProcessRunner().RunAsync(request, TestContext.Current.CancellationToken);

        Assert.False(result.IsSuccess);
        Assert.Equal(7, result.ExitCode);
        Assert.Equal("stdout", result.StandardOutput.Trim());
        Assert.Equal("stderr", result.StandardError.Trim());
        Assert.Equal(request.RawArguments, result.Arguments);
    }

    [Fact]
    public async Task RunAsync_WhenCallbacksThrow_StillCapturesOutput()
    {
        using var workspace = new TemporaryDirectory();
        var errorLines = new List<string>();
        ProcessExecutionRequest request = ProcessExecutionRequest.FromRawArguments(
            GetCommandProcessor(),
            "/d /s /c \"(echo stdout) & (echo stderr) 1>&2\"",
            workspace.Path) with
        {
            OnOutputData = _ => throw new InvalidOperationException("callback failure"),
            OnErrorData = errorLines.Add
        };

        ProcessExecutionResult result = await new ProcessRunner().RunAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal("stdout", result.StandardOutput.Trim());
        Assert.Equal("stderr", result.StandardError.Trim());
        Assert.Equal(["stderr"], errorLines);
    }

    [Fact]
    public async Task RunAsync_CreatesAndUsesWorkingDirectory()
    {
        using var workspace = new TemporaryDirectory();
        string workingDirectory = Path.Combine(workspace.Path, "created", "nested");
        ProcessExecutionRequest request = ProcessExecutionRequest.FromRawArguments(
            GetCommandProcessor(),
            "/d /s /c cd",
            workingDirectory);

        ProcessExecutionResult result = await new ProcessRunner().RunAsync(request, TestContext.Current.CancellationToken);

        Assert.True(Directory.Exists(workingDirectory));
        Assert.Equal(workingDirectory, result.StandardOutput.Trim(), ignoreCase: true);
        Assert.Equal(workingDirectory, result.WorkingDirectory);
    }

    [Fact]
    public async Task RunAsync_AppliesEnvironmentOverrides()
    {
        using var workspace = new TemporaryDirectory();
        ProcessExecutionRequest request = ProcessExecutionRequest.FromRawArguments(
            GetCommandProcessor(),
            "/d /s /c echo %FOUNDRY_PROCESS_TEST%",
            workspace.Path) with
        {
            EnvironmentOverrides = new Dictionary<string, string?>
            {
                ["FOUNDRY_PROCESS_TEST"] = "expected value"
            }
        };

        ProcessExecutionResult result = await new ProcessRunner().RunAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal("expected value", result.StandardOutput.Trim());
    }

    [Fact]
    public async Task RunAsync_WithPreCanceledToken_DoesNotCreateWorkingDirectory()
    {
        using var workspace = new TemporaryDirectory();
        string workingDirectory = Path.Combine(workspace.Path, "must-not-exist");
        ProcessExecutionRequest request = ProcessExecutionRequest.FromRawArguments(
            GetCommandProcessor(),
            "/d /s /c echo should-not-run",
            workingDirectory);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new ProcessRunner().RunAsync(request, cancellation.Token));

        Assert.False(Directory.Exists(workingDirectory));
    }

    [Fact]
    public async Task RunAsync_WhenCanceledAfterRootExit_DoesNotWaitForInheritedOutputPipe()
    {
        using var workspace = new TemporaryDirectory();
        string scriptPath = Path.Combine(workspace.Path, "start-child.cmd");
        await File.WriteAllTextAsync(
            scriptPath,
            "@echo off\r\n" +
            "start \"\" /b ping.exe 127.0.0.1 -n 5\r\n" +
            "echo child-ready\r\n" +
            "exit /b 0\r\n",
            TestContext.Current.CancellationToken);
        var childReady = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        ProcessExecutionRequest request = ProcessExecutionRequest.FromRawArguments(
            GetCommandProcessor(),
            $"/d /s /c call \"{scriptPath}\"",
            Environment.SystemDirectory) with
        {
            OnOutputData = line =>
            {
                if (line.Equals("child-ready", StringComparison.Ordinal))
                {
                    childReady.TrySetResult(true);
                }
            }
        };
        using var cancellation = new CancellationTokenSource();
        Task<ProcessExecutionResult> executionTask = new ProcessRunner().RunAsync(request, cancellation.Token);
        await childReady.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        await Task.Delay(TimeSpan.FromMilliseconds(500), TestContext.Current.CancellationToken);
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => executionTask);

        Assert.True(
            stopwatch.Elapsed < TimeSpan.FromSeconds(1),
            $"Cancellation took {stopwatch.Elapsed} while a child process held the output pipe open.");
    }

    [Fact]
    public async Task RunAsync_WhenExecutableCannotStart_ThrowsProcessStartException()
    {
        using var workspace = new TemporaryDirectory();
        var request = new ProcessExecutionRequest(
            Path.Combine(workspace.Path, "missing-executable.exe"),
            [],
            workspace.Path);

        ProcessStartException exception = await Assert.ThrowsAsync<ProcessStartException>(() =>
            new ProcessRunner().RunAsync(request, TestContext.Current.CancellationToken));

        Assert.Equal(request.FileName, exception.FileName);
        Assert.NotNull(exception.InnerException);
        Assert.NotNull(exception.NativeErrorCode);
    }

    [Theory]
    [InlineData("", "working")]
    [InlineData("   ", "working")]
    [InlineData("cmd.exe", "")]
    [InlineData("cmd.exe", "   ")]
    public async Task RunAsync_WithBlankRequiredValue_ThrowsArgumentException(string fileName, string workingDirectory)
    {
        var request = new ProcessExecutionRequest(fileName, [], workingDirectory);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            new ProcessRunner().RunAsync(request, TestContext.Current.CancellationToken));
    }

    [Fact]
    public void ToDiagnosticText_IncludesCommandLocationExitCodeAndNonEmptyStreams()
    {
        var result = new ProcessExecutionResult
        {
            ExitCode = 5,
            FileName = "tool.exe",
            Arguments = "--flag value",
            WorkingDirectory = @"C:\work",
            StandardOutput = " output ",
            StandardError = " error "
        };

        Assert.Equal(
            "Command: tool.exe --flag value\r\n" +
            "WorkingDirectory: C:\\work\r\n" +
            "ExitCode: 5\r\n" +
            "StdOut:\r\n" +
            "output\r\n" +
            "StdErr:\r\n" +
            "error",
            result.ToDiagnosticText());
    }

    private static string GetCommandProcessor()
    {
        string? commandProcessor = Environment.GetEnvironmentVariable("ComSpec");
        return string.IsNullOrWhiteSpace(commandProcessor) ? @"C:\Windows\System32\cmd.exe" : commandProcessor;
    }
}
