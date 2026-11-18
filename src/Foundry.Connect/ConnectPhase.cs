// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Globalization;
using System.Windows;
using System.Windows.Threading;
using Foundry.Connect.Configuration;
using Foundry.Connect.DependencyInjection;
using Foundry.Connect.Lifetime;
using Foundry.Connect.Security;
using Foundry.Connect.Services.Logging;
using Foundry.Connect.Startup;
using Foundry.Core.Models.Configuration;
using Foundry.Core.Models.Runtime;
using Foundry.Core.Services.Runtime;
using Foundry.Telemetry;
using Foundry.Utilities.Diagnostics;
using Foundry.Wpf.Localization;
using Foundry.Wpf.Theme;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Extensions.Logging;

namespace Foundry.Connect;

/// <summary>
/// Runs Foundry Connect as the first phase of the Foundry.WinPE UI process: inside the host's WPF <see cref="Application"/>,
/// with its own log file (<c>FoundryConnect.log</c>), configuration and DI container, and ends with a result instead of
/// shutting the application down.
/// </summary>
/// <remarks>
/// The host has already done the process-wide setup: diagnostic clock, global exception handlers, Fluent backdrop switch,
/// remote diagnostics, single-instance mutex, the startup protocol, and the application resources, with
/// <c>/Foundry.Connect;component/Themes/ConnectApplicationResources.xaml</c> merged after Fluent and the Foundry.Wpf styles.
/// While the phase runs, it owns <see cref="Log.Logger"/>, <see cref="Application.MainWindow"/> and a handler of
/// <see cref="Application.DispatcherUnhandledException"/>, which marks the exception handled and ends the phase.
/// </remarks>
public static class ConnectPhase
{
    private static readonly RuntimeApplicationIdentity Identity = new("Foundry.Connect", TelemetryApps.FoundryConnect, FoundryConnectApplicationInfo.Version);
    private static readonly TimeSpan DiagnosticsShutdownTimeout = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Loads the configuration, shows Connect's main window and completes when Connect's lifetime ends: Continue or
    /// auto-continue (success), closing the window (user abort), or a failure. Must be called on the application's UI thread.
    /// </summary>
    /// <param name="options">The host hooks.</param>
    /// <param name="cancellationToken">Ends a running phase as a user abort, like closing its window.</param>
    /// <returns>The phase result; its deployment key and window are handed to the caller.</returns>
    /// <exception cref="InvalidOperationException">No WPF application runs on the calling thread.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled before the phase started.</exception>
    public static async Task<ConnectPhaseResult> RunAsync(ConnectPhaseOptions options, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);
        Application application = Application.Current
            ?? throw new InvalidOperationException("The Foundry Connect phase runs inside a WPF application.");
        application.Dispatcher.VerifyAccess();
        cancellationToken.ThrowIfCancellationRequested();

        RuntimeStartupDiagnostics startup = RuntimeStartupDiagnostics.CreateForHostedPhase(Identity, options.OnFirstWindowRendered);
        Serilog.ILogger previousLogger = Log.Logger;
        Serilog.ILogger phaseLogger = CreatePhaseLogger(out string logFilePath);
        Log.Logger = phaseLogger;
        Serilog.ILogger logger = Log.ForContext(typeof(ConnectPhase));
        IHost? host = null;
        ITelemetryService? telemetry = null;
        ApplicationLifetimeService? lifetime = null;
        DispatcherUnhandledExceptionEventHandler? dispatcherHandler = null;
        string category = "managed_startup";
        // Managed startup (logging, runtime checks) precedes the policy stages.
        ConnectStartupStage? stage = null;
        try
        {
            logger.Information(
                "Foundry Connect phase started. Version={Version}, SessionId={SessionId}, LogFilePath={LogFilePath}",
                FoundryConnectApplicationInfo.Version, DiagnosticSessionContext.CurrentSessionId, logFilePath);
            if (!RuntimeStartupGuard.CanRun())
                throw new InvalidOperationException("Foundry Connect requires Windows PE outside a DEBUG debugger session.");

            category = "configuration";
            stage = ConnectStartupStage.Configuration;
            var configurationService = new ConnectConfigurationService(
                new SerilogLoggerFactory(Log.Logger).CreateLogger<ConnectConfigurationService>());
            ConnectConfigurationLoadResult loadResult = configurationService.Load();
            if (!loadResult.IsSuccess)
            {
                startup.ReportFailure(loadResult.CreateDiagnosticFailure(), category);
                return CreateEndedResult((int)ConnectStartupFailurePolicy.Map(ConnectStartupStage.Configuration), services: null);
            }

            // Building the host keeps the "configuration" category but is not a configuration failure for the exit code.
            stage = ConnectStartupStage.Services;
            host = BuildHost(startup, configurationService, loadResult.Configuration, loadResult.Secrets);
            category = "services";
            telemetry = host.Services.GetRequiredService<ITelemetryService>();

            category = "ui_startup";
            stage = ConnectStartupStage.UiStartup;
            ApplicationLifetimeService exitAuthority = host.Services.GetRequiredService<ApplicationLifetimeService>();
            lifetime = exitAuthority;
            dispatcherHandler = (_, eventArgs) =>
            {
                startup.ReportFailure(eventArgs.Exception, "dispatcher");
                eventArgs.Handled = true;
                exitAuthority.TryExit(GetFailureExitCode(ConnectStartupStage.UiStartup, startup));
            };
            application.DispatcherUnhandledException += dispatcherHandler;
            using CancellationTokenRegistration cancellation = cancellationToken.Register(
                () => exitAuthority.TryExit(ChildRuntimeExitCode.UserAborted));

            MainWindow window = host.Services.GetRequiredService<MainWindow>();
            bool isWindowClosed = false;
            window.Closed += (_, _) => isWindowClosed = true;
            // Owner fallbacks (About, prompts, message dialogs) read Application.MainWindow: point it at this phase's window.
            application.MainWindow = window;
            window.Show();

            var exitCode = (int)await exitAuthority.Completion;
            logger.Information("Foundry Connect phase ended with code {ExitCode}.", exitCode);
            if (exitCode == (int)ChildRuntimeExitCode.Success)
            {
                if (isWindowClosed)
                {
                    return CreateSuccessResult(host.Services, window: null);
                }

                // Left open and disabled so the next window replaces it without a blank screen.
                window.IsEnabled = false;
                return CreateSuccessResult(host.Services, window);
            }

            if (!isWindowClosed)
            {
                window.Close();
            }

            return CreateEndedResult(exitCode, host.Services);
        }
        catch (Exception exception)
        {
            startup.ReportFailure(exception, category);
            int exitCode = ConnectStartupFailurePolicy.ResolveProcessExitCode(
                lifetime?.RequestedExitCode, (int)GetFailureExitCode(stage, startup));
            return CreateEndedResult(exitCode, host?.Services);
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

            logger.Information("Foundry Connect phase shutdown completed. Closing its log.");
            Log.Logger = previousLogger;
            await CloseLoggerAsync(phaseLogger);
        }
    }

    /// <summary>Takes the kept deployment key and the theme and language Connect ended with.</summary>
    private static ConnectPhaseResult CreateSuccessResult(IServiceProvider services, Window? window)
    {
        DeploymentKeyTransfer? key = services.GetRequiredService<IConnectUnlockService>().TakeDeploymentKey();
        return new ConnectPhaseResult(
            (int)ChildRuntimeExitCode.Success,
            key,
            services.GetRequiredService<IThemeService>().CurrentTheme.ToString(),
            services.GetRequiredService<ILocalizationService>().CurrentCulture.Name,
            window);
    }

    /// <summary>
    /// Builds the result of a phase that did not succeed: no key (an unlocked key stays with the unlock service, which zeroes
    /// it when the container is disposed) and no window.
    /// </summary>
    private static ConnectPhaseResult CreateEndedResult(int exitCode, IServiceProvider? services)
    {
        string theme = services?.GetService<IThemeService>()?.CurrentTheme.ToString() ?? nameof(Foundry.Wpf.Theme.ThemeMode.System);
        string culture = services?.GetService<ILocalizationService>()?.CurrentCulture.Name ?? CultureInfo.CurrentUICulture.Name;
        return new ConnectPhaseResult(exitCode, DeploymentKey: null, theme, culture, MainWindow: null);
    }

    /// <summary>
    /// Maps a failure to its exit code; failures before a policy stage keep the generic startup failure code, and once
    /// the phase's first window rendered every failure is a runtime failure.
    /// </summary>
    private static ChildRuntimeExitCode GetFailureExitCode(ConnectStartupStage? stage, RuntimeStartupDiagnostics startup)
    {
        if (stage is not { } current)
        {
            return ChildRuntimeExitCode.StartupFailure;
        }

        return ConnectStartupFailurePolicy.MapFailure(current, startup.IsUiReady);
    }

    private static Serilog.ILogger CreatePhaseLogger(out string logFilePath)
    {
        try
        {
            logFilePath = FoundryConnectLogging.ResolveStartupLogFilePath();
            return FoundryConnectLogging.CreateLogger(logFilePath);
        }
        catch (Exception exception)
        {
            logFilePath = "<unavailable>";
            Serilog.ILogger fallback = FoundryLogConfiguration.CreateDebugLogger(
                Identity.ApplicationId, DiagnosticSessionContext.CurrentSessionId,
                Serilog.Events.LogEventLevel.Verbose, additionalSink: RemoteDiagnosticsSink.Instance);
            fallback.ForContext(typeof(ConnectPhase)).Error(
                "File logging initialization failed. Falling back to debugger output. FailureType={FailureType}, ErrorCode={ErrorCode}",
                exception.GetType().Name, exception.HResult);
            return fallback;
        }
    }

    private static IHost BuildHost(
        RuntimeStartupDiagnostics startup,
        IConnectConfigurationService configurationService,
        FoundryConnectConfigurationDocument configuration,
        ConnectRuntimeSecrets secrets)
    {
        HostApplicationBuilder builder = Host.CreateApplicationBuilder();
        builder.Logging.ClearProviders();
        builder.Logging.SetMinimumLevel(LogLevel.Trace);
        builder.Logging.AddSerilog(dispose: false);
        builder.Services.AddSingleton(startup);
        builder.Services.AddSingleton(configurationService);
        builder.Services.AddSingleton(configuration);
        builder.Services.AddSingleton(secrets);
        builder.Services.AddFoundryConnectApplicationServices();
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
            logger.ForContext("PostHogTransportInternal", true).Warning(
                "Telemetry flush did not complete within the shutdown budget. FailureType={FailureType}, ErrorCode={ErrorCode}",
                exception.GetType().Name, exception.HResult);
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
            logger.Warning(
                "Foundry Connect service disposal failed. FailureType={FailureType}, ErrorCode={ErrorCode}",
                exception.GetType().Name, exception.HResult);
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
