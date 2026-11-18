// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Diagnostics;
using Foundry.WinPE.Diagnostics;
using Foundry.WinPE.Processes;
using Foundry.WinPE.SystemPreparation;
using Foundry.Core.Models.Runtime;
using Foundry.Core.Services.Runtime;
using Serilog;

namespace Foundry.WinPE;

/// <summary>
/// Runs the finite, ordered WinPE boot workflow. Foundry Connect and Foundry Deploy run in one UI process started from this
/// guardian's own executable (<paramref name="executablePath"/>, default <see cref="Environment.ProcessPath"/>) in its
/// <c>--ui</c> mode, which also prepares the clock and time zone between them; no runtime is resolved or downloaded, Deploy
/// reads Foundry PostInstall from the boot image. The guardian waits for the UI process to exit. A result of the UI stage
/// carries the file version of that executable, other stages carry none. <paramref name="readPayloadVersion"/> defaults to
/// the executable file version and must not throw.
/// </summary>
internal sealed class BootstrapCoordinator(BootstrapContext context,
    ISystemPreparation preparation, IApplicationLauncher launcher, IBootstrapLogPersistence persistence,
    ILogger logger, Action<BootstrapProgress> progress, Action? deliveryReady = null,
    Action<BootstrapResult, TimeSpan>? completed = null, Func<string, string?>? readPayloadVersion = null,
    string? executablePath = null)
{
    private BootstrapStage stage = BootstrapStage.Environment;
    private BootstrapStage? loggedStage;
    private long stageStarted;
    private readonly Func<string, string?> readPayloadVersion = readPayloadVersion ?? ReadFileVersion;

    private readonly string executable = executablePath ?? Environment.ProcessPath ??
        throw new InvalidOperationException("The Foundry.WinPE executable path is unavailable.");
    private string? payloadVersion;

    internal async Task<BootstrapResult> RunAsync(CancellationToken cancellationToken)
    {
        long started = Stopwatch.GetTimestamp();
        logger.Information("Bootstrap started in {DeploymentMode} mode for {RuntimeIdentifier}",
            context.IsUsb ? "Usb" : "Iso", context.RuntimeIdentifier);
        BootstrapResult result;
        try
        {
            result = await ExecuteAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            result = new BootstrapResult(BootstrapOutcome.Cancelled, stage, PayloadVersion: payloadVersion);
        }
        catch (Exception exception)
        {
            logger.Error(exception, "Bootstrap stage {Stage} failed", stage);
            result = new BootstrapResult(BootstrapOutcome.Failed, stage, PayloadVersion: payloadVersion);
        }

        try
        {
            BootstrapStatus status = result.Outcome switch
            {
                BootstrapOutcome.Succeeded => BootstrapStatus.Completed,
                BootstrapOutcome.Cancelled => BootstrapStatus.Cancelled,
                _ => BootstrapStatus.Failed
            };
            string message = result.Outcome switch
            {
                BootstrapOutcome.Succeeded => result.ReadinessConfirmed ? "Foundry Deploy is ready." : "Foundry Deploy was launched. Readiness is unverified.",
                BootstrapOutcome.Cancelled => "Boot was cancelled. Deployment will not continue.",
                _ when result.FailureCategory == "readiness_timeout" => "Application readiness was not confirmed within two minutes. The application may still be running.",
                _ when result.ChildExitCode is int code => ChildExitMessages.Describe(result.Stage, code),
                _ when result.FailureCategory == "capability_invalid" => "Application startup metadata is invalid. Recreate the boot media or use Update USB.",
                _ => "This boot stage could not be completed. Check the session log for details."
            };
            Report(status, message);
            logger.Information("Bootstrap finished with {Outcome} at {Stage}; child exit {ExitCode}; elapsed {DurationMilliseconds} ms",
                result.Outcome, result.Stage, result.ChildExitCode, Stopwatch.GetElapsedTime(started).TotalMilliseconds);
            try { completed?.Invoke(result, Stopwatch.GetElapsedTime(started)); }
            catch { }
        }
        finally
        {
            try { await persistence.PersistAsync(CancellationToken.None).ConfigureAwait(false); }
            catch (Exception exception) { logger.Warning(exception, "Boot log persistence failed"); }
        }

        return result;
    }

    private async Task<BootstrapResult> ExecuteAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Report(BootstrapStatus.Running, "Preparing network access");
        await BestEffortAsync(() => preparation.PrepareNetworkAsync(cancellationToken),
            "Some network preparation was unavailable. Continuing.").ConfigureAwait(false);
        Report(BootstrapStatus.Completed, "Environment prepared");

        await BestEffortAsync(() => preparation.PrepareClockAsync(cancellationToken),
            "Early clock synchronization was unavailable. Continuing to Foundry Connect.").ConfigureAwait(false);

        // The UI process starts with Foundry Connect; until the UI state file is read, its whole run is the Connect stage.
        stage = BootstrapStage.Connect;
        payloadVersion = readPayloadVersion(executable);
        Report(BootstrapStatus.Running, "Waiting for Foundry Connect and Foundry Deploy");
        ApplicationLaunchResult uiResult = await launcher.RunUiAsync(executable, context.ChildEnvironment, cancellationToken)
            .ConfigureAwait(false);
        if (!uiResult.Succeeded)
        {
            return new BootstrapResult(uiResult.ExitCode == (int)ChildRuntimeExitCode.UserAborted ? BootstrapOutcome.Cancelled : BootstrapOutcome.Failed,
                stage, uiResult.ExitCode, uiResult.FailureCategory, uiResult.LastStage, PayloadVersion: payloadVersion);
        }

        Report(BootstrapStatus.Completed, "Foundry Connect and Foundry Deploy completed");
        try { deliveryReady?.Invoke(); }
        catch { }
        return new BootstrapResult(BootstrapOutcome.Succeeded, stage, uiResult.ExitCode, uiResult.FailureCategory,
            uiResult.LastStage, uiResult.ReadinessConfirmed, payloadVersion);
    }

    private static string? ReadFileVersion(string executable)
    {
        try { return FileVersionInfo.GetVersionInfo(executable).FileVersion; }
        catch { return null; }
    }
    private async Task BestEffortAsync(Func<Task> action, string warning)
    {
        try { await action().ConfigureAwait(false); }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.Warning(exception, "Optional boot preparation failed at {Stage}", stage);
            Report(BootstrapStatus.Warning, warning);
        }
    }

    private void Report(BootstrapStatus status, string message)
    {
        if (status == BootstrapStatus.Running && loggedStage != stage)
        {
            loggedStage = stage;
            stageStarted = Stopwatch.GetTimestamp();
            logger.Information("Bootstrap stage {Stage} started", stage);
        }
        if (status == BootstrapStatus.Completed)
        {
            logger.Information("Bootstrap stage {Stage} completed in {DurationMilliseconds:F0} ms",
                stage, Stopwatch.GetElapsedTime(stageStarted).TotalMilliseconds);
        }
        progress(new BootstrapProgress(stage, status, message));
    }
}
