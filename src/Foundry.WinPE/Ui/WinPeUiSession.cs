// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Globalization;
using System.Windows;
using Foundry.Connect;
using Foundry.Core.Models.Runtime;
using Foundry.Deploy;
using Foundry.Wpf.Shell;
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
/// reach Deploy in memory. Both phases show their content in the one shell the host gives the session: Connect's content stays
/// shown, disabled, until the next content replaces it (the status page while the system is prepared, then Deploy's content),
/// so the screen never goes blank between the phases. The session never closes the shell and handles none of its close requests: a phase handles
/// them while it runs, and the shell refuses them in between (while system preparation runs).
/// </para>
/// <para>
/// Readiness: <c>configuration_loaded</c> comes from Connect, <c>ui_ready</c> once from the first content of the session
/// (Connect's page or its password prompt, or Deploy's content when Connect is skipped), and <c>startup_failed</c> when Connect
/// fails before any content rendered.
/// The UI state file follows each phase, the language the operator works in, and the start of a destructive deployment step
/// (before that step runs).
/// System preparation runs only in Windows PE, because it sets the clock and time zone of the machine; its failure is a
/// warning. It runs behind the shared status page ("Setting the clock and time zone…", at least
/// <see cref="StatusPageMinimumDisplay"/>), and whether the clock is usable afterwards is recorded in the Deploy-phase state
/// the session writes next, so the guardian sees it before it starts its telemetry delivery. The hooks given to the phases log their own failures: the phases swallow hook exceptions.
/// </para>
/// </remarks>
/// <param name="shell">The shell window both phases show their content in, created by the host.</param>
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
/// <param name="createStatusPage">
/// Creates the status page for a model; by default the shared <see cref="StatusPageView"/>. Replaced in tests, which have no UI
/// thread.
/// </param>
/// <param name="timeProvider">The clock of the status page minimum display time; the system clock by default.</param>
internal sealed class WinPeUiSession(
    IShellHost shell,
    IConnectPhaseRunner connectPhase,
    ISystemPreparationStep systemPreparation,
    IDeployPhaseRunner deployPhase,
    IUiStateWriter stateWriter,
    IUiReadinessReporter readiness,
    Func<bool> isWinPeRuntime,
    ILogger logger,
    bool skipConnect = false,
    Func<StatusPageModel, FrameworkElement>? createStatusPage = null,
    TimeProvider? timeProvider = null)
{
    /// <summary>The minimum time the status page stays on screen, so a fast step does not flash.</summary>
    internal static readonly TimeSpan StatusPageMinimumDisplay = TimeSpan.FromSeconds(1);

    private const string StatusPageTitle = "Foundry OSD";
    private const string StatusPageStatusKey = "SystemPreparation.Status";
    private const string StatusPageStatusEnglish = "Setting the clock and time zone…";

    private readonly Func<StatusPageModel, FrameworkElement> _createStatusPage =
        createStatusPage ?? (static model => new StatusPageView { DataContext = model });
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

    private readonly Lock _stateGate = new();
    private UiState _state = UiState.Initial;
    private int _uiReady;
    private int _phaseRunning;

    /// <summary>
    /// Gets whether the Connect or Deploy phase is running. A phase handles the dispatcher exceptions raised while it runs; the
    /// host handles the others.
    /// </summary>
    public bool IsPhaseRunning => Volatile.Read(ref _phaseRunning) != 0;

    /// <summary>Runs the session and returns the UI process exit code.</summary>
    /// <param name="cancellationToken">
    /// Cancelled by the host when it ends the process: a running phase ends as its own cancellation defines, no further
    /// phase starts, and the deployment key is still released.
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
        ConnectPhaseResult connect = await RunPhaseAsync(() => connectPhase.RunAsync(new ConnectPhaseOptions
        {
            Shell = shell,
            OnConfigurationLoaded = OnConnectConfigurationLoaded,
            OnCultureChanged = OnCultureChanged,
            OnFirstContentRendered = OnConnectFirstContentRendered
        }, cancellationToken));
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
            // No close handler from here until Deploy attaches its own: the shell refuses a close while the system is prepared.
            EnterPhase(UiPhase.System);
            await PrepareSystemAsync(cancellationToken);
            return await RunDeployAsync(connect, cancellationToken);
        }
        finally
        {
            // Deploy disposes the key it received; this covers the paths where it never did.
            connect.DeploymentKey?.Dispose();
        }
    }

    private async Task<int> RunDeployAsync(ConnectPhaseResult? connect, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnterPhase(UiPhase.Deploy, connect?.CultureName);
        var options = new DeployPhaseOptions
        {
            Shell = shell,
            InitialDeploymentKey = connect?.DeploymentKey,
            ThemeMode = connect?.ThemeMode,
            CultureName = connect?.CultureName,
            OnFirstContentRendered = OnDeployFirstContentRendered,
            OnDestructiveStepStarted = OnDestructiveStepStarted,
            OnCultureChanged = OnCultureChanged
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
        bool clockUsable = false;
        try
        {
            shell.ShowContent(_createStatusPage(CreateStatusPageModel()));
            Task<bool> run = systemPreparation.RunAsync(cancellationToken);
            await StatusPageModel.EnsureMinimumDisplayAsync(run, StatusPageMinimumDisplay, _timeProvider);
            clockUsable = await run;
            logger.Information("System preparation completed. ClockUsable={ClockUsable}", clockUsable);
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            logger.Warning(exception, "System preparation failed. Foundry Deploy starts anyway.");
        }

        // Written with the Deploy phase state that follows: the guardian reads it before it starts its telemetry delivery.
        lock (_stateGate)
        {
            _state = _state with { ClockUsable = clockUsable };
        }
    }

    private StatusPageModel CreateStatusPageModel()
    {
        CultureInfo culture = UiStateFile.TryGetSupportedCulture(_state.Culture) ?? CultureInfo.CurrentUICulture;
        return new StatusPageModel(StatusPageTitle,
            WinPeDialogApplication.Localize(StatusPageStatusKey, StatusPageStatusEnglish, culture));
    }

    private void EnterPhase(UiPhase phase, string? connectCulture = null)
    {
        lock (_stateGate)
        {
            // Deploy starts in the language Connect ended with, before it reports its own.
            _state = _state with { Phase = phase, Culture = connectCulture ?? _state.Culture };
            stateWriter.Write(_state);
        }

        logger.Information("UI phase {Phase} started.", phase);
    }

    // Called from each phase's localization service, at its start and after a language change: the guardian shows its crash
    // window in the language the operator works in.
    private void OnCultureChanged(string culture) => RunHook("culture_changed", () =>
    {
        lock (_stateGate)
        {
            _state = _state with { Culture = culture };
            stateWriter.Write(_state);
        }
    });

    private void OnConnectConfigurationLoaded() => RunHook("configuration_loaded", readiness.ReportConfigurationLoaded);

    private void OnConnectFirstContentRendered() => RunHook("connect_first_content", ReportUiReady);

    // Called once Deploy's own content rendered; it decides readiness only when Connect was skipped: after Connect, Connect's
    // first content already reported it and this call changes nothing.
    private void OnDeployFirstContentRendered() => RunHook("deploy_first_content", ReportUiReady);

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
            logger.Information("The UI session rendered its first content.");
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
