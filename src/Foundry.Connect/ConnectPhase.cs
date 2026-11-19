// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Threading;
using Foundry.Connect.Configuration;
using Foundry.Connect.DependencyInjection;
using Foundry.Connect.Lifetime;
using Foundry.Connect.Network;
using Foundry.Connect.Network.Policy;
using Foundry.Connect.Security;
using Foundry.Connect.Services.Logging;
using Foundry.Connect.Startup;
using Foundry.Connect.ViewModels;
using Foundry.Core.Models.Runtime;
using Foundry.Core.Services.Runtime;
using Foundry.Telemetry;
using Foundry.Utilities.Diagnostics;
using Foundry.Wpf.Localization;
using Foundry.Wpf.Shell;
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
/// <c>/Foundry.Connect;component/Themes/ConnectStyles.xaml</c> merged after Fluent and the Foundry.Wpf styles.
/// While the phase runs, it owns <see cref="Log.Logger"/> (through <see cref="HostedPhaseScope"/>),
/// <see cref="Application.MainWindow"/> (set to the shell window), the shell's close requests and a handler of <see cref="Application.DispatcherUnhandledException"/>, which marks
/// the exception handled and ends the phase.
/// <para>
/// The deployment can reopen Connect (<see cref="ConnectPhaseOptions.Revisit"/>) while the Foundry Deploy phase stays alive
/// underneath: a new container and a new <see cref="HostedPhaseScope"/>, nested in Deploy's, which the caller ends in reverse
/// order. That run owns only <see cref="Log.Logger"/>, <see cref="Application.MainWindow"/> and the shown content; Deploy keeps
/// the close requests, the dispatcher's unhandled exceptions and the startup hooks.
/// </para>
/// </remarks>
public static class ConnectPhase
{
    /// <summary>Shortest time the status page stays shown, so a network gate that ends at once does not flash it.</summary>
    internal static readonly TimeSpan StatusPageMinimumDisplay = TimeSpan.FromSeconds(1);

    private static readonly RuntimeApplicationIdentity Identity = new("Foundry.Connect", TelemetryApps.FoundryConnect, FoundryConnectApplicationInfo.Version);

    /// <summary>
    /// Loads the configuration, shows the "Looking for the network" status page while the <see cref="NetworkGate"/> provisions
    /// and probes the internet, then either completes at once when the internet is online (the Connect page is skipped) or
    /// shows Connect's content, and completes when Connect's lifetime ends: the gate's skip, Continue or auto-continue
    /// (success), a close request of the shell window (user abort), or a failure. Must be called on the application's UI thread.
    /// </summary>
    /// <param name="options">The host hooks; with <see cref="ConnectPhaseOptions.Revisit"/> set, Connect is reopened from the deployment.</param>
    /// <param name="cancellationToken">Ends a running phase as a user abort, like closing its window.</param>
    /// <returns>
    /// The phase result; its deployment key (never set for a reopened Connect) is handed to the caller; after a success its
    /// content (disabled), or the status page when the gate skipped it, stays shown in the shell.
    /// </returns>
    /// <exception cref="InvalidOperationException">No WPF application runs on the calling thread.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled before the phase started.</exception>
    public static async Task<ConnectPhaseResult> RunAsync(ConnectPhaseOptions options, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (options.Revisit is { } revisit)
        {
            try
            {
                Application revisitApplication = GetCallingApplication();
                cancellationToken.ThrowIfCancellationRequested();
                return await RunRevisitAsync(revisitApplication, options, revisit, cancellationToken);
            }
            finally
            {
                // The deployment's copy of its key is zeroed on every path, early exits included; Dispose is idempotent.
                revisit.DeploymentKey?.Dispose();
            }
        }

        Application application = GetCallingApplication();
        cancellationToken.ThrowIfCancellationRequested();

        RuntimeStartupDiagnostics startup = RuntimeStartupDiagnostics.CreateForHostedPhase(
            Identity, options.OnFirstContentRendered, options.OnConfigurationLoaded);
        HostedPhaseScope scope = HostedPhaseScope.Begin(Identity, "Foundry Connect", typeof(ConnectPhase),
            FoundryConnectLogging.ResolveStartupLogFilePath, FoundryConnectLogging.CreateLogger);
        Serilog.ILogger logger = scope.Logger;
        IHost? host = null;
        ITelemetryService? telemetry = null;
        ApplicationLifetimeService? lifetime = null;
        DispatcherUnhandledExceptionEventHandler? dispatcherHandler = null;
        IDisposable? closeAttachment = null;
        CultureChangeReporter? cultureReporter = null;
        ConnectShellContent? content = null;
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
            host = LoadServices(startup, configurationService, options.Shell, current => stage = current);
            if (host is null)
            {
                return CreateEndedResult((int)ConnectStartupFailurePolicy.Map(ConnectStartupStage.Configuration), services: null);
            }

            category = "services";
            telemetry = host.Services.GetRequiredService<ITelemetryService>();
            if (options.OnCultureChanged is not null)
            {
                cultureReporter = CultureChangeReporter.Start(
                    host.Services.GetRequiredService<ILocalizationService>(), options.OnCultureChanged);
            }

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

            // Created before the gate: the page view models render the network state and open the automatic Wi-Fi unlock
            // prompt while the gate runs, and the close handler applies to the status page as well.
            content = host.Services.GetRequiredService<ConnectShellContent>();
            closeAttachment = AttachCloseHandler(options.Shell, host.Services.GetRequiredService<MainWindowViewModel>().HandleWindowClosing);
            Task gate = RunNetworkGateAsync(
                application, options.Shell, host.Services, content, startup, exitAuthority, options.StatusPageTitle, cancellationToken);

            var exitCode = (int)await exitAuthority.Completion;

            // Every exit ends the gate; a skip also waits for the status page's minimum display time. It never throws.
            await gate;
            logger.Information("Foundry Connect phase ended with code {ExitCode}.", exitCode);
            if (exitCode != (int)ChildRuntimeExitCode.Success)
            {
                return CreateEndedResult(exitCode, host.Services);
            }

            // Left shown and disabled so the next page replaces it without a blank screen.
            content.IsEnabled = false;
            return CreateSuccessResult(host.Services, isRevisit: false);
        }
        catch (Exception exception)
        {
            startup.ReportFailure(exception, category);
            int exitCode = ConnectStartupFailurePolicy.ResolveFailureExitCode(
                lifetime?.RequestedExitCode, GetFailureExitCode(stage, startup));
            return CreateEndedResult(exitCode, host?.Services);
        }
        finally
        {
            // The close decision belongs to the next phase from here on; without a handler the shell refuses a close.
            closeAttachment?.Dispose();
            cultureReporter?.Dispose();
            // The shell may keep showing the content, but no longer holds it (or its view model) through its events.
            content?.Detach();
            if (dispatcherHandler is not null)
            {
                application.DispatcherUnhandledException -= dispatcherHandler;
            }

            await scope.EndAsync(telemetry, host);
        }
    }

    /// <summary>
    /// Runs Connect reopened from the deployment, inside Deploy's phase: no status page, network gate, startup hooks, close
    /// handler or dispatcher handler. It applies the deployment's theme and language, takes the deployment's key before any
    /// prompt can open, replays the first page's provisioning, shows the page with "Back to deployment" and completes on that
    /// button (success), the caller's cancellation (user abort) or a failure.
    /// </summary>
    private static async Task<ConnectPhaseResult> RunRevisitAsync(
        Application application,
        ConnectPhaseOptions options,
        ConnectRevisit revisit,
        CancellationToken cancellationToken)
    {
        // No host hook: the UI process is already ready; the diagnostics only log failures and pick their exit code.
        RuntimeStartupDiagnostics startup = RuntimeStartupDiagnostics.CreateForHostedPhase(Identity, static () => { });
        HostedPhaseScope scope = HostedPhaseScope.Begin(Identity, "Foundry Connect", typeof(ConnectPhase),
            FoundryConnectLogging.ResolveStartupLogFilePath, FoundryConnectLogging.CreateLogger);
        Serilog.ILogger logger = scope.Logger;
        IHost? host = null;
        ApplicationLifetimeService? lifetime = null;
        CultureChangeReporter? cultureReporter = null;
        ConnectShellContent? content = null;
        string category = "managed_startup";
        ConnectStartupStage? stage = null;
        try
        {
            logger.Information(
                "Foundry Connect reopened from the deployment. Version={Version}, SessionId={SessionId}, LogFilePath={LogFilePath}, HasPreviousProvisioning={HasPreviousProvisioning}, HasDeploymentKey={HasDeploymentKey}",
                FoundryConnectApplicationInfo.Version, DiagnosticSessionContext.CurrentSessionId, scope.LogFilePath,
                revisit.PreviousProvisioning is not null, revisit.DeploymentKey is not null);

            category = "configuration";
            var configurationService = new ConnectConfigurationService(
                new SerilogLoggerFactory(Log.Logger).CreateLogger<ConnectConfigurationService>());
            host = LoadServices(startup, configurationService, options.Shell, current => stage = current, isRevisit: true);
            if (host is null)
            {
                return CreateEndedResult((int)ConnectStartupFailurePolicy.Map(ConnectStartupStage.Configuration), services: null);
            }

            category = "services";
            IServiceProvider services = host.Services;
            services.GetRequiredService<MainWindowViewModel>().ApplyInitialPreferences(options.ThemeMode, options.CultureName);
            if (options.OnCultureChanged is not null)
            {
                cultureReporter = CultureChangeReporter.Start(services.GetRequiredService<ILocalizationService>(), options.OnCultureChanged);
            }

            category = "ui_startup";
            stage = ConnectStartupStage.UiStartup;
            ApplicationLifetimeService exitAuthority = services.GetRequiredService<ApplicationLifetimeService>();
            lifetime = exitAuthority;
            using CancellationTokenRegistration cancellation = cancellationToken.Register(
                () => exitAuthority.TryExit(ChildRuntimeExitCode.UserAborted));

            content = services.GetRequiredService<ConnectShellContent>();
            if (revisit.DeploymentKey is { } deploymentKey)
            {
                services.GetRequiredService<IConnectUnlockService>().TryAcceptDeploymentKey(deploymentKey);
            }

            await ShowRevisitedPageAsync(
                application, options.Shell, services, content, startup, exitAuthority, revisit.PreviousProvisioning, cancellationToken);

            var exitCode = (int)await exitAuthority.Completion;
            logger.Information("Foundry Connect reopened from the deployment ended with code {ExitCode}.", exitCode);
            if (exitCode != (int)ChildRuntimeExitCode.Success)
            {
                return CreateEndedResult(exitCode, services);
            }

            // Left shown and disabled until the deployment shows its own content again.
            content.IsEnabled = false;
            return CreateSuccessResult(services, isRevisit: true);
        }
        catch (Exception exception)
        {
            startup.ReportFailure(exception, category);
            int exitCode = ConnectStartupFailurePolicy.ResolveFailureExitCode(
                lifetime?.RequestedExitCode, GetFailureExitCode(stage, startup));
            return CreateEndedResult(exitCode, host?.Services);
        }
        finally
        {
            cultureReporter?.Dispose();
            content?.Detach();

            // Nothing to flush: the reopened page has no telemetry.
            await scope.EndAsync(telemetry: null, host);
        }
    }

    /// <summary>
    /// Shows the reopened page at once, then starts its network services (<see cref="NetworkGate.StartForRevisitAsync"/>)
    /// behind it; "Back to deployment" is available meanwhile and ends the start.
    /// </summary>
    private static async Task ShowRevisitedPageAsync(
        Application application,
        IShellHost shell,
        IServiceProvider services,
        ConnectShellContent content,
        RuntimeStartupDiagnostics startup,
        ApplicationLifetimeService exitAuthority,
        ProvisioningOutcome? previousProvisioning,
        CancellationToken cancellationToken)
    {
        PresentContent(application, shell, content);

        // Failures from here on are runtime failures.
        startup.ReportUiReady();
        services.GetRequiredService<AutoContinueViewModel>().OnPageShown();
        try
        {
            await services.GetRequiredService<NetworkGate>().StartForRevisitAsync(previousProvisioning, cancellationToken);
        }
        catch (OperationCanceledException) when (exitAuthority.RequestedExitCode is not null || cancellationToken.IsCancellationRequested)
        {
            // "Back to deployment" or the caller's cancellation (its registration requested the user abort) ended the start.
        }
    }

    /// <summary>
    /// Shows the status page, reports the phase ready once it rendered, runs the <see cref="NetworkGate"/> behind it for at
    /// least <see cref="StatusPageMinimumDisplay"/>, then shows <paramref name="content"/> and releases its auto-continue
    /// countdown unless the gate skipped Connect or another exit was requested. The wired unlock prompt and the automatic Wi-Fi prompt open over the status page. A gate
    /// failure ends the phase like a failure of the page; never throws.
    /// </summary>
    private static async Task RunNetworkGateAsync(
        Application application,
        IShellHost shell,
        IServiceProvider services,
        ConnectShellContent content,
        RuntimeStartupDiagnostics startup,
        ApplicationLifetimeService exitAuthority,
        string? statusPageTitle,
        CancellationToken cancellationToken)
    {
        ShellContentAttachment? attachment = null;
        try
        {
            ILocalizationService localization = services.GetRequiredService<ILocalizationService>();
            var statusPage = new StatusPageView
            {
                DataContext = CreateStatusPageModel(statusPageTitle, localization)
            };
            attachment = new ShellContentAttachment(shell, statusPage);
            PresentContent(application, shell, statusPage);
            await attachment.Rendered;
            await application.Dispatcher.InvokeAsync(static () => { }, DispatcherPriority.ContextIdle);
            if (exitAuthority.RequestedExitCode is not null)
            {
                return;
            }

            startup.ReportUiReady();
            Task<NetworkGateOutcome> run = services.GetRequiredService<NetworkGate>().RunAsync(cancellationToken);
            await StatusPageModel.EnsureMinimumDisplayAsync(run, StatusPageMinimumDisplay, services.GetRequiredService<TimeProvider>());
            if (await run == NetworkGateOutcome.ShowConnect && exitAuthority.RequestedExitCode is null)
            {
                PresentContent(application, shell, content);

                // Only now: the countdown starts from its full delay on the visible page, never behind the status page.
                services.GetRequiredService<AutoContinueViewModel>().OnPageShown();
            }
        }
        catch (OperationCanceledException) when (exitAuthority.RequestedExitCode is not null || attachment?.IsEnded == true)
        {
            // Another exit ended the gate, or the shell window closed.
        }
        catch (Exception exception)
        {
            // Ends the phase: RunAsync awaits the exit authority before this task.
            startup.ReportFailure(exception, "network_gate");
            exitAuthority.TryExit(GetFailureExitCode(ConnectStartupStage.UiStartup, startup));
        }
        finally
        {
            attachment?.Dispose();
        }
    }

    /// <summary>
    /// Loads the configuration and builds the phase's services, then reports <c>configuration_loaded</c> once the container
    /// is built, where Connect's former entry point reported it.
    /// </summary>
    /// <param name="startup">The phase diagnostics; a configuration failure is reported to it.</param>
    /// <param name="configurationService">Loads the Foundry.WinPE runtime configuration.</param>
    /// <param name="shell">The shell that shows Connect's content, registered for the content to resolve.</param>
    /// <param name="enterStage">Receives the startup stage before configuration loading and before the container build.</param>
    /// <param name="isRevisit">Whether the container serves a Connect page reopened from the deployment.</param>
    /// <returns>The phase's container, or <see langword="null"/> when the configuration cannot be loaded.</returns>
    internal static IHost? LoadServices(
        RuntimeStartupDiagnostics startup,
        IConnectConfigurationService configurationService,
        IShellHost shell,
        Action<ConnectStartupStage> enterStage,
        bool isRevisit = false)
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
        IHost host = BuildHost(startup, configurationService, shell, loadResult.Configuration, loadResult.Secrets, isRevisit);
        try
        {
            startup.ReportConfigurationLoaded();
        }
        catch
        {
            // The host hook failed: the caller never receives the container, so it is disposed here.
            host.Dispose();
            throw;
        }

        return host;
    }

    /// <summary>
    /// Builds the result of a successful phase: its theme, culture and provisioning outcome, and the deployment key for the
    /// first Connect page only; a page reopened from the deployment zeroes a key it obtained, since the deployment keeps its own.
    /// </summary>
    /// <param name="services">The phase's container.</param>
    /// <param name="isRevisit">Whether Connect was reopened from the deployment.</param>
    /// <returns>The success result; the caller owns and disposes its key.</returns>
    internal static ConnectPhaseResult CreateSuccessResult(IServiceProvider services, bool isRevisit)
    {
        string theme = services.GetRequiredService<IThemeService>().CurrentTheme.ToString();
        string culture = services.GetRequiredService<ILocalizationService>().CurrentCulture.Name;
        ProvisioningOutcome? provisioning = services.GetRequiredService<IProvisioningStep>().LastOutcome;

        // Taken last: nothing below can fail, so the caller always receives the key it must dispose.
        DeploymentKeyTransfer? key = services.GetRequiredService<IConnectUnlockService>().TakeDeploymentKey();
        if (isRevisit)
        {
            key?.Dispose();
            key = null;
        }

        return new ConnectPhaseResult((int)ChildRuntimeExitCode.Success, key, theme, culture) { Provisioning = provisioning };
    }

    /// <summary>
    /// Builds the result of a phase that did not succeed: no content change, and no key (an unlocked key is taken and zeroed here).
    /// The provisioned profile document is released and the secrets are cleared before the provisioning outcome is read, as
    /// the successful exits do, so the result never carries profile key material. Reading the end state never fails the result.
    /// </summary>
    /// <param name="exitCode">The phase's exit code.</param>
    /// <param name="services">The phase's container, or <see langword="null"/> when it was never built.</param>
    /// <returns>The ended result.</returns>
    internal static ConnectPhaseResult CreateEndedResult(int exitCode, IServiceProvider? services)
    {
        string theme = nameof(Foundry.Wpf.Theme.ThemeMode.System);
        string culture = CultureInfo.CurrentUICulture.Name;
        ProvisioningOutcome? provisioning = null;
        if (services is not null)
        {
            try
            {
                // The secrets go first, so a failing read of the preferences cannot leave them behind.
                services.GetService<IConnectUnlockService>()?.TakeDeploymentKey()?.Dispose();
                services.GetService<IConnectionCoordinator>()?.ReleaseProvisionedProfile();
                services.GetService<ConnectRuntimeSecrets>()?.ClearAll();
                theme = services.GetService<IThemeService>()?.CurrentTheme.ToString() ?? theme;
                culture = services.GetService<ILocalizationService>()?.CurrentCulture.Name ?? culture;
                provisioning = services.GetService<IProvisioningStep>()?.LastOutcome;
                provisioning?.WifiProfile?.Clear();
            }
            catch (Exception exception) when (exception is InvalidOperationException or ObjectDisposedException)
            {
                // A service that cannot be built leaves the defaults; the container disposal still zeroes a kept key.
            }
        }

        return new ConnectPhaseResult(exitCode, DeploymentKey: null, theme, culture) { Provisioning = provisioning };
    }

    /// <summary>Creates the "Looking for the network" status page model, titled with the host's title when it gave one.</summary>
    internal static StatusPageModel CreateStatusPageModel(string? hostTitle, ILocalizationService localization) =>
        new(string.IsNullOrWhiteSpace(hostTitle) ? localization.GetString("App.Name") : hostTitle, localization.GetString("Status.LookingForNetwork"));

    /// <summary>Returns the calling thread's WPF application.</summary>
    /// <exception cref="InvalidOperationException">No WPF application runs on the calling thread.</exception>
    private static Application GetCallingApplication()
    {
        Application application = Application.Current
            ?? throw new InvalidOperationException("The Foundry Connect phase runs inside a WPF application.");
        application.Dispatcher.VerifyAccess();
        return application;
    }

    /// <summary>
    /// Routes the shell's close requests to <paramref name="handleClosing"/> (the operator abort) and keeps the window open:
    /// the host closes it when the process ends. Disposing detaches the handler, leaving the decision to the next phase.
    /// </summary>
    /// <param name="shell">The shell whose close requests Connect handles.</param>
    /// <param name="handleClosing">Ends the phase as a user abort unless another exit already won.</param>
    /// <returns>The attachment; dispose it when the phase ends.</returns>
    internal static IDisposable AttachCloseHandler(IShellHost shell, Action handleClosing)
    {
        void OnCloseRequested(object? sender, CancelEventArgs e)
        {
            handleClosing();
            e.Cancel = true;
        }

        shell.CloseRequested += OnCloseRequested;
        return new CloseAttachment(() => shell.CloseRequested -= OnCloseRequested);
    }

    /// <summary>
    /// Shows <paramref name="content"/> in the shell and makes the shell window the application's main window, which owner
    /// fallbacks (About, prompts, message dialogs) read.
    /// </summary>
    internal static void PresentContent(Application application, IShellHost shell, FrameworkElement content)
    {
        application.MainWindow = shell.Window;
        shell.ShowContent(content);
    }

    private sealed class CloseAttachment(Action detach) : IDisposable
    {
        public void Dispose() => detach();
    }

    /// <summary>
    /// Maps a failure to its exit code; failures before a policy stage keep the generic startup failure code, and once
    /// the phase's first content rendered every failure is a runtime failure.
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
        IShellHost shell,
        FoundryConnectConfigurationDocument configuration,
        ConnectRuntimeSecrets secrets,
        bool isRevisit)
    {
        // Kept here: Foundry.Core does not reference Microsoft.Extensions.Hosting or Serilog.Extensions.Logging.
        HostApplicationBuilder builder = Host.CreateApplicationBuilder();
        builder.Logging.ClearProviders();
        builder.Logging.SetMinimumLevel(LogLevel.Trace);
        builder.Logging.AddSerilog(dispose: false);
        builder.Services.AddSingleton(startup);
        builder.Services.AddSingleton(shell);
        builder.Services.AddSingleton(configurationService);
        builder.Services.AddSingleton(configuration);
        builder.Services.AddSingleton(secrets);
        builder.Services.AddFoundryConnectApplicationServices(isRevisit);
        return builder.Build();
    }
}
