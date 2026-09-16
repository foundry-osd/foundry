// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Runtime.InteropServices;
using System.Text;
using Foundry.Utilities.Processes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using DeployProcessRunner = Foundry.Deploy.Services.System.ProcessRunner;
using UtilityProcessRunner = Foundry.Utilities.Processes.ProcessRunner;

namespace Foundry.Deploy.Tests;

public sealed class ProcessRunnerAdapterTests
{
    [Fact]
    public async Task RunAsync_WithRawPowerShellCommand_PreservesArgumentsAndOutput()
    {
        using var workspace = new TemporaryDirectory();
        string encodedCommand = Convert.ToBase64String(
            Encoding.Unicode.GetBytes("[Console]::OutputEncoding = New-Object System.Text.UTF8Encoding($false); [Console]::Out.WriteLine('Éducation 日本語')"));
        string arguments = $"-NoLogo -NoProfile -NonInteractive -EncodedCommand {encodedCommand}";

        ProcessExecutionResult result = await CreateRunner().RunAsync(
            GetPowerShellPath(),
            arguments,
            workspace.Path,
            TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal(arguments, result.Arguments);
        Assert.Equal("Éducation 日本語", result.StandardOutput.Trim());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RunAsync_Dism_DecodesOemBytesInBothStreams(bool useArgumentList)
    {
        using var workspace = new TemporaryDirectory();
        // This executable only replays fixture bytes; no DISM or image operation runs.
        string executablePath = Path.Combine(workspace.Path, "DiSm.ExE");
        File.Copy(GetCommandProcessor(), executablePath);
        string outputPath = Path.Combine(workspace.Path, "output.txt");
        Encoding encoding = CodePagesEncodingProvider.Instance.GetEncoding((int)GetOEMCP())
            ?? Encoding.GetEncoding((int)GetOEMCP());
        const string fixture = "Size : 26\u00A0839\u00A0601\u00A0777 bytes; Éducation 日本語";
        byte[] outputBytes = encoding.GetBytes(fixture + "\r\n");
        // Some OEM pages cannot represent NBSP; expect the text actually emitted by the fixture.
        string expected = encoding.GetString(outputBytes).Trim();
        await File.WriteAllBytesAsync(outputPath, outputBytes, TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(Path.Combine(workspace.Path, "emit.cmd"),
            "@echo off\r\ntype output.txt\r\ntype output.txt 1>&2\r\n", TestContext.Current.CancellationToken);
        var outputLines = new List<string>();
        var errorLines = new List<string>();

        ProcessExecutionResult result = useArgumentList
            ? await CreateRunner().RunAsync(executablePath, ["/d", "/c", "call", "emit.cmd"], workspace.Path,
                outputLines.Add, errorLines.Add, TestContext.Current.CancellationToken)
            : await CreateRunner().RunAsync(executablePath, "/d /c call emit.cmd", workspace.Path,
                TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal(expected, result.StandardOutput.Trim());
        Assert.Equal(expected, result.StandardError.Trim());
        if (useArgumentList)
        {
            Assert.Equal([expected], outputLines);
            Assert.Equal([expected], errorLines);
        }
    }

    [Fact]
    public async Task RunAsync_WithArgumentList_PreservesWhitespaceInArgument()
    {
        using var workspace = new TemporaryDirectory();
        string searchRoot = Path.Combine(workspace.Path, "folder with spaces");
        Directory.CreateDirectory(searchRoot);
        string markerPath = Path.Combine(searchRoot, "marker.txt");
        await File.WriteAllTextAsync(markerPath, "marker", TestContext.Current.CancellationToken);

        ProcessExecutionResult result = await CreateRunner().RunAsync(
            Path.Combine(Environment.SystemDirectory, "where.exe"),
            ["/R", searchRoot, "marker.txt"],
            workspace.Path,
            TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal(markerPath, result.StandardOutput.Trim(), ignoreCase: true);
    }

    [Fact]
    public async Task RunAsync_WithNonZeroExit_ReturnsCapturedResult()
    {
        using var workspace = new TemporaryDirectory();
        var logger = new RecordingLogger<DeployProcessRunner>();
        var runner = new DeployProcessRunner(new UtilityProcessRunner(), logger);

        ProcessExecutionResult result = await runner.RunAsync(
            GetCommandProcessor(),
            "/d /s /c \"echo stdout & echo stderr 1>&2 & exit /b 7\"",
            workspace.Path,
            TestContext.Current.CancellationToken);

        Assert.Equal(7, result.ExitCode);
        Assert.Equal("stdout", result.StandardOutput.Trim());
        Assert.Equal("stderr", result.StandardError.Trim());
        Assert.Equal(1, logger.WarningCount);
        Assert.Equal(7, logger.LastScope["ExitCode"]);
        Assert.Equal("cmd.exe", logger.LastScope["ToolName"]);
        Assert.True(Convert.ToDouble(logger.LastScope["ProcessDurationMs"]) >= 0);
        Assert.DoesNotContain("Arguments", logger.LastScope.Keys);
    }

    [Fact]
    public async Task RunAsync_WhenExecutableCannotStart_ThrowsSharedException()
    {
        using var workspace = new TemporaryDirectory();
        string executablePath = Path.Combine(workspace.Path, "missing.exe");
        var logger = new RecordingLogger<DeployProcessRunner>();
        var runner = new DeployProcessRunner(new UtilityProcessRunner(), logger);

        ProcessStartException exception = await Assert.ThrowsAsync<ProcessStartException>(() =>
            runner.RunAsync(
                executablePath,
                [],
                workspace.Path,
                TestContext.Current.CancellationToken));

        Assert.Equal(executablePath, exception.FileName);
        Assert.NotNull(exception.NativeErrorCode);
        Assert.Equal(1, logger.WarningCount);
        Assert.Equal("missing.exe", logger.LastScope["ToolName"]);
        Assert.Equal("process_start_failed", logger.LastScope["FailureReason"]);
        Assert.Equal(exception.NativeErrorCode.Value, logger.LastScope["FailureCode"]);
        Assert.DoesNotContain("ExitCode", logger.LastScope.Keys);
    }

    [Fact]
    public async Task RunAsync_WhenOutputCallbackThrows_LogsWarningAndReturnsCapturedOutput()
    {
        using var workspace = new TemporaryDirectory();
        string markerPath = Path.Combine(workspace.Path, "marker.txt");
        await File.WriteAllTextAsync(markerPath, "marker", TestContext.Current.CancellationToken);
        var logger = new RecordingLogger<DeployProcessRunner>();
        var runner = new DeployProcessRunner(new UtilityProcessRunner(), logger);

        ProcessExecutionResult result = await runner.RunAsync(
            Path.Combine(Environment.SystemDirectory, "where.exe"),
            ["/R", workspace.Path, "marker.txt"],
            workspace.Path,
            _ => throw new InvalidOperationException("callback failure"),
            onErrorData: null,
            TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal(markerPath, result.StandardOutput.Trim(), ignoreCase: true);
        Assert.Equal(1, logger.WarningCount);
    }

    private static DeployProcessRunner CreateRunner()
    {
        return new DeployProcessRunner(
            new UtilityProcessRunner(),
            NullLogger<DeployProcessRunner>.Instance);
    }

    [DllImport("kernel32.dll", ExactSpelling = true)]
    private static extern uint GetOEMCP();

    private static string GetCommandProcessor()
    {
        string? commandProcessor = Environment.GetEnvironmentVariable("ComSpec");
        return string.IsNullOrWhiteSpace(commandProcessor) ? @"C:\Windows\System32\cmd.exe" : commandProcessor;
    }

    private static string GetPowerShellPath()
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            "System32",
            "WindowsPowerShell",
            "v1.0",
            "powershell.exe");
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = Directory.CreateTempSubdirectory("FoundryDeployProcessRunner-").FullName;
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }

    private sealed class RecordingLogger<T> : ILogger<T>
    {
        public int WarningCount { get; private set; }
        public IReadOnlyDictionary<string, object> LastScope { get; private set; } = new Dictionary<string, object>();

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull
        {
            if (state is IReadOnlyDictionary<string, object> properties)
            {
                LastScope = properties;
            }
            return null;
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return true;
        }

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (logLevel == LogLevel.Warning)
            {
                WarningCount++;
            }
        }
    }
}
