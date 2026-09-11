// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Diagnostics;
using System.Globalization;
using Foundry.Core.Models.Runtime;
using Foundry.Core.Services.Runtime;
using Foundry.Utilities.Diagnostics;
using Serilog;

namespace Foundry.Bootstrap.Processes;

/// <summary>Observes application lifetimes without owning their termination or buffering their output.</summary>
internal sealed class ApplicationLauncher(ILogger logger, string? sessionDirectory = null,
    Action<string>? warning = null, Action<string, Guid, string>? recoverFailure = null,
    Func<ProcessStartInfo, Process?>? startProcess = null) : IApplicationLauncher
{
    public Task<ApplicationLaunchResult> RunConnectAsync(string executable, string configurationPath,
        IReadOnlyDictionary<string, string?> environment, CancellationToken cancellationToken)
    {
        string[] arguments = File.Exists(configurationPath) ? ["--config", configurationPath] : [];
        return LaunchAsync(executable, "Foundry.Connect", environment, arguments, true, cancellationToken);
    }

    public Task<ApplicationLaunchResult> StartDeployAsync(string executable, IReadOnlyDictionary<string, string?> environment,
        CancellationToken cancellationToken) =>
        LaunchAsync(executable, "Foundry.Deploy", environment, [], false, cancellationToken);

    private async Task<ApplicationLaunchResult> LaunchAsync(string executable, string application,
        IReadOnlyDictionary<string, string?> environment, string[] arguments, bool waitForCompletion,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        StartupCapabilityResult capability = RuntimeStartupCapabilities.Negotiate(executable);
        if (capability.Mode == StartupCapabilityMode.Invalid)
        {
            logger.Error("Application startup capability manifest is invalid for {Component}", application);
            return new(false, FailureCategory: "capability_invalid");
        }
        ProcessStartInfo start = CreateStartInfo(executable, environment, arguments);
        // Never inherit another launch's protocol when handing off a legacy payload.
        start.Environment.Remove(StartupProtocol.ProtocolEnvironmentVariable);
        start.Environment.Remove(StartupProtocol.LaunchIdEnvironmentVariable);
        start.Environment.Remove(StartupProtocol.StatusPathEnvironmentVariable);
        bool supported = capability.Mode == StartupCapabilityMode.Supported;
        Guid launchId = Guid.NewGuid();
        string sessionId = DiagnosticSessionContext.ResolveSessionId(
            environment.GetValueOrDefault(DiagnosticSessionContext.EnvironmentVariableName));
        string directory = Path.Combine(sessionDirectory ?? Path.Combine(Path.GetTempPath(), "Foundry", "Logs", sessionId),
            "Startup", launchId.ToString("N"));
        string statusPath = Path.Combine(directory, "status.json");
        if (supported)
        {
            Directory.CreateDirectory(directory);
            start.Environment[StartupProtocol.SessionIdEnvironmentVariable] = sessionId;
            start.Environment[StartupProtocol.ProtocolEnvironmentVariable] = capability.ProtocolVersion!.Value.ToString(CultureInfo.InvariantCulture);
            start.Environment[StartupProtocol.LaunchIdEnvironmentVariable] = launchId.ToString("N");
            start.Environment[StartupProtocol.StatusPathEnvironmentVariable] = statusPath;
        }
        else
        {
            logger.Warning("Application startup readiness is unverified for {Component}; capability {Mode}; reason {CompatibilityReason}",
                application, capability.Mode, capability.Mode == StartupCapabilityMode.Legacy ? "Startup capability manifest is absent" : "No supported startup protocol version was advertised");
            warning?.Invoke("This application does not support startup confirmation. Readiness will remain unverified.");
        }
        long launched = Stopwatch.GetTimestamp();
        using Process process = Start(start);
        try
        {
            if (!supported)
            {
                if (!waitForCompletion) { return new(true); }
                int exitCode = await ObserveAsync(process, cancellationToken).ConfigureAwait(false);
                return new(exitCode == 0, exitCode, FailureCategory: exitCode == 0 ? null : "child_exit");
            }
            var reader = new RuntimeStartupStatusReader(statusPath, sessionId, launchId.ToString("N"), application, process.Id);
            logger.Debug("Observing managed startup for {Component}; process {ProcessId}; protocol {ProtocolVersion}",
                application, process.Id, capability.ProtocolVersion);
            ApplicationLaunchResult result = await new ApplicationStartupObserver().ObserveAsync(
                new ObservedApplication(process), ReadStatus, waitForCompletion, cancellationToken).ConfigureAwait(false);
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
            logger.Information(
                "Application startup observed for {Component}: {Outcome}; stage {Stage}; exit {ExitCode}; reason {FailureReason}; duration {DurationMilliseconds:F0} ms",
                application, result.Succeeded ? "Succeeded" : "Stopped", result.LastStage, result.ExitCode, result.FailureCategory,
                Stopwatch.GetElapsedTime(launched).TotalMilliseconds);
            return result;

            RuntimeStartupStatus? ReadStatus()
            {
                RuntimeStartupStatus? status = reader.Read();
                if (status is not null)
                {
                    logger.Information("Application {Component} acknowledged startup stage {Stage}; process {ProcessId}; elapsed {DurationMilliseconds:F0} ms",
                        application, status.Stage, process.Id, Stopwatch.GetElapsedTime(launched).TotalMilliseconds);
                }
                return status;
            }
        }
        finally
        {
            // The child owns its failure file until process exit; timeout and cancellation never terminate it.
            if (supported && process.HasExited)
            {
                try { recoverFailure?.Invoke(directory, launchId, application); }
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
