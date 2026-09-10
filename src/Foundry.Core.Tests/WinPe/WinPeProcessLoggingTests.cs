// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Services.WinPe;
using Foundry.Core.Tests.TestUtilities;
using Serilog;
using Serilog.Core;
using Serilog.Events;

namespace Foundry.Core.Tests.WinPe;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class WinPeProcessLoggingCollection
{
    public const string Name = "WinPeProcessLogging";
}

[Collection(WinPeProcessLoggingCollection.Name)]
public sealed class WinPeProcessLoggingTests : IDisposable
{
    private readonly ILogger _previousLogger = Log.Logger;
    private readonly CapturingSink _sink = new();
    private readonly Logger _logger;

    public WinPeProcessLoggingTests()
    {
        _logger = new LoggerConfiguration().MinimumLevel.Debug().WriteTo.Sink(_sink).CreateLogger();
        Log.Logger = _logger;
    }

    [Theory]
    [InlineData(false, 1)]
    [InlineData(true, 3)]
    public async Task RunAsync_RobocopySuccessfulCopy_LogsDebug(bool extraFile, int expectedExitCode)
    {
        using var workspace = new TemporaryDirectory();
        string source = Directory.CreateDirectory(Path.Combine(workspace.Path, "source")).FullName;
        string destination = Directory.CreateDirectory(Path.Combine(workspace.Path, "destination")).FullName;
        await File.WriteAllTextAsync(Path.Combine(source, "source.txt"), "copy me", TestContext.Current.CancellationToken);
        if (extraFile)
        {
            await File.WriteAllTextAsync(Path.Combine(destination, "extra.txt"), "keep me", TestContext.Current.CancellationToken);
        }

        WinPeProcessExecution result = await new WinPeProcessRunner().RunAsync(
            Path.Combine(Environment.SystemDirectory, "robocopy.exe"),
            $"\"{source}\" \"{destination}\" /E /R:0 /W:0 /NFL /NDL /NJH /NJS /NP",
            workspace.Path,
            TestContext.Current.CancellationToken);

        Assert.Equal(expectedExitCode, result.ExitCode);
        Assert.Equal("copy me", await File.ReadAllTextAsync(Path.Combine(destination, "source.txt"), TestContext.Current.CancellationToken));
        AssertExitLog(LogEventLevel.Debug, expectedExitCode);
    }

    [Fact]
    public async Task RunAsync_RobocopyMissingSource_StillLogsWarning()
    {
        using var workspace = new TemporaryDirectory();
        WinPeProcessExecution result = await new WinPeProcessRunner().RunAsync(
            Path.Combine(Environment.SystemDirectory, "robocopy.exe"),
            $"\"{Path.Combine(workspace.Path, "missing")}\" \"{Path.Combine(workspace.Path, "destination")}\" /R:0 /W:0",
            workspace.Path,
            TestContext.Current.CancellationToken);

        Assert.True(result.ExitCode >= 8);
        AssertExitLog(LogEventLevel.Warning, result.ExitCode);
    }

    [Theory]
    [InlineData("MakeWinPEMedia.cmd", "/?", 1, "echo /bootex", LogEventLevel.Debug)]
    [InlineData("MakeWinPEMedia.cmd", "/?", 1, "echo /bootex 1>&2", LogEventLevel.Debug)]
    [InlineData("MakeWinPEMedia.cmd", "/?", 1, "echo unavailable", LogEventLevel.Warning)]
    [InlineData("MakeWinPEMedia.cmd", "/?", 2, "echo /bootex", LogEventLevel.Warning)]
    [InlineData("MakeWinPEMedia.cmd", "/ISO", 1, "echo /bootex", LogEventLevel.Warning)]
    [InlineData("other.cmd", "/?", 1, "echo /bootex", LogEventLevel.Warning)]
    public async Task RunCmdScriptDirectAsync_OnlySupportedHelpProbeIsDebug(
        string scriptName, string arguments, int exitCode, string outputCommand, LogEventLevel expectedLevel)
    {
        using var workspace = new TemporaryDirectory();
        string script = Path.Combine(workspace.Path, scriptName);
        await File.WriteAllTextAsync(script, $"@echo off\r\n{outputCommand}\r\nexit /b {exitCode}\r\n", TestContext.Current.CancellationToken);

        WinPeProcessExecution result = await new WinPeProcessRunner().RunCmdScriptDirectAsync(
            script, arguments, workspace.Path, TestContext.Current.CancellationToken);

        Assert.Equal(exitCode, result.ExitCode);
        AssertExitLog(expectedLevel, exitCode);
    }

    [Fact]
    public async Task RunAsync_OrdinaryCommandFailure_StillLogsWarning()
    {
        using var workspace = new TemporaryDirectory();
        WinPeProcessExecution result = await new WinPeProcessRunner().RunAsync(
            Path.Combine(Environment.SystemDirectory, "cmd.exe"), "/d /c exit /b 1", workspace.Path, TestContext.Current.CancellationToken);

        Assert.Equal(1, result.ExitCode);
        AssertExitLog(LogEventLevel.Warning, 1);
    }

    private void AssertExitLog(LogEventLevel expectedLevel, int exitCode)
    {
        LogEvent logEvent = Assert.Single(_sink.Events);
        Assert.Equal(expectedLevel, logEvent.Level);
        Assert.Equal(exitCode, Assert.IsType<ScalarValue>(logEvent.Properties["ExitCode"]).Value);
        Assert.Equal(true, Assert.IsType<ScalarValue>(logEvent.Properties["ProcessOutputOmitted"]).Value);
        Assert.False(logEvent.Properties.ContainsKey("ProcessStdout"));
        Assert.False(logEvent.Properties.ContainsKey("ProcessStderr"));
    }

    public void Dispose()
    {
        Log.Logger = _previousLogger;
        _logger.Dispose();
    }

    private sealed class CapturingSink : ILogEventSink
    {
        public List<LogEvent> Events { get; } = [];

        public void Emit(LogEvent logEvent) => Events.Add(logEvent);
    }
}
