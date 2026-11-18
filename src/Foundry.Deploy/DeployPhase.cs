// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.IO;
using System.Windows;
using System.Windows.Threading;
using Foundry.Core.Models.Runtime;
using Foundry.Core.Services.Runtime;
using Foundry.Deploy.DependencyInjection;
using Foundry.Deploy.Services.Configuration;
using Foundry.Deploy.Services.Logging;
using Foundry.Deploy.ViewModels;
using Foundry.Telemetry;
using Foundry.Utilities.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;

namespace Foundry.Deploy;

/// <summary>
/// Runs Foundry Deploy as the last phase of the Foundry.WinPE UI process: inside the host's WPF <see cref="Application"/>, with
/// its own log file (<c>FoundryDeploy.log</c>) and DI container, and ends with an exit code instead of shutting the
/// application down.
/// </summary>
/// <remarks>
/// The host has already done the process-wide setup: diagnostic clock, global exception handlers, Fluent backdrop switch,
/// remote diagnostics, single-instance mutex, the startup protocol, and the application resources, with
/// <c>/Foundry.Deploy;component/Themes/DeployApplicationResources.xaml</c> merged after Fluent and the Foundry.Wpf styles.
/// While the phase runs, it owns <see cref="Log.Logger"/> (through <see cref="HostedPhaseScope"/>),
/// <see cref="Application.MainWindow"/> and a handler of
/// <see cref="Application.DispatcherUnhandledException"/>, which marks the exception handled and ends the phase with
/// <see cref="UnhandledExceptionExitCode"/>. A startup or configuration failure ends it with the <see cref="ChildRuntimeExitCode"/>
/// failure codes that Foundry Connect uses (see <see cref="FailureExitCodeFor"/>), so the guardian reports it as a failure of
/// the phase rather than a crash.
/// </remarks>
public static class DeployPhase
{
    /// <summary>Exit code of a phase that ended normally: its window was closed, or it ended before the wizard by design.</summary>
    public const int SuccessExitCode = 0;

    /// <summary>Exit code of a phase ended by an unhandled UI exception: the guardian treats it as a crash.</summary>
    public const int UnhandledExceptionExitCode = 1;

    private static readonly RuntimeApplicationIdentity Identity = new("Foundry.Deploy", TelemetryApps.FoundryDeploy, FoundryDeployApplicationInfo.Version);

    /// <summary>
    /// Shows Deploy's main window with the Connect theme and language and completes when the window closes or the phase
    /// fails. Must be called on the application's UI thread. <see cref="DeployPhaseOptions.InitialDeploymentKey"/> is disposed
    /// on every path.
    /// </summary>
    /// <param name="options">The Connect hand-over and the host hooks.</param>
    /// <param name="cancellationToken">
    /// Asks a running phase to close its window, as the operator would; a running deployment keeps the window open and is
    /// asked to cancel instead.
    /// </param>
    /// <returns><see cref="SuccessExitCode"/>, a failure code of <see cref="FailureExitCodeFor"/> or <see cref="UnhandledExceptionExitCode"/>.</returns>
    /// <exception cref="InvalidOperationException">No WPF application runs on the calling thread.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled before the phase started.</exception>
    public static async Task<int> RunAsync(DeployPhaseOptions options, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);
        try
        {
            Application application = Application.Current
                ?? throw new InvalidOperationException("The Foundry Deploy phase runs inside a WPF application.");
            application.Dispatcher.VerifyAccess();
            cancellationToken.ThrowIfCancellationRequested();
            return await RunCoreAsync(application, options, cancellationToken);
        }
        finally
        {
            options.InitialDeploymentKey?.Dispose();
        }
    }

    private static async Task<int> RunCoreAsync(Application application, DeployPhaseOptions options, CancellationToken cancellationToken)
    {
        // The first render of the main window or of a startup dialog, whichever comes first, lets the host close Connect's window.
        int firstWindowRendered = 0;
        void OnFirstWindowRendered()
        {
            if (Interlocked.Exchange(ref firstWindowRendered, 1) == 0)
            {
                options.OnFirstWindowRendered();
            }
        }

        RuntimeStartupDiagnostics startup = RuntimeStartupDiagnostics.CreateForHostedPhase(Identity, OnFirstWindowRendered);
        HostedPhaseScope scope = HostedPhaseScope.Begin(Identity, "Foundry Deploy", typeof(DeployPhase),
            FoundryDeployLogging.ResolveStartupLogFilePath, FoundryDeployLogging.CreateLogger);
        Serilog.ILogger logger = scope.Logger;
        IHost? host = null;
        ITelemetryService? telemetry = null;
        DispatcherUnhandledExceptionEventHandler? dispatcherHandler = null;
        string stage = "managed_startup";
        try
        {
            logger.Information(
                "Foundry Deploy phase started. Version={Version}, SessionId={SessionId}, LogFilePath={LogFilePath}",
                FoundryDeployApplicationInfo.Version, DiagnosticSessionContext.CurrentSessionId, scope.LogFilePath);
            if (!RuntimeStartupGuard.CanRun())
                throw new InvalidOperationException("Foundry Deploy requires Windows PE outside a DEBUG debugger session.");

            stage = "configuration";
            host = BuildHost(startup, options);
            DeployConfigurationLoadResult configuration = host.Services.GetRequiredService<IDeployConfigurationService>().LoadOptional();
            if (configuration.Exists && configuration.Document is null)
            {
                startup.ReportFailure(configuration.FailureException ?? new InvalidDataException("Deployment configuration is invalid."), stage);
                return FailureExitCodeFor(stage);
            }

            stage = "services";
            telemetry = host.Services.GetRequiredService<ITelemetryService>();

            stage = "ui_startup";
            DeployPhaseLifetime lifetime = host.Services.GetRequiredService<DeployPhaseLifetime>();
            dispatcherHandler = (_, eventArgs) =>
            {
                startup.ReportFailure(eventArgs.Exception, "dispatcher");
                eventArgs.Handled = true;
                lifetime.TryEnd(FailureExitCodeFor("dispatcher"));
            };
            application.DispatcherUnhandledException += dispatcherHandler;

            // Before the window is resolved, so its first frame and its menus already use Connect's theme and language.
            host.Services.GetRequiredService<MainWindowViewModel>().ApplyInitialPreferences(options.ThemeMode, options.CultureName);
            MainWindow window = host.Services.GetRequiredService<MainWindow>();
            bool isWindowClosed = false;
            window.ContentRendered += (_, _) => OnFirstWindowRendered();
            window.Closed += (_, _) =>
            {
                isWindowClosed = true;
                lifetime.TryEnd(SuccessExitCode);
            };
            using CancellationTokenRegistration cancellation = cancellationToken.Register(() =>
                application.Dispatcher.BeginInvoke(() =>
                {
                    if (!isWindowClosed) window.Close();
                }));
            // Owner fallbacks (About, prompts, message dialogs) read Application.MainWindow: point it at this phase's window.
            application.MainWindow = window;
            window.Show();

            int exitCode = await lifetime.Completion;
            logger.Information("Foundry Deploy phase ended with code {ExitCode}.", exitCode);
            if (!isWindowClosed)
            {
                // A running deployment cancels this close and requests the deployment's cancellation instead.
                window.Close();
            }

            return exitCode;
        }
        catch (Exception exception)
        {
            startup.ReportFailure(exception, stage);
            return FailureExitCodeFor(stage);
        }
        finally
        {
            if (dispatcherHandler is not null)
            {
                application.DispatcherUnhandledException -= dispatcherHandler;
            }

            await scope.EndAsync(telemetry, host);
            try { FoundryDeployLogging.PersistCurrentLogs(); }
            catch { }
        }
    }

    /// <summary>
    /// Maps the stage a phase failed at to its exit code: <see cref="ChildRuntimeExitCode.ConfigurationFailure"/> for the
    /// configuration, <see cref="UnhandledExceptionExitCode"/> for an unhandled UI exception (<c>dispatcher</c>), and
    /// <see cref="ChildRuntimeExitCode.StartupFailure"/> for the other startup stages (managed startup, services, window
    /// startup and initialization).
    /// </summary>
    internal static int FailureExitCodeFor(string stage) => stage switch
    {
        "configuration" => (int)ChildRuntimeExitCode.ConfigurationFailure,
        "dispatcher" => UnhandledExceptionExitCode,
        _ => (int)ChildRuntimeExitCode.StartupFailure
    };

    private static IHost BuildHost(RuntimeStartupDiagnostics startup, DeployPhaseOptions options)
    {
        // Kept here: Foundry.Core does not reference Microsoft.Extensions.Hosting or Serilog.Extensions.Logging.
        HostApplicationBuilder builder = Host.CreateApplicationBuilder();
        builder.Logging.ClearProviders();
        builder.Logging.SetMinimumLevel(LogLevel.Trace);
        builder.Logging.AddSerilog(dispose: false);
        builder.Services.AddSingleton(startup);
        builder.Services.AddFoundryDeployApplicationServices(options);
        return builder.Build();
    }
}
