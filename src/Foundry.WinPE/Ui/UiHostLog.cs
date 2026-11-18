// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Services.Runtime;
using Foundry.Telemetry;
using Foundry.Utilities.Diagnostics;
using Foundry.Utilities.IO;
using Serilog;
using Serilog.Core;
using Serilog.Events;

namespace Foundry.WinPE.Ui;

/// <summary>
/// The UI process's own log, outside the phases: it writes to the log file of the phase the session is in,
/// <see cref="ConnectLogFileName"/> until the Deploy phase and <see cref="DeployLogFileName"/> from it, so the log file names
/// stay those of the former Connect and Deploy processes. System preparation therefore logs to the Connect file.
/// </summary>
/// <remarks>
/// While a phase runs, its own logger replaces <see cref="Log.Logger"/> and writes to the same shared file; the phase restores
/// this logger when it ends. Loggers from <see cref="CreateLogger"/> follow <see cref="Log.Logger"/>, so they keep working
/// across file switches. Not thread-safe: switch on the UI thread.
/// </remarks>
internal static class UiHostLog
{
    /// <summary>The file of the Connect phase and of the UI process before Deploy.</summary>
    internal const string ConnectLogFileName = "FoundryConnect.log";

    /// <summary>The file of the Deploy phase and of the UI process from Deploy on.</summary>
    internal const string DeployLogFileName = "FoundryDeploy.log";

    private const string ApplicationName = "Foundry.WinPE";
    private const string ApplicationProperty = "Application";
    private const int RetainedLogFileCount = 5;

    private static ILogger? _fileLogger;
    private static string? _fileName;

    /// <summary>Gets the directory of the current log file, or <see langword="null"/> when logging fell back to debugger output.</summary>
    internal static string? LogDirectory { get; private set; }

    /// <summary>
    /// Points <see cref="Log.Logger"/> at <paramref name="fileName"/> and closes the previous host log; a file that cannot be
    /// opened falls back to debugger output. Calling it again with the same file does nothing.
    /// </summary>
    internal static void Use(string fileName)
    {
        if (_fileName == fileName)
        {
            return;
        }

        ILogger? previous = _fileLogger;
        Exception? failure = null;
        try
        {
            string path = WritableFilePathResolver.Resolve(
                [WinPeWorkspaceLayout.LogsDirectory, Path.Combine(Path.GetTempPath(), "Foundry", "Logs"), AppContext.BaseDirectory],
                fileName);
            LogDirectory = Path.GetDirectoryName(Path.GetFullPath(path));
            RemoteDiagnosticsSink.SetLogDirectory(Path.Combine(LogDirectory!, "PendingLogs"));
            _fileLogger = FoundryLogConfiguration.CreateFileLogger(path, ApplicationName,
                DiagnosticSessionContext.CurrentSessionId, LogEventLevel.Verbose, RetainedLogFileCount,
                additionalSink: RemoteDiagnosticsSink.Instance);
        }
        catch (Exception exception)
        {
            LogDirectory = null;
            _fileLogger = FoundryLogConfiguration.CreateDebugLogger(ApplicationName, DiagnosticSessionContext.CurrentSessionId,
                LogEventLevel.Verbose, additionalSink: RemoteDiagnosticsSink.Instance);
            failure = exception;
        }

        _fileName = fileName;
        Log.Logger = _fileLogger;
        (previous as IDisposable)?.Dispose();
        if (failure is not null)
        {
            // The exception is not attached: its message can carry the log path, and every level is mirrored remotely.
            Log.ForContext(typeof(UiHostLog)).Error(
                "File logging initialization failed. Falling back to debugger output. FailureType={FailureType}, ErrorCode={ErrorCode}",
                failure.GetType().Name, failure.HResult);
        }
    }

    /// <summary>
    /// Creates a logger for <paramref name="sourceContext"/> that writes to whatever <see cref="Log.Logger"/> is current. Its
    /// entries keep the <c>Foundry.WinPE</c> application name, also while a phase logger is current, because enrichers never
    /// replace a property an event already carries.
    /// </summary>
    internal static ILogger CreateLogger(Type sourceContext) => new LoggerConfiguration()
        .MinimumLevel.Verbose()
        .Enrich.WithProperty(ApplicationProperty, ApplicationName)
        .WriteTo.Sink(new CurrentLoggerSink())
        .CreateLogger()
        .ForContext(sourceContext);

    /// <summary>Closes the host log within <see cref="HostedPhaseScope.ShutdownBudget"/>.</summary>
    internal static void Close()
    {
        ILogger? logger = _fileLogger;
        _fileLogger = null;
        _fileName = null;
        Log.Logger = Serilog.Core.Logger.None;
        if (logger is not IAsyncDisposable disposable)
        {
            return;
        }

        try { Task.Run(() => disposable.DisposeAsync().AsTask()).Wait(HostedPhaseScope.ShutdownBudget); }
        catch { }
    }

    private sealed class CurrentLoggerSink : ILogEventSink
    {
        public void Emit(LogEvent logEvent) => Log.Logger.Write(logEvent);
    }
}
