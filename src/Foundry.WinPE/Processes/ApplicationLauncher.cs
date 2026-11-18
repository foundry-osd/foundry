// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Diagnostics;
using System.Globalization;
using Foundry.Core.Models.Runtime;
using Foundry.Core.Services.Runtime;
using Foundry.Utilities.Diagnostics;
using Foundry.WinPE.Ui;
using Serilog;

namespace Foundry.WinPE.Processes;

/// <summary>
/// Launches the UI process and observes it until it exits, without terminating it or buffering its output. Each launch gets
/// its own directory under <c>Startup\&lt;launch id&gt;</c> in the session directory, holding the readiness status file and
/// the UI state file (<see cref="UiStateFile"/>).
/// </summary>
internal sealed class ApplicationLauncher(ILogger logger, string? sessionDirectory = null,
    Action<string, Guid, string>? recoverFailure = null,
    Func<ProcessStartInfo, Process?>? startProcess = null) : IApplicationLauncher
{
    /// <summary>The UI state file name, next to the launch's status file.</summary>
    internal const string UiStateFileName = "ui-state.json";

    private const string Application = UiReadinessReporter.ApplicationId;

    public async Task<ApplicationLaunchResult> RunUiAsync(string executable, IReadOnlyDictionary<string, string?> environment,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        StartupCapabilityResult capability = RuntimeStartupCapabilities.Negotiate(executable);
        if (capability.Mode != StartupCapabilityMode.Supported)
        {
            // Every supported boot image ships foundry.startup.json advertising protocol v1: anything else is a damaged image.
            logger.Error("Application startup capability manifest is invalid for {Component}; capability {Mode}",
                Application, capability.Mode);
            return new(false, FailureCategory: "capability_invalid");
        }
        ProcessStartInfo start = CreateStartInfo(executable, environment, [WinPeModes.UiArgument]);
        Guid launchId = Guid.NewGuid();
        string sessionId = DiagnosticSessionContext.ResolveSessionId(
            environment.GetValueOrDefault(DiagnosticSessionContext.EnvironmentVariableName));
        string directory = Path.Combine(sessionDirectory ?? Path.Combine(Path.GetTempPath(), "Foundry", "Logs", sessionId),
            "Startup", launchId.ToString("N"));
        string statusPath = Path.Combine(directory, "status.json");
        string statePath = Path.Combine(directory, UiStateFileName);
        Directory.CreateDirectory(directory);
        start.Environment[StartupProtocol.SessionIdEnvironmentVariable] = sessionId;
        start.Environment[StartupProtocol.ProtocolEnvironmentVariable] = capability.ProtocolVersion!.Value.ToString(CultureInfo.InvariantCulture);
        start.Environment[StartupProtocol.LaunchIdEnvironmentVariable] = launchId.ToString("N");
        start.Environment[StartupProtocol.StatusPathEnvironmentVariable] = statusPath;
        start.Environment[UiStateFile.PathEnvironmentVariable] = statePath;
        long launched = Stopwatch.GetTimestamp();
        using Process process = Start(start);
        try
        {
            var reader = new RuntimeStartupStatusReader(statusPath, sessionId, launchId.ToString("N"), Application, process.Id);
            logger.Debug("Observing managed startup for {Component}; process {ProcessId}; protocol {ProtocolVersion}; launch {LaunchId}",
                Application, process.Id, capability.ProtocolVersion, launchId.ToString("N"));
            ApplicationLaunchResult result = await new ApplicationStartupObserver().ObserveAsync(
                new ObservedApplication(process), ReadStatus, cancellationToken).ConfigureAwait(false);
            if (result.FailureCategory == "startup_failed" && recoverFailure is not null && !process.HasExited)
            {
                // Allow orderly child cleanup to release its failure record before terminal telemetry shutdown.
                using var exitGrace = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                exitGrace.CancelAfter(TimeSpan.FromSeconds(5));
                try { await process.WaitForExitAsync(exitGrace.Token).ConfigureAwait(false); }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { }
                cancellationToken.ThrowIfCancellationRequested();
                if (process.HasExited) { result = result with { ExitCode = process.ExitCode }; }
            }
            if (process.HasExited) { result = result with { UiState = UiStateFile.Read(statePath) }; }
            logger.Information(
                "Application run observed for {Component}: {Outcome}; stage {Stage}; exit {ExitCode}; reason {FailureReason}; UI phase {UiPhase}; destructive step started {DestructiveStarted}; duration {DurationMilliseconds:F0} ms",
                Application, result.Succeeded ? "Succeeded" : "Stopped", result.LastStage, result.ExitCode, result.FailureCategory,
                result.UiState?.Phase, result.UiState?.DestructiveStarted, Stopwatch.GetElapsedTime(launched).TotalMilliseconds);
            return result;

            RuntimeStartupStatus? ReadStatus()
            {
                RuntimeStartupStatus? status = reader.Read();
                if (status is not null)
                {
                    logger.Information("Application {Component} acknowledged startup stage {Stage}; process {ProcessId}; elapsed {DurationMilliseconds:F0} ms",
                        Application, status.Stage, process.Id, Stopwatch.GetElapsedTime(launched).TotalMilliseconds);
                }
                return status;
            }
        }
        finally
        {
            // The child owns its failure file until process exit; timeout and cancellation never terminate it.
            if (process.HasExited)
            {
                try { recoverFailure?.Invoke(directory, launchId, Application); }
                catch (Exception exception) { logger.Warning(exception, "Child startup diagnostics could not be recovered"); }
            }
        }
    }

    internal async Task<int> ObserveAsync(Process process, CancellationToken cancellationToken)
    {
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        logger.Information("Application process {ProcessId} exited with code {ExitCode}", process.Id, process.ExitCode);
        return process.ExitCode;
    }

    internal static ProcessStartInfo CreateStartInfo(string executable,
        IReadOnlyDictionary<string, string?> environment, IEnumerable<string> arguments)
    {
        var start = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            // Child modes never write to the console; sharing startnet's console would let Ctrl+C there terminate them.
            CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(Path.GetFullPath(executable))!
        };
        foreach (string argument in arguments) { start.ArgumentList.Add(argument); }
        foreach ((string key, string? value) in environment)
        {
            if (value is null) { start.Environment.Remove(key); }
            else { start.Environment[key] = value; }
        }

        return start;
    }

    private Process Start(ProcessStartInfo start)
    {
        Process process = (startProcess is null ? Process.Start(start) : startProcess(start)) ??
            throw new InvalidOperationException("The application process could not be created.");
        logger.Information("Application process {ProcessId} launched", process.Id);
        return process;
    }
}
