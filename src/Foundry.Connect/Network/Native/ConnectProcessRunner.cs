// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.IO;
using Foundry.Telemetry;
using Foundry.Utilities.Processes;
using Microsoft.Extensions.Logging;

namespace Foundry.Connect.Network.Native;

/// <summary>
/// How an external process ended.
/// </summary>
public enum ConnectProcessStatus
{
    /// <summary>The process ran to completion; see <see cref="ConnectProcessResult.ExitCode"/>.</summary>
    Completed,

    /// <summary>The timeout elapsed and the process tree was stopped.</summary>
    TimedOut,

    /// <summary>The process could not be started.</summary>
    StartFailed
}

/// <summary>
/// Outcome of an external process run.
/// </summary>
/// <param name="Status">How the process ended.</param>
/// <param name="ExitCode">The exit code, or -1 when the process timed out or never started.</param>
/// <param name="StandardOutput">Captured standard output (partial after a timeout).</param>
/// <param name="StandardError">Captured standard error (partial after a timeout).</param>
public sealed record ConnectProcessResult(ConnectProcessStatus Status, int ExitCode, string StandardOutput, string StandardError)
{
    /// <summary>Gets a value indicating whether the process completed with exit code 0.</summary>
    public bool IsSuccess => Status == ConnectProcessStatus.Completed && ExitCode == 0;
}

/// <summary>
/// How <see cref="IConnectProcessRunner"/> logs a run.
/// </summary>
public enum ConnectProcessLogging
{
    /// <summary>A one-off operation: every run logs its start and its outcome.</summary>
    EveryRun,

    /// <summary>
    /// A status read repeated on every monitor pass: the start is not logged, and an outcome is logged only when it
    /// differs from the previous run of the same command (state is logged on transitions, never on every poll).
    /// </summary>
    OnChange
}

/// <summary>
/// Runs external tools with a mandatory timeout.
/// </summary>
public interface IConnectProcessRunner
{
    /// <summary>
    /// Runs <paramref name="executablePath"/> with <paramref name="rawArguments"/> passed unchanged (netsh <c>key="value"</c>
    /// syntax, L2-17). A timeout is reported as <see cref="ConnectProcessStatus.TimedOut"/>; cancelling
    /// <paramref name="cancellationToken"/> throws <see cref="OperationCanceledException"/>.
    /// </summary>
    /// <param name="executablePath">Absolute path of the tool.</param>
    /// <param name="rawArguments">Arguments passed unchanged.</param>
    /// <param name="timeout">Time allowed before the process tree is stopped.</param>
    /// <param name="logging">Whether every run or only a changed outcome is logged.</param>
    /// <param name="cancellationToken">Caller cancellation.</param>
    /// <returns>How the process ended.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="timeout"/> is not positive or not finite.</exception>
    Task<ConnectProcessResult> RunAsync(
        string executablePath,
        string rawArguments,
        TimeSpan timeout,
        ConnectProcessLogging logging,
        CancellationToken cancellationToken);
}

/// <summary>
/// <see cref="IConnectProcessRunner"/> that wraps the shared <see cref="ProcessRunner"/> with a timeout and diagnostics.
/// </summary>
/// <remarks>
/// The shared runner drains redirected standard output and error asynchronously and kills the process tree on cancellation,
/// so a chatty or hung tool cannot deadlock the caller. The exit code and the length of the standard error are logged; the
/// standard-error text and the arguments are not, because the remote Logs stream mirrors every log property and only
/// <see cref="Foundry.Utilities.Diagnostics.LogSecretMasker"/> masks secrets. The full standard error stays available in the returned result.
/// </remarks>
public sealed class ConnectProcessRunner(TimeProvider timeProvider, ILogger<ConnectProcessRunner> logger) : IConnectProcessRunner
{
    // CancellationTokenSource accepts at most uint.MaxValue - 1 milliseconds.
    private static readonly TimeSpan MaxTimeout = TimeSpan.FromMilliseconds(uint.MaxValue - 1d);

    private readonly ProcessRunner _processRunner = new();

    // Last logged outcome per command for ConnectProcessLogging.OnChange; kept in memory only, never logged.
    private readonly Dictionary<string, string> _lastOutcomes = new(StringComparer.Ordinal);

    /// <inheritdoc />
    public async Task<ConnectProcessResult> RunAsync(
        string executablePath,
        string rawArguments,
        TimeSpan timeout,
        ConnectProcessLogging logging,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);
        ArgumentNullException.ThrowIfNull(rawArguments);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(timeout, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(timeout, MaxTimeout);

        // Both sources exist before the first await so a manual clock can fire the timeout right after this call returns.
        using var timeoutSource = new CancellationTokenSource(timeout, timeProvider);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutSource.Token);

        string toolName = Path.GetFileName(executablePath).ToLowerInvariant();
        string commandKey = $"{executablePath}\n{rawArguments}";
        long startTimestamp = timeProvider.GetTimestamp();
        if (logging == ConnectProcessLogging.EveryRun)
        {
            logger.LogDebug("External process started. ToolName={ToolName}", toolName);
        }

        try
        {
            ProcessExecutionResult result = await _processRunner
                .RunAsync(
                    ProcessExecutionRequest.FromRawArguments(executablePath, rawArguments, Environment.SystemDirectory),
                    linked.Token)
                .ConfigureAwait(false);

            if (ShouldLog(logging, commandKey, $"completed:{result.ExitCode}"))
            {
                LogCompleted(result, toolName, timeProvider.GetElapsedTime(startTimestamp));
            }

            return new ConnectProcessResult(ConnectProcessStatus.Completed, result.ExitCode, result.StandardOutput, result.StandardError);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException exception) when (timeoutSource.IsCancellationRequested)
        {
            // The caller did not cancel (handled above), so the timeout cancelled the linked token.
            if (ShouldLog(logging, commandKey, "timed_out"))
            {
                logger.LogWarning(
                    "External process timed out and was stopped. ToolName={ToolName}, TimeoutSeconds={TimeoutSeconds}",
                    toolName,
                    timeout.TotalSeconds);
            }

            ProcessCanceledException? canceled = exception as ProcessCanceledException;
            return new ConnectProcessResult(
                ConnectProcessStatus.TimedOut,
                -1,
                canceled?.StandardOutput ?? string.Empty,
                canceled?.StandardError ?? string.Empty);
        }
        catch (ProcessStartException exception)
        {
            if (ShouldLog(logging, commandKey, "start_failed"))
            {
                using IDisposable? scope = logger.BeginScope(
                    RemoteProcessDiagnostics.CreateStartFailureProperties(exception, timeProvider.GetElapsedTime(startTimestamp)));
                logger.LogWarning("External process could not start. ToolName={ToolName}", toolName);
            }

            return StartFailed(exception.Message);
        }
        catch (Exception exception)
        {
            // The exception is not attached: the remote Logs stream mirrors its message, which can echo the arguments.
            string failureType = exception.GetType().Name;
            if (ShouldLog(logging, commandKey, $"failed:{failureType}"))
            {
                logger.LogWarning(
                    "External process execution failed. ToolName={ToolName}, FailureType={FailureType}, ErrorCode={ErrorCode}",
                    toolName,
                    failureType,
                    NetworkFailureTaxonomy.ExceptionErrorCode(exception));
            }

            return StartFailed(exception.Message);
        }
    }

    private static ConnectProcessResult StartFailed(string message) =>
        new(ConnectProcessStatus.StartFailed, -1, string.Empty, message);

    // EveryRun always logs; OnChange logs the first outcome of a command and then only an outcome that differs.
    private bool ShouldLog(ConnectProcessLogging logging, string commandKey, string outcome)
    {
        if (logging == ConnectProcessLogging.EveryRun)
        {
            return true;
        }

        lock (_lastOutcomes)
        {
            if (_lastOutcomes.TryGetValue(commandKey, out string? previous) && string.Equals(previous, outcome, StringComparison.Ordinal))
            {
                return false;
            }

            _lastOutcomes[commandKey] = outcome;
            return true;
        }
    }

    private void LogCompleted(ProcessExecutionResult result, string toolName, TimeSpan elapsed)
    {
        if (result.IsSuccess)
        {
            logger.LogDebug(
                "External process completed. ToolName={ToolName}, ExitCode={ExitCode}, DurationMs={DurationMs}",
                toolName,
                result.ExitCode,
                Math.Round(elapsed.TotalMilliseconds));
            return;
        }

        using IDisposable? scope = logger.BeginScope(RemoteProcessDiagnostics.CreateProperties(result, elapsed));
        logger.LogWarning(
            "External process returned a nonzero exit code. ToolName={ToolName}, ExitCode={ExitCode}, StandardErrorLength={StandardErrorLength}",
            toolName,
            result.ExitCode,
            result.StandardError.Length);
    }
}
