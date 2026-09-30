// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Utilities.Processes;
using Foundry.Utilities.Tests.IO;
using System.Diagnostics;

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
    public async Task RunAsync_WhenCanceled_PreservesCapturedStreamsAndConfirmedRootExit()
    {
        using var workspace = new TemporaryDirectory();
        var outputReady = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var errorReady = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        ProcessExecutionRequest request = ProcessExecutionRequest.FromRawArguments(
            GetCommandProcessor(),
            "/d /s /c \"(echo progress) & (echo warning) 1>&2 & ping 127.0.0.1 -n 30 >nul\"",
            workspace.Path) with
        {
            OnOutputData = _ => outputReady.TrySetResult(),
            OnErrorData = _ => errorReady.TrySetResult()
        };
        using var cancellation = new CancellationTokenSource();
        Task<ProcessExecutionResult> execution = new ProcessRunner().RunAsync(request, cancellation.Token);
        await Task.WhenAll(outputReady.Task, errorReady.Task)
            .WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        cancellation.Cancel();

        ProcessCanceledException exception = await Assert.ThrowsAsync<ProcessCanceledException>(() => execution);

        Assert.Equal("progress", exception.StandardOutput.Trim());
        Assert.Equal("warning", exception.StandardError.Trim());
        Assert.True(exception.ProcessExitConfirmed);
        Assert.NotNull(exception.ExitCode);
        Assert.Equal(cancellation.Token, exception.CancellationToken);
        Assert.DoesNotContain("progress", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunAsync_WhenCanceledAfterRootExit_DoesNotWaitForInheritedOutputPipe()
    {
        using var workspace = new TemporaryDirectory();
        string childScript = Path.Combine(workspace.Path, "child.cmd");
        string rootScript = Path.Combine(workspace.Path, "start-child.ps1");
        string releaseRoot = Path.Combine(workspace.Path, "release-root");
        string releaseChild = Path.Combine(workspace.Path, "release-child");
        TimeSpan watchdog = TimeSpan.FromSeconds(15);
        await File.WriteAllTextAsync(
            childScript,
            """
            @echo off
            echo child-ready
            :wait
            if not exist "%FOUNDRY_PIPE_WORKSPACE%" exit /b 0
            if exist "%FOUNDRY_PIPE_RELEASE_CHILD%" exit /b 0
            ping.exe 127.0.0.1 -n 2 >nul
            goto wait
            """,
            TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(rootScript,
            """
            $ErrorActionPreference = 'Stop'
            $info = [System.Diagnostics.ProcessStartInfo]::new($env:ComSpec)
            $info.Arguments = '/d /s /c call "' + $env:FOUNDRY_PIPE_CHILD_SCRIPT + '"'
            $info.UseShellExecute = $false
            $info.CreateNoWindow = $true
            # Redirecting input enables explicit inheritance of the unchanged output/error handles.
            $info.RedirectStandardInput = $true
            $child = [System.Diagnostics.Process]::Start($info)
            Write-Output "child-pid:$($child.Id)"
            Write-Output "root-pid:$PID"
            while (-not (Test-Path -LiteralPath $env:FOUNDRY_PIPE_RELEASE_ROOT)) {
                Start-Sleep -Milliseconds 50
            }
            """, TestContext.Current.CancellationToken);
        var rootStarted = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var childStarted = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var childReady = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var request = new ProcessExecutionRequest(
            Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe"),
            ["-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", rootScript],
            workspace.Path) with
        {
            EnvironmentOverrides = new Dictionary<string, string?>
            {
                ["FOUNDRY_PIPE_CHILD_SCRIPT"] = childScript,
                ["FOUNDRY_PIPE_RELEASE_ROOT"] = releaseRoot,
                ["FOUNDRY_PIPE_RELEASE_CHILD"] = releaseChild,
                ["FOUNDRY_PIPE_WORKSPACE"] = workspace.Path
            },
            OnOutputData = line =>
            {
                if (line.StartsWith("root-pid:", StringComparison.Ordinal) && int.TryParse(line.AsSpan(9), out int rootPid))
                    rootStarted.TrySetResult(rootPid);
                if (line.StartsWith("child-pid:", StringComparison.Ordinal) && int.TryParse(line.AsSpan(10), out int childPid))
                    childStarted.TrySetResult(childPid);
                if (line.Equals("child-ready", StringComparison.Ordinal)) childReady.TrySetResult();
            }
        };
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        Task<ProcessExecutionResult> executionTask = new ProcessRunner().RunAsync(request, cancellation.Token);
        Process? child = null;
        try
        {
            int rootPid = await rootStarted.Task.WaitAsync(watchdog, TestContext.Current.CancellationToken);
            int childPid = await childStarted.Task.WaitAsync(watchdog, TestContext.Current.CancellationToken);
            using var root = Process.GetProcessById(rootPid);
            child = Process.GetProcessById(childPid);
            await childReady.Task.WaitAsync(watchdog, TestContext.Current.CancellationToken);

            // Confirm root exit while the child keeps the inherited stream open until finally releases it.
            await File.WriteAllTextAsync(releaseRoot, "release", TestContext.Current.CancellationToken);
            await root.WaitForExitAsync(TestContext.Current.CancellationToken).WaitAsync(watchdog, TestContext.Current.CancellationToken);
            Assert.Equal(0, root.ExitCode);
            Assert.False(child.HasExited);
            Assert.False(executionTask.IsCompleted);
            cancellation.Cancel();

            ProcessCanceledException exception = await Assert.ThrowsAsync<ProcessCanceledException>(() =>
                executionTask.WaitAsync(watchdog, TestContext.Current.CancellationToken));

            Assert.True(exception.ProcessExitConfirmed);
            Assert.Equal(0, exception.ExitCode);
            Assert.Contains("child-ready", exception.StandardOutput, StringComparison.Ordinal);
            Assert.Equal(cancellation.Token, exception.CancellationToken);
            Assert.False(child.HasExited, "Cancellation must complete before the child releases its output pipe.");
        }
        finally
        {
            File.WriteAllText(releaseRoot, "release");
            File.WriteAllText(releaseChild, "release");
            cancellation.Cancel();
            try
            {
                if (child is not null)
                {
                    using (child)
                    {
                        using var cleanup = new CancellationTokenSource(watchdog);
                        try { await child.WaitForExitAsync(cleanup.Token); }
                        catch (OperationCanceledException) when (cleanup.IsCancellationRequested)
                        {
                            if (!child.HasExited) child.Kill(entireProcessTree: true);
                            using var killed = new CancellationTokenSource(watchdog);
                            await child.WaitForExitAsync(killed.Token);
                            throw;
                        }
                    }
                }
            }
            finally
            {
                using var observation = new CancellationTokenSource(watchdog);
                try { await executionTask.WaitAsync(watchdog, observation.Token); }
                catch (OperationCanceledException) when (!observation.IsCancellationRequested) { }
            }
        }
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
