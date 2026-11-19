// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.ComponentModel;
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
using Foundry.Wpf.Localization;
using Foundry.Wpf.Shell;
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
/// <c>/Foundry.Deploy;component/Themes/DeployStyles.xaml</c> merged after Fluent and the Foundry.Wpf styles.
/// While the phase runs, it owns <see cref="Log.Logger"/> (through <see cref="HostedPhaseScope"/>),
/// <see cref="Application.MainWindow"/> (set to the shell window), the shell's close requests and a handler of
/// <see cref="Application.DispatcherUnhandledException"/>, which marks the exception handled and ends the phase with
/// <see cref="UnhandledExceptionExitCode"/>. A startup or configuration failure ends it with the <see cref="ChildRuntimeExitCode"/>
/// failure codes that Foundry Connect uses (see <see cref="FailureExitCodeFor"/>), so the guardian reports it as a failure of
/// the phase rather than a crash.
/// </remarks>
public static class DeployPhase
{
    /// <summary>Exit code of a phase that ended normally: the shell window was closed, or it ended before the wizard by design.</summary>
    public const int SuccessExitCode = 0;

    /// <summary>Exit code of a phase ended by an unhandled UI exception: the guardian treats it as a crash.</summary>
    public const int UnhandledExceptionExitCode = 1;

    private static readonly RuntimeApplicationIdentity Identity = new("Foundry.Deploy", TelemetryApps.FoundryDeploy, FoundryDeployApplicationInfo.Version);

    /// <summary>
    /// Shows Deploy's content in the shell with the Connect theme and language and completes when the shell window
    /// closes or the phase fails. Must be called on the application's UI thread. <see cref="DeployPhaseOptions.InitialDeploymentKey"/> is disposed
    /// on every path.
    /// </summary>
    /// <param name="options">The Connect hand-over and the host hooks.</param>
    /// <param name="cancellationToken">
    /// Asks a running phase to end, as a close of the shell window would; a running deployment is asked to cancel instead.
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
        // The first render of Deploy's own content in the shell (also after Connect's content) or of a startup dialog, whichever
        // comes first, tells the host Deploy is visible.
        Action onFirstContentRendered = RunOnce(options.OnFirstContentRendered);

        RuntimeStartupDiagnostics startup = RuntimeStartupDiagnostics.CreateForHostedPhase(Identity, onFirstContentRendered);
        HostedPhaseScope scope = HostedPhaseScope.Begin(Identity, "Foundry Deploy", typeof(DeployPhase),
            FoundryDeployLogging.ResolveStartupLogFilePath, FoundryDeployLogging.CreateLogger);
        Serilog.ILogger logger = scope.Logger;
        IHost? host = null;
        ITelemetryService? telemetry = null;
        DispatcherUnhandledExceptionEventHandler? dispatcherHandler = null;
        IDisposable? closeAttachment = null;
        CultureChangeReporter? cultureReporter = null;
        DeployShellContent? content = null;
        string stage = "managed_startup";
        try
        {
            logger.Information(
                "Foundry Deploy phase started. Version={Version}, SessionId={SessionId}, LogFilePath={LogFilePath}",
                FoundryDeployApplicationInfo.Version, DiagnosticSessionContext.CurrentSessionId, scope.LogFilePath);
            if (!RuntimeStartupGuard.CanRun())
                throw new InvalidOperationException("Foundry Deploy requires Windows PE outside a DEBUG debugger session.");

            // Building the host and resolving services is the services stage; only the configuration read below is "configuration".
            stage = "services";
            host = BuildHost(startup, options);
            IDeployConfigurationService configurationService = host.Services.GetRequiredService<IDeployConfigurationService>();
            stage = "configuration";
            DeployConfigurationLoadResult configuration = configurationService.LoadOptional();
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

            // Before the content is resolved, so its first frame and its menus already use Connect's theme and language.
            MainWindowViewModel viewModel = host.Services.GetRequiredService<MainWindowViewModel>();
            viewModel.ApplyInitialPreferences(options.ThemeMode, options.CultureName);
            if (options.OnCultureChanged is not null)
            {
                cultureReporter = CultureChangeReporter.Start(
                    host.Services.GetRequiredService<ILocalizationService>(), options.OnCultureChanged);
            }

            content = host.Services.GetRequiredService<DeployShellContent>();
            bool isEnded = false;
            void CancelDeployment()
            {
                if (viewModel.CancelDeploymentCommand.CanExecute(null))
                {
                    viewModel.CancelDeploymentCommand.Execute(null);
                }
            }

            closeAttachment = AttachCloseHandler(
                options.Shell,
                () => viewModel.IsDeploymentRunning,
                CancelDeployment,
                () => lifetime.TryEnd(SuccessExitCode));
            using CancellationTokenRegistration cancellation = cancellationToken.Register(() =>
                application.Dispatcher.BeginInvoke(() =>
                {
                    // Like a close of the shell: a running deployment is asked to cancel and keeps the phase alive.
                    if (isEnded) return;
                    if (viewModel.IsDeploymentRunning)
                    {
                        CancelDeployment();
                    }
                    else
                    {
                        lifetime.TryEnd(SuccessExitCode);
                    }
                }));
            PresentContent(application, options.Shell, content);

            int exitCode = await lifetime.Completion;
            logger.Information("Foundry Deploy phase ended with code {ExitCode}.", exitCode);
            isEnded = true;
            return exitCode;
        }
        catch (Exception exception)
        {
            startup.ReportFailure(exception, stage);
            return FailureExitCodeFor(stage);
        }
        finally
        {
            // The close decision belongs to the host from here on; without a handler the shell refuses a close.
            closeAttachment?.Dispose();
            cultureReporter?.Dispose();
            if (content is not null)
            {
                // The page stays shown until the process ends: a click must not reach the disposed view model.
                content.IsEnabled = false;
                content.Detach();
            }
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
    /// Routes the shell's close requests to Deploy's rules: while a deployment runs the close is refused and the deployment's
    /// cancellation is requested; otherwise the close is accepted and the phase ends normally. Disposing detaches the handler.
    /// </summary>
    /// <param name="shell">The shell whose close requests Deploy handles.</param>
    /// <param name="isDeploymentRunning">Tells whether a deployment is running.</param>
    /// <param name="cancelDeployment">Requests the running deployment's cancellation.</param>
    /// <param name="endPhase">Ends the phase with <see cref="SuccessExitCode"/>.</param>
    /// <returns>The attachment; dispose it when the phase ends.</returns>
    internal static IDisposable AttachCloseHandler(IShellHost shell, Func<bool> isDeploymentRunning, Action cancelDeployment, Action endPhase)
    {
        void OnCloseRequested(object? sender, CancelEventArgs e)
        {
            if (isDeploymentRunning())
            {
                e.Cancel = true;
                cancelDeployment();
                return;
            }

            endPhase();
        }

        shell.CloseRequested += OnCloseRequested;
        return new CloseAttachment(() => shell.CloseRequested -= OnCloseRequested);
    }

    /// <summary>Wraps <paramref name="action"/> so that only its first call runs it, whatever the thread.</summary>
    internal static Action RunOnce(Action action)
    {
        int called = 0;
        return () =>
        {
            if (Interlocked.Exchange(ref called, 1) == 0)
            {
                action();
            }
        };
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
        builder.Services.AddSingleton(options.Shell);
        builder.Services.AddFoundryDeployApplicationServices(options);
        return builder.Build();
    }
}
