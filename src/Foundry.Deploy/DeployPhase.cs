// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.IO;
using System.Windows;
using System.Windows.Threading;
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
/// While the phase runs, it owns <see cref="Log.Logger"/>, <see cref="Application.MainWindow"/> and a handler of
/// <see cref="Application.DispatcherUnhandledException"/>, which marks the exception handled and ends the phase with
/// <see cref="FailureExitCode"/>.
/// </remarks>
public static class DeployPhase
{
    /// <summary>Exit code of a phase that ended normally: its window was closed, or it ended before the wizard by design.</summary>
    public const int SuccessExitCode = 0;

    /// <summary>Exit code of a phase that failed: invalid configuration, a startup failure or an unhandled UI exception.</summary>
    public const int FailureExitCode = 1;

    private static readonly RuntimeApplicationIdentity Identity = new("Foundry.Deploy", TelemetryApps.FoundryDeploy, FoundryDeployApplicationInfo.Version);
    private static readonly TimeSpan DiagnosticsShutdownTimeout = TimeSpan.FromSeconds(2);

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
    /// <returns><see cref="SuccessExitCode"/> or <see cref="FailureExitCode"/>.</returns>
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
        Serilog.ILogger previousLogger = Log.Logger;
        Serilog.ILogger phaseLogger = CreatePhaseLogger(out string logFilePath);
        Log.Logger = phaseLogger;
        Serilog.ILogger logger = Log.ForContext(typeof(DeployPhase));
        IHost? host = null;
        ITelemetryService? telemetry = null;
        DispatcherUnhandledExceptionEventHandler? dispatcherHandler = null;
        string stage = "managed_startup";
        try
        {
            logger.Information(
                "Foundry Deploy phase started. Version={Version}, SessionId={SessionId}, LogFilePath={LogFilePath}",
                FoundryDeployApplicationInfo.Version, DiagnosticSessionContext.CurrentSessionId, logFilePath);
            if (!RuntimeStartupGuard.CanRun())
                throw new InvalidOperationException("Foundry Deploy requires Windows PE outside a DEBUG debugger session.");

            stage = "configuration";
            host = BuildHost(startup, options);
            DeployConfigurationLoadResult configuration = host.Services.GetRequiredService<IDeployConfigurationService>().LoadOptional();
            if (configuration.Exists && configuration.Document is null)
            {
                startup.ReportFailure(configuration.FailureException ?? new InvalidDataException("Deployment configuration is invalid."), stage);
                return FailureExitCode;
            }

            stage = "services";
            telemetry = host.Services.GetRequiredService<ITelemetryService>();

            stage = "ui_startup";
            DeployPhaseLifetime lifetime = host.Services.GetRequiredService<DeployPhaseLifetime>();
            dispatcherHandler = (_, eventArgs) =>
            {
                startup.ReportFailure(eventArgs.Exception, "dispatcher");
                eventArgs.Handled = true;
                lifetime.TryEnd(FailureExitCode);
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
            return FailureExitCode;
        }
        finally
        {
            if (dispatcherHandler is not null)
            {
                application.DispatcherUnhandledException -= dispatcherHandler;
            }

            await FlushTelemetryAsync(telemetry, logger);
            if (host is not null)
            {
                await DisposeHostAsync(host, logger);
            }

            logger.Information("Foundry Deploy phase shutdown completed. Closing its log.");
            Log.Logger = previousLogger;
            await CloseLoggerAsync(phaseLogger);
            try { FoundryDeployLogging.PersistCurrentLogs(); }
            catch { }
        }
    }

    private static Serilog.ILogger CreatePhaseLogger(out string logFilePath)
    {
        try
        {
            logFilePath = FoundryDeployLogging.ResolveStartupLogFilePath();
            return FoundryDeployLogging.CreateLogger(logFilePath);
        }
        catch (Exception exception)
        {
            logFilePath = "<unavailable>";
            Serilog.ILogger fallback = FoundryLogConfiguration.CreateDebugLogger(
                Identity.ApplicationId, DiagnosticSessionContext.CurrentSessionId,
                Serilog.Events.LogEventLevel.Verbose, additionalSink: RemoteDiagnosticsSink.Instance);
            fallback.ForContext(typeof(DeployPhase)).Error(exception, "File logging initialization failed. Falling back to debugger output.");
            return fallback;
        }
    }

    private static IHost BuildHost(RuntimeStartupDiagnostics startup, DeployPhaseOptions options)
    {
        HostApplicationBuilder builder = Host.CreateApplicationBuilder();
        builder.Logging.ClearProviders();
        builder.Logging.SetMinimumLevel(LogLevel.Trace);
        builder.Logging.AddSerilog(dispose: false);
        builder.Services.AddSingleton(startup);
        builder.Services.AddFoundryDeployApplicationServices(options);
        return builder.Build();
    }

    /// <summary>Sends pending product analytics within the shutdown budget; the remote log stream belongs to the host.</summary>
    private static async Task FlushTelemetryAsync(ITelemetryService? telemetry, Serilog.ILogger logger)
    {
        if (telemetry is null)
        {
            return;
        }

        try
        {
            await Task.Run(() => telemetry.FlushAsync()).WaitAsync(DiagnosticsShutdownTimeout);
        }
        catch (Exception exception)
        {
            logger.ForContext("PostHogTransportInternal", true).Warning(exception, "Telemetry flush did not complete within the shutdown budget.");
        }
    }

    /// <summary>
    /// Disposes the phase's container on the UI thread, which owns the view models it disposes; a disposal failure never
    /// changes the phase result.
    /// </summary>
    private static async Task DisposeHostAsync(IHost host, Serilog.ILogger logger)
    {
        try
        {
            if (host is IAsyncDisposable asyncDisposable)
            {
                await asyncDisposable.DisposeAsync();
            }
            else
            {
                host.Dispose();
            }
        }
        catch (Exception exception)
        {
            logger.Warning(exception, "Foundry Deploy service disposal failed.");
        }
    }

    private static async Task CloseLoggerAsync(Serilog.ILogger logger)
    {
        if (logger is not IAsyncDisposable disposable)
        {
            return;
        }

        try
        {
            await Task.Run(() => disposable.DisposeAsync().AsTask()).WaitAsync(DiagnosticsShutdownTimeout);
        }
        catch
        {
            // Log closing is best effort: the phase outcome is already decided.
        }
    }
}
