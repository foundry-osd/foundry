// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Models.Runtime;
using Foundry.Deploy;
using Serilog;

namespace Foundry.WinPE.Ui;

/// <summary>
/// Sequences one UI process: the Foundry Connect phase, then system preparation (clock and time zone), then the Foundry Deploy
/// phase, and returns the process exit code: Connect's when it does not succeed (20 user abort, 21 to 23 failures), otherwise
/// Deploy's.
/// </summary>
/// <remarks>
/// <para>
/// Runs on the UI thread of the host's WPF application; the phases resume there. Connect's deployment key, theme and language
/// reach Deploy in memory. Connect's window stays open and disabled until Deploy's first window rendered (or Deploy ended
/// without rendering), so the screen never goes blank between the phases.
/// </para>
/// <para>
/// Readiness: <c>configuration_loaded</c> comes from Connect, <c>ui_ready</c> once from the first window of the session
/// (Connect's, or Deploy's when Connect is skipped), and <c>startup_failed</c> when Connect fails before any window rendered.
/// The UI state file follows each phase and records the start of a destructive deployment step before that step runs.
/// System preparation runs only in Windows PE, because it sets the clock and time zone of the machine; its failure is a
/// warning. The hooks given to the phases log their own failures: the phases swallow hook exceptions.
/// </para>
/// </remarks>
/// <param name="connectPhase">Runs the Foundry Connect phase.</param>
/// <param name="systemPreparation">Prepares the clock and time zone.</param>
/// <param name="deployPhase">Runs the Foundry Deploy phase.</param>
/// <param name="stateWriter">Records the UI state for the guardian.</param>
/// <param name="readiness">Reports the startup stages to the guardian.</param>
/// <param name="isWinPeRuntime">Returns whether the process runs in Windows PE.</param>
/// <param name="logger">The session log.</param>
/// <param name="skipConnect">
/// Starts with the Deploy phase, without key, preferences or system preparation. Only the debug mode picker uses it.
/// </param>
internal sealed class WinPeUiSession(
    IConnectPhaseRunner connectPhase,
    ISystemPreparationStep systemPreparation,
    IDeployPhaseRunner deployPhase,
    IUiStateWriter stateWriter,
    IUiReadinessReporter readiness,
    Func<bool> isWinPeRuntime,
    ILogger logger,
    bool skipConnect = false)
{
    private readonly Lock _stateGate = new();
    private UiState _state = UiState.Initial;
    private int _uiReady;
    private int _phaseRunning;
    private IHandOverWindow? _connectWindow;

    /// <summary>
    /// Gets whether the Connect or Deploy phase is running. A phase handles the dispatcher exceptions raised while it runs; the
    /// host handles the others.
    /// </summary>
    public bool IsPhaseRunning => Volatile.Read(ref _phaseRunning) != 0;

    /// <summary>Runs the session and returns the UI process exit code.</summary>
    /// <param name="cancellationToken">
    /// Cancelled by the host when it ends the process: a running phase ends as its own cancellation defines, no further
    /// phase starts, and the key and Connect's window are still released.
    /// </param>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled before a phase started.</exception>
    public async Task<int> RunAsync(CancellationToken cancellationToken)
    {
        if (skipConnect)
        {
            logger.Information("Foundry Connect is skipped: the UI session starts with Foundry Deploy.");
            return await RunDeployAsync(connect: null, cancellationToken);
        }

        EnterPhase(UiPhase.Connect);
        ConnectPhaseOutcome connect = await RunPhaseAsync(() =>
            connectPhase.RunAsync(OnConnectConfigurationLoaded, OnConnectFirstWindowRendered, cancellationToken));
        _connectWindow = connect.Window;
        try
        {
            if (connect.ExitCode != (int)ChildRuntimeExitCode.Success)
            {
                if (connect.ExitCode is >= (int)ChildRuntimeExitCode.StartupFailure and <= (int)ChildRuntimeExitCode.RuntimeFailure &&
                    Volatile.Read(ref _uiReady) == 0)
                {
                    readiness.ReportStartupFailed(connect.ExitCode);
                }

                logger.Information("UI session ended in Foundry Connect with code {ExitCode}.", connect.ExitCode);
                return connect.ExitCode;
            }

            // A cancelled session (the host is ending the process) starts no further phase; the finally still runs.
            cancellationToken.ThrowIfCancellationRequested();
            EnterPhase(UiPhase.System);
            await PrepareSystemAsync(cancellationToken);
            return await RunDeployAsync(connect, cancellationToken);
        }
        finally
        {
            // Deploy disposes the key it received; this covers the paths where it never did. Connect's window is still open
            // when Deploy ended without rendering.
            connect.DeploymentKey?.Dispose();
            CloseConnectWindow();
        }
    }

    private async Task<int> RunDeployAsync(ConnectPhaseOutcome? connect, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnterPhase(UiPhase.Deploy);
        var options = new DeployPhaseOptions
        {
            InitialDeploymentKey = connect?.DeploymentKey,
            ThemeMode = connect?.ThemeMode,
            CultureName = connect?.CultureName,
            OnFirstWindowRendered = OnDeployFirstWindowRendered,
            OnDestructiveStepStarted = OnDestructiveStepStarted
        };
        int exitCode = await RunPhaseAsync(() => deployPhase.RunAsync(options, cancellationToken));
        logger.Information("UI session ended in Foundry Deploy with code {ExitCode}.", exitCode);
        return exitCode;
    }

    private async Task<T> RunPhaseAsync<T>(Func<Task<T>> run)
    {
        Volatile.Write(ref _phaseRunning, 1);
        try
        {
            return await run();
        }
        finally
        {
            Volatile.Write(ref _phaseRunning, 0);
        }
    }

    private async Task PrepareSystemAsync(CancellationToken cancellationToken)
    {
        if (!isWinPeRuntime())
        {
            logger.Information("System preparation (clock and time zone) was skipped outside Windows PE.");
            return;
        }

        logger.Information("System preparation (clock and time zone) started.");
        try
        {
            await systemPreparation.RunAsync(cancellationToken);
            logger.Information("System preparation completed.");
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            logger.Warning(exception, "System preparation failed. Foundry Deploy starts anyway.");
        }
    }

    private void EnterPhase(UiPhase phase)
    {
        lock (_stateGate)
        {
            _state = _state with { Phase = phase };
            stateWriter.Write(_state);
        }

        logger.Information("UI phase {Phase} started.", phase);
    }

    private void OnConnectConfigurationLoaded() => RunHook("configuration_loaded", readiness.ReportConfigurationLoaded);

    private void OnConnectFirstWindowRendered() => RunHook("connect_first_window", ReportUiReady);

    private void OnDeployFirstWindowRendered() => RunHook("deploy_first_window", () =>
    {
        CloseConnectWindow();
        if (skipConnect)
        {
            ReportUiReady();
        }
    });

    // Called on the deployment thread right before the step that can wipe the target disk; the record is on disk when it returns.
    private void OnDestructiveStepStarted() => RunHook("destructive_step", () =>
    {
        lock (_stateGate)
        {
            _state = _state with { DestructiveStarted = true };
            stateWriter.Write(_state);
        }

        logger.Information("A destructive deployment step started: the target disk can be modified from now on.");
    });

    private void ReportUiReady()
    {
        if (Interlocked.Exchange(ref _uiReady, 1) == 0)
        {
            readiness.ReportUiReady();
            logger.Information("The UI session rendered its first window.");
        }
    }

    private void CloseConnectWindow()
    {
        IHandOverWindow? window = Interlocked.Exchange(ref _connectWindow, null);
        if (window is null)
        {
            return;
        }

        try
        {
            window.Close();
            logger.Debug("Foundry Connect window closed after the hand-over to Foundry Deploy.");
        }
        catch (Exception exception)
        {
            logger.Warning("Foundry Connect window could not be closed. FailureType={FailureType}, ErrorCode={ErrorCode}",
                exception.GetType().Name, exception.HResult);
        }
    }

    private void RunHook(string hook, Action action)
    {
        try
        {
            action();
        }
        catch (Exception exception)
        {
            logger.Warning("UI session hook {Hook} failed. FailureType={FailureType}, ErrorCode={ErrorCode}",
                hook, exception.GetType().Name, exception.HResult);
        }
    }
}
