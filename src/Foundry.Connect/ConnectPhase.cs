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
/// While the phase runs, it owns <see cref="Log.Logger"/> (through <see cref="HostedPhaseScope"/>),
/// <see cref="Application.MainWindow"/> and a handler of <see cref="Application.DispatcherUnhandledException"/>, which marks
/// the exception handled and ends the phase.
/// </remarks>
public static class ConnectPhase
{
    private static readonly RuntimeApplicationIdentity Identity = new("Foundry.Connect", TelemetryApps.FoundryConnect, FoundryConnectApplicationInfo.Version);

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

        RuntimeStartupDiagnostics startup = RuntimeStartupDiagnostics.CreateForHostedPhase(
            Identity, options.OnFirstWindowRendered, options.OnConfigurationLoaded);
        HostedPhaseScope scope = HostedPhaseScope.Begin(Identity, "Foundry Connect", typeof(ConnectPhase),
            FoundryConnectLogging.ResolveStartupLogFilePath, FoundryConnectLogging.CreateLogger);
        Serilog.ILogger logger = scope.Logger;
        IHost? host = null;
        ITelemetryService? telemetry = null;
        ApplicationLifetimeService? lifetime = null;
        DispatcherUnhandledExceptionEventHandler? dispatcherHandler = null;
        MainWindow? window = null;
        bool isWindowClosed = false;
        DeploymentKeyTransfer? takenKey = null;
        string category = "managed_startup";
        // Managed startup (logging, runtime checks) precedes the policy stages.
        ConnectStartupStage? stage = null;
        try
        {
            logger.Information(
                "Foundry Connect phase started. Version={Version}, SessionId={SessionId}, LogFilePath={LogFilePath}",
                FoundryConnectApplicationInfo.Version, DiagnosticSessionContext.CurrentSessionId, scope.LogFilePath);
            if (!RuntimeStartupGuard.CanRun())
                throw new InvalidOperationException("Foundry Connect requires Windows PE outside a DEBUG debugger session.");

            category = "configuration";
            var configurationService = new ConnectConfigurationService(
                new SerilogLoggerFactory(Log.Logger).CreateLogger<ConnectConfigurationService>());
            host = LoadServices(startup, configurationService, current => stage = current);
            if (host is null)
            {
                return CreateEndedResult((int)ConnectStartupFailurePolicy.Map(ConnectStartupStage.Configuration), services: null);
            }

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

            window = host.Services.GetRequiredService<MainWindow>();
            window.Closed += (_, _) => isWindowClosed = true;
            // Owner fallbacks (About, prompts, message dialogs) read Application.MainWindow: point it at this phase's window.
            application.MainWindow = window;
            window.Show();

            var exitCode = (int)await exitAuthority.Completion;
            logger.Information("Foundry Connect phase ended with code {ExitCode}.", exitCode);
            if (exitCode != (int)ChildRuntimeExitCode.Success)
            {
                CloseWindow(window, isWindowClosed);
                return CreateEndedResult(exitCode, host.Services);
            }

            string theme = host.Services.GetRequiredService<IThemeService>().CurrentTheme.ToString();
            string culture = host.Services.GetRequiredService<ILocalizationService>().CurrentCulture.Name;
            if (!isWindowClosed)
            {
                // Left open and disabled so the next window replaces it without a blank screen.
                window.IsEnabled = false;
            }

            // Taken last: nothing below can fail, so the caller always receives the key it must dispose.
            takenKey = host.Services.GetRequiredService<IConnectUnlockService>().TakeDeploymentKey();
            return new ConnectPhaseResult((int)ChildRuntimeExitCode.Success, takenKey, theme, culture, isWindowClosed ? null : window);
        }
        catch (Exception exception)
        {
            startup.ReportFailure(exception, category);
            takenKey?.Dispose();
            CloseWindow(window, isWindowClosed);
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

            await scope.EndAsync(telemetry, host);
        }
    }

    /// <summary>
    /// Loads the configuration and builds the phase's services, then reports <c>configuration_loaded</c> once the container
    /// is built, where Connect's former entry point reported it.
    /// </summary>
    /// <param name="startup">The phase diagnostics; a configuration failure is reported to it.</param>
    /// <param name="configurationService">Loads the Foundry.WinPE runtime configuration.</param>
    /// <param name="enterStage">Receives the startup stage before configuration loading and before the container build.</param>
    /// <returns>The phase's container, or <see langword="null"/> when the configuration cannot be loaded.</returns>
    internal static IHost? LoadServices(
        RuntimeStartupDiagnostics startup,
        IConnectConfigurationService configurationService,
        Action<ConnectStartupStage> enterStage)
    {
        enterStage(ConnectStartupStage.Configuration);
        ConnectConfigurationLoadResult loadResult = configurationService.Load();
        if (!loadResult.IsSuccess)
        {
            startup.ReportFailure(loadResult.CreateDiagnosticFailure(), "configuration");
            return null;
        }

        // Building the host is not a configuration failure for the exit code.
        enterStage(ConnectStartupStage.Services);
        IHost host = BuildHost(startup, configurationService, loadResult.Configuration, loadResult.Secrets);
        startup.ReportConfigurationLoaded();
        return host;
    }

    /// <summary>
    /// Builds the result of a phase that did not succeed: no window, and no key (an unlocked key is taken and zeroed here).
    /// Reading the end state never fails the result.
    /// </summary>
    private static ConnectPhaseResult CreateEndedResult(int exitCode, IServiceProvider? services)
    {
        string theme = nameof(Foundry.Wpf.Theme.ThemeMode.System);
        string culture = CultureInfo.CurrentUICulture.Name;
        if (services is not null)
        {
            try
            {
                services.GetService<IConnectUnlockService>()?.TakeDeploymentKey()?.Dispose();
                theme = services.GetService<IThemeService>()?.CurrentTheme.ToString() ?? theme;
                culture = services.GetService<ILocalizationService>()?.CurrentCulture.Name ?? culture;
            }
            catch (Exception exception) when (exception is InvalidOperationException or ObjectDisposedException)
            {
                // A service that cannot be built leaves the defaults; the container disposal still zeroes a kept key.
            }
        }

        return new ConnectPhaseResult(exitCode, DeploymentKey: null, theme, culture, MainWindow: null);
    }

    /// <summary>Closes the phase's window unless it is already closed; a window that refuses to close stays for the host.</summary>
    private static void CloseWindow(Window? window, bool isWindowClosed)
    {
        if (window is null || isWindowClosed)
        {
            return;
        }

        try
        {
            window.Close();
        }
        catch (InvalidOperationException)
        {
            // Already closing: the host's shutdown closes it.
        }
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

    private static IHost BuildHost(
        RuntimeStartupDiagnostics startup,
        IConnectConfigurationService configurationService,
        FoundryConnectConfigurationDocument configuration,
        ConnectRuntimeSecrets secrets)
    {
        // Kept here: Foundry.Core does not reference Microsoft.Extensions.Hosting or Serilog.Extensions.Logging.
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
}
