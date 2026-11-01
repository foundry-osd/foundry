// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.IO;
using System.Windows;
using System.Windows.Threading;
using Foundry.AutopilotInteractive.DependencyInjection;
using Foundry.AutopilotInteractive.Services.Registration;
using Foundry.AutopilotInteractive.Startup;
using Foundry.Core.Models.Autopilot;
using Foundry.Utilities.Diagnostics;
using Foundry.Wpf.Dialogs;
using Foundry.Wpf.Localization;
using Foundry.Wpf.Runtime;
using Foundry.Wpf.Theme;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Events;
using Serilog.Extensions.Logging;

namespace Foundry.AutopilotInteractive;

/// <summary>
/// Entry point of Foundry.AutopilotInteractive, started by Foundry.PostInstall during the Windows setup specialize pass.
/// </summary>
/// <remarks>
/// The application writes a local log file only: it has no telemetry and no remote diagnostics. The process exit code is
/// one of <see cref="AutopilotInteractiveExitCodes"/>, the contract read by Foundry.PostInstall; every code lets Windows
/// setup continue.
/// </remarks>
public static class Program
{
    private const string ApplicationName = "Foundry.AutopilotInteractive";
    private const string LogFileName = "Foundry.AutopilotInteractive.log";
    private const int RetainedLogFileCount = 5;
    private static readonly TimeSpan LogFlushTimeout = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan MaxTimerInterval = TimeSpan.FromMilliseconds(int.MaxValue);

    /// <summary>
    /// Parses the arguments, starts logging, loads the configuration, then runs the window on the original STA thread.
    /// </summary>
    /// <param name="args">Process arguments: <c>--config &lt;path&gt; --deadline &lt;UTC ISO 8601&gt;</c>.</param>
    /// <returns>The process exit code.</returns>
    [STAThread]
    public static int Main(string[] args)
    {
        List<string> argumentWarnings = [];
        AutopilotInteractiveCommandLine commandLine = AutopilotInteractiveCommandLine.Parse(args, argumentWarnings.Add);

        // The configuration is read before logging starts because it carries the session id shared with Deploy and
        // PostInstall. A failure is kept and logged once the logger exists.
        AutopilotInteractiveRuntimeConfiguration? configuration = null;
        InvalidDataException? configurationFailure = null;
        try
        {
            configuration = AutopilotInteractiveConfigurationLoader.Load(commandLine.ConfigPath, allowDebugDefault: DebugSafetyMode.IsEnabled);
        }
        catch (InvalidDataException exception)
        {
            configurationFailure = exception;
        }

        string sessionId = AutopilotInteractiveLogSession.Resolve(
            configuration,
            Environment.GetEnvironmentVariable(DiagnosticSessionContext.EnvironmentVariableName));
        Log.Logger = CreateLogger(sessionId);
        Serilog.ILogger logger = Log.ForContext(typeof(Program));
        IHost? host = null;
        try
        {
            RegisterGlobalExceptionHandlers();
            logger.Information(
                "Foundry.AutopilotInteractive started. Version={Version}, SessionId={SessionId}, HasConfigPath={HasConfigPath}, HasDeadline={HasDeadline}",
                FoundryAutopilotInteractiveApplicationInfo.Version,
                sessionId,
                commandLine.ConfigPath is not null,
                commandLine.DeadlineUtc is not null);
            foreach (string warning in argumentWarnings)
            {
                logger.Warning("Command line: {Warning}", warning);
            }

            // Both startup guards run before any UI and before the configuration is needed: a device that is already
            // registered or a setup window that already closed must not show the application.
            RegistrationResultStore resultStore = new(RegistrationResultStore.ResolveStateDirectory(DebugSafetyMode.IsEnabled));
            if (DebugSafetyMode.IsEnabled)
            {
                // Simulated runs write real result files; the guard would otherwise lock every later debug session out.
                logger.Information("Debug safety mode: the completed-result guard is bypassed.");
            }
            else if (resultStore.IsCompleted())
            {
                logger.Information("Registration already completed by an earlier run. Exiting with code {ExitCode}.", AutopilotInteractiveExitCodes.Registered);
                return AutopilotInteractiveExitCodes.Registered;
            }

            if (DeadlinePolicy.IsReached(commandLine.DeadlineUtc, DateTimeOffset.UtcNow))
            {
                logger.Warning("The setup deadline was already reached at startup. Exiting with code {ExitCode}.", AutopilotInteractiveExitCodes.Incomplete);
                WriteDeadlineResult(resultStore, logger);
                return AutopilotInteractiveExitCodes.Incomplete;
            }

            if (configuration is null)
            {
                logger.Error(configurationFailure, "The configuration is missing or invalid. Exiting with code {ExitCode}.", AutopilotInteractiveExitCodes.Incomplete);
                ShowConfigurationError(commandLine.DeadlineUtc, logger);
                return AutopilotInteractiveExitCodes.Incomplete;
            }

            LogConfiguration(configuration, logger);

            host = BuildHost(args, commandLine, configuration);
            App app = host.Services.GetRequiredService<App>();
            app.DispatcherUnhandledException += (_, eventArgs) =>
            {
                logger.Error(eventArgs.Exception, "Unhandled UI exception. Exiting with code {ExitCode}.", AutopilotInteractiveExitCodes.Incomplete);
                eventArgs.Handled = true;
                app.Shutdown(AutopilotInteractiveExitCodes.Incomplete);
            };
            app.InitializeComponent();
            RuntimePreferencesApplier.Apply(
                configuration,
                host.Services.GetRequiredService<IThemeService>(),
                host.Services.GetRequiredService<ILocalizationService>(),
                host.Services.GetRequiredService<ILogger<App>>());

            // The window ends the application itself with the exit code of the run (MainWindow.OnClosed).
            app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            int exitCode = app.Run(host.Services.GetRequiredService<MainWindow>());
            logger.Information("Foundry.AutopilotInteractive exited with code {ExitCode}.", exitCode);
            return exitCode;
        }
        catch (Exception exception)
        {
            logger.Error(exception, "Foundry.AutopilotInteractive failed to start or run. Exiting with code {ExitCode}.", AutopilotInteractiveExitCodes.Incomplete);
            return AutopilotInteractiveExitCodes.Incomplete;
        }
        finally
        {
            try
            {
                host?.Dispose();
            }
            catch (Exception exception)
            {
                logger.Warning("Application service disposal failed. FailureType={FailureType}, ErrorCode={ErrorCode}", exception.GetType().Name, exception.HResult);
            }

            try
            {
                Task.Run(() => Log.CloseAndFlushAsync().AsTask()).WaitAsync(LogFlushTimeout).GetAwaiter().GetResult();
            }
            catch
            {
                // A log that cannot be flushed must not replace the exit code.
            }
        }
    }

    private static void WriteDeadlineResult(RegistrationResultStore resultStore, Serilog.ILogger logger)
    {
        try
        {
            resultStore.Write(new RegistrationResult(
                RegistrationResult.StatusFailed,
                RegistrationFailureCodes.DeadlineReached,
                ImportId: null,
                AutopilotDeviceId: null,
                ProfileAssignment: null,
                DateTimeOffset.UtcNow));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // The exit code already tells PostInstall to continue; a missing result file only costs a later attempt.
            logger.Warning("The deadline result could not be written. FailureType={FailureType}, ErrorCode={ErrorCode}", exception.GetType().Name, exception.HResult);
        }
    }

    private static void LogConfiguration(AutopilotInteractiveRuntimeConfiguration configuration, Serilog.ILogger logger)
    {
        logger.Information(
            "Configuration loaded. CultureName={CultureName}, Theme={Theme}, HasAssignedComputerName={HasAssignedComputerName}, PreselectCustomGroupTag={PreselectCustomGroupTag}, OfferAssignedUser={OfferAssignedUser}",
            configuration.CultureName,
            configuration.Theme,
            !string.IsNullOrWhiteSpace(configuration.AssignedComputerName),
            configuration.PreselectCustomGroupTag,
            configuration.OfferAssignedUser);
    }

    /// <summary>
    /// Shows a blocking message in the operating system language: the configuration that carries the language is the one
    /// that failed. The window cannot come from the container, which needs the configuration.
    /// </summary>
    /// <remarks>
    /// With a deadline, the message closes itself at <see cref="DeadlinePolicy.StopAt"/> (or is not shown when that instant
    /// has passed), so an unattended setup is never held until PostInstall stops the process.
    /// </remarks>
    private static void ShowConfigurationError(DateTimeOffset? deadlineUtc, Serilog.ILogger logger)
    {
        TimeSpan? timeLeft = DeadlinePolicy.StopAt(deadlineUtc) - DateTimeOffset.UtcNow;
        if (timeLeft <= TimeSpan.Zero)
        {
            logger.Warning("The configuration error is not shown: the setup deadline is too close.");
            return;
        }

        App app = new();
        app.InitializeComponent();
        ResourceLocalizationService localization = ServiceCollectionExtensions.CreateLocalizationService();
        LocalizedMessageDialog dialog = new(
            localization.GetString("App.Name"),
            localization.GetString("Config.LoadFailed"),
            localization.GetString("Common.Close"))
        {
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            Topmost = true
        };

        DispatcherTimer? deadlineTimer = null;
        if (timeLeft is { } delay)
        {
            // DispatcherTimer accepts at most int.MaxValue milliseconds.
            deadlineTimer = new DispatcherTimer(DispatcherPriority.Normal, dialog.Dispatcher)
            {
                Interval = delay < MaxTimerInterval ? delay : MaxTimerInterval
            };
            deadlineTimer.Tick += (_, _) =>
            {
                deadlineTimer.Stop();
                if (dialog.IsVisible)
                {
                    logger.Warning("The setup deadline is near: the configuration error closes itself.");
                    dialog.DialogResult = false;
                }
            };
            deadlineTimer.Start();
        }

        try
        {
            _ = dialog.ShowDialog();
        }
        finally
        {
            deadlineTimer?.Stop();
        }
    }

    private static IHost BuildHost(
        string[] args,
        AutopilotInteractiveCommandLine commandLine,
        AutopilotInteractiveRuntimeConfiguration configuration)
    {
        HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);
        builder.Logging.ClearProviders();
        builder.Logging.SetMinimumLevel(LogLevel.Trace);
        builder.Logging.AddSerilog(dispose: false);
        builder.Services.AddFoundryAutopilotInteractiveApplicationServices(commandLine, configuration);
        return builder.Build();
    }

    /// <summary>
    /// Creates the shared Foundry logger: the file under the retained Foundry root, or the debugger output in a debug session
    /// (and when the file cannot be opened) so a development machine never writes into its Windows folder.
    /// </summary>
    private static Serilog.ILogger CreateLogger(string sessionId)
    {
        if (DebugSafetyMode.IsEnabled)
        {
            return FoundryLogConfiguration.CreateDebugLogger(ApplicationName, sessionId, LogEventLevel.Verbose);
        }

        try
        {
            string logDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                "Temp",
                "Foundry",
                AutopilotInteractiveLayout.LogsRelativePath);
            Directory.CreateDirectory(logDirectory);
            return FoundryLogConfiguration.CreateFileLogger(
                Path.Combine(logDirectory, LogFileName),
                ApplicationName,
                sessionId,
                LogEventLevel.Verbose,
                RetainedLogFileCount);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Serilog.ILogger fallback = FoundryLogConfiguration.CreateDebugLogger(ApplicationName, sessionId, LogEventLevel.Verbose);
            fallback.ForContext(typeof(Program)).Error(
                "File logging initialization failed. Falling back to debugger output. FailureType={FailureType}, ErrorCode={ErrorCode}",
                exception.GetType().Name,
                exception.HResult);
            return fallback;
        }
    }

    private static void RegisterGlobalExceptionHandlers()
    {
        AppDomain.CurrentDomain.UnhandledException += (_, eventArgs) =>
        {
            Log.ForContext(typeof(Program)).Error(eventArgs.ExceptionObject as Exception, "Unhandled exception terminating the process.");

            // The process ends right after this handler and the finally block of Main never runs.
            Log.CloseAndFlush();
        };
        TaskScheduler.UnobservedTaskException += (_, eventArgs) =>
        {
            Log.ForContext(typeof(Program)).Error(eventArgs.Exception, "Unobserved task exception.");
            eventArgs.SetObserved();
        };
    }
}
