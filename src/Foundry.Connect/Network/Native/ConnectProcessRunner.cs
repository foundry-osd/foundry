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
/// Runs external tools with a mandatory timeout.
/// </summary>
public interface IConnectProcessRunner
{
    /// <summary>
    /// Runs <paramref name="executablePath"/> with <paramref name="rawArguments"/> passed unchanged (netsh <c>key="value"</c>
    /// syntax, L2-17). A timeout is reported as <see cref="ConnectProcessStatus.TimedOut"/>; cancelling
    /// <paramref name="cancellationToken"/> throws <see cref="OperationCanceledException"/>.
    /// </summary>
    Task<ConnectProcessResult> RunAsync(string executablePath, string rawArguments, TimeSpan timeout, CancellationToken cancellationToken);
}

/// <summary>
/// <see cref="IConnectProcessRunner"/> that wraps the shared <see cref="ProcessRunner"/> with a timeout and diagnostics.
/// </summary>
/// <remarks>
/// The shared runner drains redirected standard output and error asynchronously and kills the process tree on cancellation,
/// so a chatty or hung tool cannot deadlock the caller. Exit codes and standard error are logged locally (the PostHog Logs
/// stream drops properties that are not allowlisted); the arguments are never logged.
/// </remarks>
public sealed class ConnectProcessRunner(TimeProvider timeProvider, ILogger<ConnectProcessRunner> logger) : IConnectProcessRunner
{
    private const int MaxLoggedStandardErrorLength = 4096;

    private readonly ProcessRunner _processRunner = new();

    /// <inheritdoc />
    public async Task<ConnectProcessResult> RunAsync(
        string executablePath,
        string rawArguments,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);
        ArgumentNullException.ThrowIfNull(rawArguments);

        // Both sources exist before the first await so a manual clock can fire the timeout right after this call returns.
        using var timeoutSource = new CancellationTokenSource(timeout, timeProvider);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutSource.Token);

        string toolName = Path.GetFileName(executablePath).ToLowerInvariant();
        long startTimestamp = timeProvider.GetTimestamp();
        logger.LogDebug("External process started. ToolName={ToolName}", toolName);

        try
        {
            ProcessExecutionResult result = await _processRunner
                .RunAsync(
                    ProcessExecutionRequest.FromRawArguments(executablePath, rawArguments, Environment.SystemDirectory),
                    linked.Token)
                .ConfigureAwait(false);

            LogCompleted(result, toolName, timeProvider.GetElapsedTime(startTimestamp));
            return new ConnectProcessResult(ConnectProcessStatus.Completed, result.ExitCode, result.StandardOutput, result.StandardError);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException exception)
        {
            // Only the timeout can have cancelled the linked token here.
            logger.LogWarning(
                "External process timed out and was stopped. ToolName={ToolName}, TimeoutSeconds={TimeoutSeconds}",
                toolName,
                timeout.TotalSeconds);

            ProcessCanceledException? canceled = exception as ProcessCanceledException;
            return new ConnectProcessResult(
                ConnectProcessStatus.TimedOut,
                -1,
                canceled?.StandardOutput ?? string.Empty,
                canceled?.StandardError ?? string.Empty);
        }
        catch (ProcessStartException exception)
        {
            using IDisposable? scope = logger.BeginScope(
                RemoteProcessDiagnostics.CreateStartFailureProperties(exception, timeProvider.GetElapsedTime(startTimestamp)));
            logger.LogWarning("External process could not start. ToolName={ToolName}", toolName);
            return StartFailed(exception.Message);
        }
        catch (Exception exception)
        {
            logger.LogDebug(
                exception,
                "External process execution failed. ToolName={ToolName}, FailureType={FailureType}",
                toolName,
                exception.GetType().Name);
            return StartFailed(exception.Message);
        }
    }

    private static ConnectProcessResult StartFailed(string message) =>
        new(ConnectProcessStatus.StartFailed, -1, string.Empty, message);

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

        string standardError = result.StandardError.Trim();
        if (standardError.Length > MaxLoggedStandardErrorLength)
        {
            standardError = standardError[..MaxLoggedStandardErrorLength];
        }

        using IDisposable? scope = logger.BeginScope(RemoteProcessDiagnostics.CreateProperties(result, elapsed));
        logger.LogWarning(
            "External process returned a nonzero exit code. ToolName={ToolName}, ExitCode={ExitCode}, StandardError={StandardError}",
            toolName,
            result.ExitCode,
            standardError);
    }
}
