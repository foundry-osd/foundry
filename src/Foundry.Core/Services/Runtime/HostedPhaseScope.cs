// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Telemetry;
using Foundry.Utilities.Diagnostics;
using Serilog;
using Serilog.Events;

namespace Foundry.Core.Services.Runtime;

/// <summary>
/// The framework-neutral part of a phase hosted by the Foundry.WinPE UI process (Foundry Connect, Foundry Deploy): it points
/// <see cref="Log.Logger"/> at the phase's own log file while the phase runs, and ends the phase in a fixed order.
/// </summary>
/// <remarks>
/// <see cref="EndAsync"/> flushes the phase's product analytics, disposes the phase's DI container, restores the logger that
/// was current before <see cref="Begin"/> and closes the phase's log, each within <see cref="ShutdownBudget"/> and never
/// throwing: teardown failures never change the phase result. The WPF parts of a phase (its shell content, its dispatcher exception
/// handler) stay in the phase, because Foundry.Core has no UI dependency. Not thread-safe: begin and end it on one thread.
/// </remarks>
public sealed class HostedPhaseScope
{
    /// <summary>Longest wait for each teardown step that can block on I/O: the telemetry flush and the log close.</summary>
    public static readonly TimeSpan ShutdownBudget = TimeSpan.FromSeconds(2);

    private readonly ILogger _previousLogger;
    private readonly ILogger _phaseLogger;
    private readonly string _phaseName;
    private bool _ended;

    private HostedPhaseScope(ILogger previousLogger, ILogger phaseLogger, ILogger logger, string logFilePath, string phaseName)
    {
        _previousLogger = previousLogger;
        _phaseLogger = phaseLogger;
        Logger = logger;
        LogFilePath = logFilePath;
        _phaseName = phaseName;
    }

    /// <summary>Gets the phase logger, with the source context given to <see cref="Begin"/>.</summary>
    public ILogger Logger { get; }

    /// <summary>Gets the full path of the phase's log file, or <c>&lt;unavailable&gt;</c> when it fell back to debugger output.</summary>
    public string LogFilePath { get; }

    /// <summary>
    /// Creates the phase's file logger and makes it <see cref="Log.Logger"/>. A file logger that cannot be created falls back
    /// to debugger output plus the remote diagnostics sink, and the failure is logged without the path.
    /// </summary>
    /// <param name="identity">The phase, named in the fallback logger.</param>
    /// <param name="phaseName">The phase name used in the teardown log entries, for example <c>Foundry Connect</c>.</param>
    /// <param name="sourceContext">The source context of <see cref="Logger"/>.</param>
    /// <param name="resolveLogFilePath">Returns the phase's log file path.</param>
    /// <param name="createLogger">Creates the phase's file logger for that path.</param>
    public static HostedPhaseScope Begin(
        RuntimeApplicationIdentity identity,
        string phaseName,
        Type sourceContext,
        Func<string> resolveLogFilePath,
        Func<string, ILogger> createLogger)
    {
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentException.ThrowIfNullOrWhiteSpace(phaseName);
        ArgumentNullException.ThrowIfNull(sourceContext);
        ArgumentNullException.ThrowIfNull(resolveLogFilePath);
        ArgumentNullException.ThrowIfNull(createLogger);

        ILogger previousLogger = Log.Logger;
        string logFilePath;
        ILogger phaseLogger;
        Exception? fileLoggingFailure = null;
        try
        {
            logFilePath = resolveLogFilePath();
            phaseLogger = createLogger(logFilePath);
        }
        catch (Exception exception)
        {
            logFilePath = "<unavailable>";
            phaseLogger = FoundryLogConfiguration.CreateDebugLogger(
                identity.ApplicationId, DiagnosticSessionContext.CurrentSessionId,
                LogEventLevel.Verbose, additionalSink: RemoteDiagnosticsSink.Instance);
            fileLoggingFailure = exception;
        }

        Log.Logger = phaseLogger;
        ILogger logger = phaseLogger.ForContext(sourceContext);
        if (fileLoggingFailure is not null)
        {
            // The exception is not attached: its message can carry the log path, and every level is mirrored remotely.
            logger.Error(
                "File logging initialization failed. Falling back to debugger output. FailureType={FailureType}, ErrorCode={ErrorCode}",
                fileLoggingFailure.GetType().Name, fileLoggingFailure.HResult);
        }

        return new HostedPhaseScope(previousLogger, phaseLogger, logger, logFilePath, phaseName);
    }

    /// <summary>
    /// Ends the phase: flushes <paramref name="telemetry"/>, disposes <paramref name="services"/> on the calling thread when
    /// it completes synchronously, restores the previous <see cref="Log.Logger"/> and closes the phase's log. Later calls do
    /// nothing.
    /// </summary>
    /// <param name="telemetry">The phase's product analytics, or <see langword="null"/> when it was never created.</param>
    /// <param name="services">The phase's DI container, or <see langword="null"/> when it was never built.</param>
    public async Task EndAsync(ITelemetryService? telemetry, IDisposable? services)
    {
        if (_ended)
        {
            return;
        }

        _ended = true;
        await FlushTelemetryAsync(telemetry);
        await DisposeServicesAsync(services);
        Logger.Information("{PhaseName} phase shutdown completed. Closing its log.", _phaseName);
        Log.Logger = _previousLogger;
        await CloseLoggerAsync(_phaseLogger);
    }

    private async Task FlushTelemetryAsync(ITelemetryService? telemetry)
    {
        if (telemetry is null)
        {
            return;
        }

        try
        {
            await Task.Run(() => telemetry.FlushAsync()).WaitAsync(ShutdownBudget);
        }
        catch (Exception exception)
        {
            Logger.ForContext("PostHogTransportInternal", true).Warning(
                "Telemetry flush did not complete within the shutdown budget. FailureType={FailureType}, ErrorCode={ErrorCode}",
                exception.GetType().Name, exception.HResult);
        }
    }

    /// <summary>
    /// Disposes the container on the calling thread (the UI thread for a WPF phase, which owns the view models it disposes)
    /// until its first asynchronous disposal, if any.
    /// </summary>
    private async Task DisposeServicesAsync(IDisposable? services)
    {
        if (services is null)
        {
            return;
        }

        try
        {
            if (services is IAsyncDisposable asyncDisposable)
            {
                await asyncDisposable.DisposeAsync();
            }
            else
            {
                services.Dispose();
            }
        }
        catch (Exception exception)
        {
            Logger.Warning(
                "{PhaseName} service disposal failed. FailureType={FailureType}, ErrorCode={ErrorCode}",
                _phaseName, exception.GetType().Name, exception.HResult);
        }
    }

    private static async Task CloseLoggerAsync(ILogger logger)
    {
        if (logger is not IAsyncDisposable disposable)
        {
            return;
        }

        try
        {
            await Task.Run(() => disposable.DisposeAsync().AsTask()).WaitAsync(ShutdownBudget);
        }
        catch
        {
            // Closing the log is best effort: the phase outcome is already decided.
        }
    }
}
