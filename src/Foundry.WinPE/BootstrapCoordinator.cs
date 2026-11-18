// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Diagnostics;
using Foundry.WinPE.Diagnostics;
using Foundry.WinPE.Processes;
using Foundry.WinPE.SystemPreparation;
using Foundry.WinPE.Ui;
using Foundry.Core.Models.Runtime;
using Serilog;

namespace Foundry.WinPE;

/// <summary>
/// Runs the finite, ordered WinPE boot workflow: the environment stage (network, early clock), then the UI process started
/// from this guardian's own executable (<paramref name="executablePath"/>, default <see cref="Environment.ProcessPath"/>) in
/// its <c>--ui</c> mode, supervised until it exits. The UI process runs Foundry Connect, system preparation (clock and time
/// zone) and Foundry Deploy; no runtime is resolved or downloaded, Deploy reads Foundry PostInstall from the boot image.
/// </summary>
/// <remarks>
/// <para>
/// Outcome of a UI process exit: 0 succeeds, 20 is a cancellation, and any other exit before <c>ui_ready</c>, or 21–23 at
/// any time, is a failure reported as today (startup failure, readiness timeout, invalid startup metadata, child exit).
/// </para>
/// <para>
/// Crash: any other exit after <c>ui_ready</c> (an unhandled exception ends the UI process with
/// <see cref="UiProcessExit.UnhandledExceptionExitCode"/>, a killed process with any code). The crash is attributed to the
/// phase recorded in the UI state file, reported through <paramref name="completed"/>, delivered and the logs persisted, then
/// <paramref name="crashScreen"/> asks the operator. "Relaunch" is offered only when the state file says no destructive step
/// started; an unknown state (missing, unreadable or invalid file) never offers it and is attributed to Foundry Connect,
/// since nothing better is known. A relaunch is a new launch from the Connect phase; there is no automatic relaunch.
/// </para>
/// <para>
/// <paramref name="completed"/> receives one result per boot: the first crash, or else the final result, so a boot sends at
/// most one <c>bootstrap:failed</c> as before. <paramref name="startDelivery"/> runs once, when there is something to send
/// (after the UI process exited or crashed), right after a best-effort clock probe so the caller reads the clock usability
/// at that moment. Results of the UI process carry the file version of the executable (<paramref name="readPayloadVersion"/>,
/// default the file version; it must not throw).
/// </para>
/// </remarks>
internal sealed class BootstrapCoordinator(BootstrapContext context,
    ISystemPreparation preparation, IApplicationLauncher launcher, IUiCrashScreen crashScreen,
    IBootstrapLogPersistence persistence, ILogger logger, Action<BootstrapProgress> progress, Action? startDelivery = null,
    Action<BootstrapResult, TimeSpan>? completed = null, Func<string, string?>? readPayloadVersion = null,
    string? executablePath = null)
{
    private BootstrapStage stage = BootstrapStage.Environment;
    private BootstrapStage? loggedStage;
    private long stageStarted;
    private long started;
    private bool outcomeReported;
    private bool deliveryStarted;
    private readonly Func<string, string?> readPayloadVersion = readPayloadVersion ?? ReadFileVersion;

    private readonly string executable = executablePath ?? Environment.ProcessPath ??
        throw new InvalidOperationException("The Foundry.WinPE executable path is unavailable.");
    private string? payloadVersion;

    internal async Task<BootstrapResult> RunAsync(CancellationToken cancellationToken)
    {
        started = Stopwatch.GetTimestamp();
        logger.Information("Guardian started in {DeploymentMode} mode for {RuntimeIdentifier}",
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
            logger.Error(exception, "Guardian stage {Stage} failed", stage);
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
                BootstrapOutcome.Succeeded => "Foundry closed.",
                BootstrapOutcome.Cancelled => "Boot was cancelled. Deployment will not continue.",
                _ when result.FailureCategory == "readiness_timeout" => "Application readiness was not confirmed within two minutes. The application may still be running.",
                _ when result.ChildExitCode is int code => ChildExitMessages.Describe(code),
                _ when result.FailureCategory == "capability_invalid" => "Application startup metadata is invalid. Recreate the boot media or use Update USB.",
                _ => "This boot stage could not be completed. Check the session log for details."
            };
            Report(status, message);
            logger.Information("Guardian finished with {Outcome} at {Stage}; child exit {ExitCode}; elapsed {DurationMilliseconds} ms",
                result.Outcome, result.Stage, result.ChildExitCode, Stopwatch.GetElapsedTime(started).TotalMilliseconds);
            ReportOutcome(result);
        }
        finally
        {
            await PersistLogsAsync().ConfigureAwait(false);
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
            "Early clock synchronization was unavailable. Continuing to Foundry.").ConfigureAwait(false);

        // Progress of the UI process is reported on the Connect stage; a result carries the phase it ended in.
        stage = BootstrapStage.Connect;
        payloadVersion = readPayloadVersion(executable);
        for (int launch = 1; ; launch++)
        {
            Report(BootstrapStatus.Running, launch == 1 ? "Starting Foundry" : "Relaunching Foundry");
            ApplicationLaunchResult ui = await launcher.RunUiAsync(executable, context.ChildEnvironment, cancellationToken)
                .ConfigureAwait(false);
            if (ui.Succeeded)
            {
                Report(BootstrapStatus.Completed, "Foundry closed.");
                await StartDeliveryAsync(cancellationToken).ConfigureAwait(false);
                return new BootstrapResult(BootstrapOutcome.Succeeded, stage, ui.ExitCode, PayloadVersion: payloadVersion);
            }

            BootstrapResult failure = new(
                ui.ExitCode == (int)ChildRuntimeExitCode.UserAborted ? BootstrapOutcome.Cancelled : BootstrapOutcome.Failed,
                ui.ReadinessConfirmed ? StageOf(ui.UiState) : BootstrapStage.Connect,
                ui.ExitCode, ui.FailureCategory, ui.LastStage, payloadVersion);
            if (!IsCrash(ui))
            {
                await StartDeliveryAsync(cancellationToken).ConfigureAwait(false);
                return failure;
            }

            if (await HandleCrashAsync(ui, failure, launch, cancellationToken).ConfigureAwait(false) != UiCrashChoice.Relaunch)
            {
                return failure;
            }
        }
    }

    /// <summary>
    /// Reports the crash, delivers it and persists the logs before the operator answers, since the operator may power off at
    /// the crash window, then shows the window.
    /// </summary>
    private async Task<UiCrashChoice> HandleCrashAsync(ApplicationLaunchResult ui, BootstrapResult failure, int launch,
        CancellationToken cancellationToken)
    {
        bool relaunchAllowed = ui.UiState is { DestructiveStarted: false };
        logger.Error(
            "Foundry UI process stopped unexpectedly. Launch={Launch}, ExitCode={ExitCode}, Phase={Phase}, UiStateKnown={UiStateKnown}, DestructiveStarted={DestructiveStarted}, RelaunchAllowed={RelaunchAllowed}",
            launch, ui.ExitCode, failure.Stage, ui.UiState is not null, ui.UiState?.DestructiveStarted, relaunchAllowed);
        Report(BootstrapStatus.Warning, $"Foundry stopped unexpectedly with exit code {ui.ExitCode}.");
        ReportOutcome(failure);
        await StartDeliveryAsync(cancellationToken).ConfigureAwait(false);
        await PersistLogsAsync().ConfigureAwait(false);
        Report(BootstrapStatus.Running, relaunchAllowed
            ? "Foundry stopped unexpectedly. Choose Relaunch or Close in the Foundry window."
            : "Foundry stopped unexpectedly and cannot be relaunched. Close the Foundry window.");

        UiCrashChoice choice;
        try
        {
            choice = await crashScreen.ShowAsync(relaunchAllowed, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.Error(exception, "The crash window could not be shown. The boot ends.");
            choice = UiCrashChoice.Close;
        }

        // The window offers Relaunch only when allowed; the decision is enforced here as well.
        if (!relaunchAllowed) { choice = UiCrashChoice.Close; }
        logger.Information("Crash window closed with {Choice}. Launch={Launch}", choice, launch);
        return choice;
    }

    /// <summary>A crash is an exit after readiness with a code that is no UI process outcome (0, 20, 21, 22, 23).</summary>
    private static bool IsCrash(ApplicationLaunchResult ui) => ui.ReadinessConfirmed && ui.ExitCode is int code &&
        (ChildRuntimeExitCode)code is not (ChildRuntimeExitCode.Success or ChildRuntimeExitCode.UserAborted or
            ChildRuntimeExitCode.StartupFailure or ChildRuntimeExitCode.ConfigurationFailure or ChildRuntimeExitCode.RuntimeFailure);

    /// <summary>Maps the recorded phase to its stage; an unknown state is attributed to Foundry Connect.</summary>
    private static BootstrapStage StageOf(UiState? state) => state?.Phase switch
    {
        UiPhase.System => BootstrapStage.System,
        UiPhase.Deploy => BootstrapStage.Deploy,
        _ => BootstrapStage.Connect
    };

    private void ReportOutcome(BootstrapResult result)
    {
        if (outcomeReported) { return; }
        outcomeReported = true;
        try { completed?.Invoke(result, Stopwatch.GetElapsedTime(started)); }
        catch { }
    }

    private async Task StartDeliveryAsync(CancellationToken cancellationToken)
    {
        if (startDelivery is null || deliveryStarted) { return; }
        deliveryStarted = true;
        // The UI process may have corrected the clock meanwhile: probe again (it returns at once when already usable).
        try { await preparation.PrepareClockAsync(cancellationToken).ConfigureAwait(false); }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.Debug("Clock probe before telemetry delivery failed. FailureType={FailureType}", exception.GetType().Name);
        }
        try { startDelivery(); }
        catch { }
    }

    private async Task PersistLogsAsync()
    {
        try { await persistence.PersistAsync(CancellationToken.None).ConfigureAwait(false); }
        catch (Exception exception) { logger.Warning(exception, "Boot log persistence failed"); }
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
            logger.Information("Guardian stage {Stage} started", stage);
        }
        if (status == BootstrapStatus.Completed)
        {
            logger.Information("Guardian stage {Stage} completed in {DurationMilliseconds:F0} ms",
                stage, Stopwatch.GetElapsedTime(stageStarted).TotalMilliseconds);
        }
        progress(new BootstrapProgress(stage, status, message));
    }
}
