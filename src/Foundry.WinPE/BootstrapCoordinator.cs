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
/// Outcome of a UI process exit: 0 succeeds and 20 is a cancellation. Any other exit before <c>ui_ready</c>, and 21–23
/// (startup or configuration failure of a phase) at any time, end the boot with exit code 1: the console names the failure
/// (startup failure, readiness timeout, invalid startup metadata, or the phase and its exit code), the result carries the
/// failure category and the attributed stage, and no crash window is shown.
/// </para>
/// <para>
/// Crash: any other exit after <c>ui_ready</c> (an unhandled exception ends the UI process with
/// <see cref="UiProcessExit.UnhandledExceptionExitCode"/>, a killed process with any code), unless Windows is shutting down
/// (<paramref name="isSystemShuttingDown"/>), as when Foundry Deploy restarts the computer: that exit is a normal end. The
/// crash is attributed to the phase recorded in the UI state file, reported through <paramref name="completed"/>, delivered
/// and the logs persisted, then <paramref name="crashScreen"/> asks the operator. "Relaunch" is offered only when the state
/// file says no destructive step started; an unknown state (missing, unreadable or invalid file) never offers it and is
/// attributed to Foundry Connect, since nothing better is known. A relaunch is a new launch from the Connect phase; there is
/// no automatic relaunch.
/// </para>
/// <para>
/// <paramref name="completed"/> receives one result per boot: the first crash, or else the final result, so a boot sends at
/// most one <c>bootstrap:failed</c>. <paramref name="startDelivery"/> runs once, when there is something to send: as soon as
/// the running UI process reaches its Deploy phase (a deployment ends with a restart while it still runs), or else when it
/// exits or crashes. The guardian never synchronizes the clock again: the UI process owns that, and the caller reads the
/// clock usability when delivery starts. Results of the UI process carry the file version of the executable
/// (<paramref name="readPayloadVersion"/>, default the file version; it must not throw).
/// </para>
/// </remarks>
internal sealed class BootstrapCoordinator(BootstrapContext context,
    ISystemPreparation preparation, IApplicationLauncher launcher, IUiCrashScreen crashScreen,
    IBootstrapLogPersistence persistence, ILogger logger, Action<BootstrapProgress> progress, Action? startDelivery = null,
    Action<BootstrapResult, TimeSpan>? completed = null, Func<string, string?>? readPayloadVersion = null,
    string? executablePath = null, Func<bool>? isSystemShuttingDown = null)
{
    private BootstrapStage stage = BootstrapStage.Environment;
    private BootstrapStage? loggedStage;
    private long stageStarted;
    private long started;
    private bool outcomeReported;
    private int deliveryStarted;
    private readonly Func<bool> isSystemShuttingDown = isSystemShuttingDown ?? SystemShutdown.IsInProgress;
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
            logger.Error(exception, "Guardian stage {Stage} failed", ReportedStageName());
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
                _ when result.ChildExitCode is int code => ChildExitMessages.Describe(result.Stage, code),
                _ when result.FailureCategory == "capability_invalid" => "Application startup metadata is invalid. Recreate the boot media or use Update USB.",
                _ => "This boot stage could not be completed. Check the session log for details."
            };
            Report(status, message);
            logger.Information("Guardian finished with {Outcome} at {Stage}; child exit {ExitCode}; elapsed {DurationMilliseconds} ms",
                result.Outcome, LogStageName(result.Stage), result.ChildExitCode, Stopwatch.GetElapsedTime(started).TotalMilliseconds);
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

        // Progress of the UI process is reported on the Connect stage (the console's Foundry line); a result carries the phase
        // it ended in.
        stage = BootstrapStage.Connect;
        payloadVersion = readPayloadVersion(executable);
        for (int launch = 1; ; launch++)
        {
            Report(BootstrapStatus.Running, launch == 1 ? "Starting Foundry" : "Relaunching Foundry");
            ApplicationLaunchResult ui = await launcher.RunUiAsync(executable, context.ChildEnvironment, OnUiState,
                cancellationToken).ConfigureAwait(false);
            StartDelivery();
            BootstrapStage uiStage = ui.ReadinessConfirmed ? StageOf(ui.UiState) : BootstrapStage.Connect;
            if (ui.Succeeded)
            {
                return new BootstrapResult(BootstrapOutcome.Succeeded, uiStage, ui.ExitCode, PayloadVersion: payloadVersion);
            }

            BootstrapResult failure = new(
                ui.ExitCode == (int)ChildRuntimeExitCode.UserAborted ? BootstrapOutcome.Cancelled : BootstrapOutcome.Failed,
                uiStage, ui.ExitCode, ui.FailureCategory, ui.LastStage, payloadVersion);
            if (!IsCrash(ui))
            {
                return failure;
            }

            if (isSystemShuttingDown())
            {
                logger.Information(
                    "Foundry UI process ended during system shutdown; treated as a normal end. ExitCode={ExitCode}, Phase={Phase}",
                    ui.ExitCode, LogStageName(uiStage));
                return new BootstrapResult(BootstrapOutcome.Succeeded, uiStage, ui.ExitCode, PayloadVersion: payloadVersion);
            }

            if (await HandleCrashAsync(ui, failure, launch, cancellationToken).ConfigureAwait(false) != UiCrashChoice.Relaunch)
            {
                return failure;
            }
        }
    }

    /// <summary>
    /// Reports the crash (delivery has started) and persists the logs before the operator answers, since the operator may power
    /// off at the crash window, then shows the window.
    /// </summary>
    private async Task<UiCrashChoice> HandleCrashAsync(ApplicationLaunchResult ui, BootstrapResult failure, int launch,
        CancellationToken cancellationToken)
    {
        bool relaunchAllowed = ui.UiState is { DestructiveStarted: false };
        logger.Error(
            "Foundry UI process stopped unexpectedly. Launch={Launch}, ExitCode={ExitCode}, Phase={Phase}, UiStateKnown={UiStateKnown}, DestructiveStarted={DestructiveStarted}, RelaunchAllowed={RelaunchAllowed}",
            launch, ui.ExitCode, LogStageName(failure.Stage), ui.UiState is not null, ui.UiState?.DestructiveStarted, relaunchAllowed);
        Report(BootstrapStatus.Warning, $"Foundry stopped unexpectedly with exit code {ui.ExitCode}.");
        ReportOutcome(failure);
        await PersistLogsAsync().ConfigureAwait(false);
        Report(BootstrapStatus.Running, relaunchAllowed
            ? "Foundry stopped unexpectedly. Choose Relaunch or Close in the Foundry window."
            : "Foundry stopped unexpectedly and cannot be relaunched. Close the Foundry window.");

        UiCrashChoice choice;
        try
        {
            choice = await crashScreen.ShowAsync(relaunchAllowed, ui.UiState?.Culture, cancellationToken).ConfigureAwait(false);
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

    // Called on the launcher's supervision thread: the Deploy phase follows system preparation, so the clock is as good as it gets.
    private void OnUiState(UiState state)
    {
        if (state.Phase == UiPhase.Deploy) { StartDelivery(); }
    }

    private void StartDelivery()
    {
        if (startDelivery is null || Interlocked.Exchange(ref deliveryStarted, 1) != 0) { return; }
        logger.Debug("Guardian telemetry delivery started");
        try { startDelivery(); }
        catch { }
    }

    /// <summary>The stage name of the guardian log: the UI process runs all its phases as the "Foundry UI" stage.</summary>
    private static string LogStageName(BootstrapStage stage) => stage switch
    {
        BootstrapStage.Environment => "Environment",
        BootstrapStage.Connect => "Foundry UI (Connect)",
        BootstrapStage.System => "Foundry UI (system preparation)",
        BootstrapStage.Deploy => "Foundry UI (Deploy)",
        _ => stage.ToString()
    };

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
            logger.Warning(exception, "Optional boot preparation failed at {Stage}", ReportedStageName());
            Report(BootstrapStatus.Warning, warning);
        }
    }

    // Progress of the UI process is reported on the Connect stage, which the log names after the UI process.
    private string ReportedStageName() => stage == BootstrapStage.Environment ? "Environment" : "Foundry UI";

    private void Report(BootstrapStatus status, string message)
    {
        if (status == BootstrapStatus.Running && loggedStage != stage)
        {
            loggedStage = stage;
            stageStarted = Stopwatch.GetTimestamp();
            logger.Information("Guardian stage {Stage} started", ReportedStageName());
        }
        if (status == BootstrapStatus.Completed)
        {
            logger.Information("Guardian stage {Stage} completed in {DurationMilliseconds:F0} ms",
                ReportedStageName(), Stopwatch.GetElapsedTime(stageStarted).TotalMilliseconds);
        }
        progress(new BootstrapProgress(stage, status, message));
    }
}
