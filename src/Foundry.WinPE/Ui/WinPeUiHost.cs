// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Reflection;
using System.Windows;
using Foundry.Connect;
using Foundry.Core.Models.Runtime;
using Foundry.Core.Services.Runtime;
using Foundry.Deploy;
using Foundry.Telemetry;
using Foundry.Utilities.Diagnostics;
using Foundry.Utilities.Runtime;
using Foundry.WinPE.SystemPreparation;
using Serilog;

namespace Foundry.WinPE.Ui;

/// <summary>
/// The <c>--ui</c> mode: one WPF <see cref="Application"/> on the calling STA thread, in which <see cref="WinPeUiSession"/>
/// runs the Foundry Connect phase, system preparation and the Foundry Deploy phase.
/// </summary>
/// <remarks>
/// <para>
/// The process-wide setup is done once here: diagnostic clock, readiness protocol (<c>managed_started</c> under
/// <see cref="UiReadinessReporter.ApplicationId"/>), host log (<see cref="UiHostLog"/>), global exception handlers, remote
/// diagnostics (<see cref="UiRemoteDiagnostics"/>), the single-instance mutex <see cref="SingleInstanceGuard.UiInstanceName"/>,
/// the Fluent backdrop switch and the application resources (<see cref="ResourcesUri"/>).
/// </para>
/// <para>
/// The application uses <see cref="ShutdownMode.OnExplicitShutdown"/>: closing a phase window never ends the process. The
/// session runs from <see cref="Application.Startup"/> and the host calls <see cref="Application.Shutdown(int)"/> with its
/// result as soon as it returns. That also ends a Deploy phase that failed while a deployment was running, whose window
/// refused to close and whose services are already disposed: application shutdown closes the window without asking it.
/// </para>
/// <para>
/// A dispatcher exception raised while a phase runs is the phase's (it ends the phase, Connect with 21 or 23, Deploy with 1).
/// One raised outside a phase is logged and ends the process with <see cref="UnhandledExceptionExitCode"/>, which the guardian
/// treats as a crash; nothing is recorded as destructive.
/// </para>
/// </remarks>
internal static class WinPeUiHost
{
    /// <summary>
    /// Exit code of a UI process ended by an unhandled exception outside the phases. It is none of the codes the guardian reads
    /// as an outcome (0, 20, 21, 22, 23), so the guardian treats it as a crash.
    /// </summary>
    internal const int UnhandledExceptionExitCode = 30;

    /// <summary>The application resources of the UI process.</summary>
    internal const string ResourcesUri = "pack://application:,,,/Foundry.WinPE;component/Ui/WinPeApplicationResources.xaml";

    private const string WinPeRoot = WinPeWorkspaceLayout.Root;
    private const string DisableFluentBackdropSwitch = "Switch.System.Windows.Appearance.DisableFluentThemeWindowBackdrop";
    private const string DisableFluentBackdropEnvironmentVariable = "FOUNDRY_DISABLE_FLUENT_BACKDROP";

    /// <summary>Runs the UI process and returns its exit code. Must run on the STA entry thread.</summary>
    /// <param name="skipConnect">Starts with Foundry Deploy; only the debug mode picker passes <see langword="true"/>.</param>
    internal static int Run(bool skipConnect)
    {
        DiagnosticClock.Current.InitializeRuntime(Environment.GetEnvironmentVariable(DiagnosticClock.EnvironmentVariableName));
        UiReadinessReporter readiness = UiReadinessReporter.Start();
        UiHostLog.Use(skipConnect ? UiHostLog.DeployLogFileName : UiHostLog.ConnectLogFileName);
        ILogger logger = UiHostLog.CreateLogger(typeof(WinPeUiHost));
        RegisterGlobalExceptionHandlers(readiness, logger);
        UiRemoteDiagnostics diagnostics = UiRemoteDiagnostics.FromConfiguration();
        if (skipConnect) diagnostics.Activate(TelemetryApps.FoundryDeploy, FoundryDeployApplicationInfo.Version);
        else diagnostics.Activate(TelemetryApps.FoundryConnect, FoundryConnectApplicationInfo.Version);
        logger.Information("Foundry UI process started. Version={Version}, SessionId={SessionId}, SkipConnect={SkipConnect}",
            typeof(WinPeUiHost).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion,
            DiagnosticSessionContext.CurrentSessionId, skipConnect);
        SingleInstanceGuard? instanceGuard = null;
        try
        {
            if (!SingleInstanceGuard.TryAcquire(SingleInstanceGuard.UiInstanceName, out instanceGuard))
            {
                logger.Error("Another Foundry UI process is already running.");
                readiness.ReportStartupFailed((int)ChildRuntimeExitCode.StartupFailure);
                return (int)ChildRuntimeExitCode.StartupFailure;
            }

            ConfigureFluentBackdrop(logger);
            int exitCode = RunApplication(skipConnect, readiness, diagnostics, logger);
            logger.Information("Foundry UI process ended with code {ExitCode}.", exitCode);
            return exitCode;
        }
        catch (Exception exception)
        {
            // Setup failed before or while the application started: no window can have rendered.
            logger.Fatal(exception, "Foundry UI process could not start.");
            readiness.ReportStartupFailed((int)ChildRuntimeExitCode.StartupFailure);
            return (int)ChildRuntimeExitCode.StartupFailure;
        }
        finally
        {
            // The dispatcher has stopped: the shutdown runs on the thread pool so its awaits never target this thread.
            try { Task.Run(diagnostics.ShutdownAsync).Wait(HostedPhaseScope.ShutdownBudget * 2); }
            catch { }
            UiHostLog.Close();
            // Released last, on the thread that acquired it, so a next instance cannot start while this one still flushes.
            try { instanceGuard?.Dispose(); }
            catch { }
        }
    }

    private static int RunApplication(bool skipConnect, UiReadinessReporter readiness, UiRemoteDiagnostics diagnostics,
        ILogger logger)
    {
        // Infinite timeout: the clock and time zone probes apply their own linked deadlines.
        using var httpClient = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        ILogger sessionLogger = UiHostLog.CreateLogger(typeof(WinPeUiSession));
        var session = new WinPeUiSession(
            new WpfConnectPhaseRunner(),
            new WinPeSystemPreparationStep(new WinPeSystemPreparation(WinPeRoot, httpClient,
                UiHostLog.CreateLogger(typeof(WinPeSystemPreparation)))),
            new WpfDeployPhaseRunner(() =>
            {
                UiHostLog.Use(UiHostLog.DeployLogFileName);
                diagnostics.Activate(TelemetryApps.FoundryDeploy, FoundryDeployApplicationInfo.Version);
            }),
            UiStateFileWriter.FromEnvironment(sessionLogger),
            readiness,
            WinPeRuntimeDetector.IsWinPeRuntime,
            sessionLogger,
            skipConnect);

        var application = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        application.Resources = new ResourceDictionary { Source = new Uri(ResourcesUri, UriKind.Absolute) };
        // Subscribed before any phase: every handler runs, so this one acts only when no phase owns the exception.
        application.DispatcherUnhandledException += (_, eventArgs) =>
        {
            if (session.IsPhaseRunning)
            {
                return;
            }

            logger.Fatal(eventArgs.Exception, "Unhandled UI exception outside the Connect and Deploy phases. The UI process ends.");
            eventArgs.Handled = true;
            application.Shutdown(UnhandledExceptionExitCode);
        };
        application.Startup += async (_, _) =>
        {
            int exitCode;
            try
            {
                exitCode = await session.RunAsync(CancellationToken.None);
            }
            catch (Exception exception)
            {
                logger.Fatal(exception, "The UI session failed. The UI process ends.");
                exitCode = UnhandledExceptionExitCode;
            }

            // The first Shutdown wins: an earlier one from the dispatcher handler keeps its crash code.
            application.Shutdown(exitCode);
        };
        return application.Run();
    }

    private static void RegisterGlobalExceptionHandlers(UiReadinessReporter readiness, ILogger logger)
    {
        string? evidenceDirectory = readiness.StatusDirectory ?? UiHostLog.LogDirectory;
        string? evidencePath = evidenceDirectory is null ? null : Path.Combine(evidenceDirectory, "FoundryWinPE-terminated.txt");
        AppDomain.CurrentDomain.UnhandledException += (_, eventArgs) =>
        {
            // Minimal local evidence only: logging, services and transport may be unusable while the process terminates.
            try
            {
                if (evidencePath is not null)
                {
                    File.WriteAllText(evidencePath, (eventArgs.ExceptionObject as Exception)?.GetType().FullName
                        ?? "Unhandled terminating exception");
                }
            }
            catch { }
        };
        TaskScheduler.UnobservedTaskException += (_, eventArgs) =>
        {
            // Kept with its exception object so error tracking receives the stack trace of this unexpected failure.
            logger.Error(eventArgs.Exception, "Unobserved task exception.");
            eventArgs.SetObserved();
        };
    }

    private static void ConfigureFluentBackdrop(ILogger logger)
    {
        string? overrideValue = Environment.GetEnvironmentVariable(DisableFluentBackdropEnvironmentVariable);
        // WinPE does not provide the desktop composition stack the Fluent window backdrop needs.
        bool disable = string.IsNullOrWhiteSpace(overrideValue)
            ? WinPeRuntimeDetector.IsWinPeRuntime()
            : overrideValue.Trim().ToLowerInvariant() is "1" or "true" or "yes" or "on";
        if (!disable)
        {
            return;
        }

        AppContext.SetSwitch(DisableFluentBackdropSwitch, true);
        logger.Debug("Enabled '{SwitchName}'.", DisableFluentBackdropSwitch);
    }
}
