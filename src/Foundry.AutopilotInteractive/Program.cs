// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.IO;
using System.Windows;
using Foundry.AutopilotInteractive.DependencyInjection;
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
/// The application writes a local log file only: it has no telemetry and no remote diagnostics. Exit code 0 means the
/// registration step finished; any other code lets Windows setup continue without it.
/// </remarks>
public static class Program
{
    private const string ApplicationName = "Foundry.AutopilotInteractive";
    private const string LogFileName = "Foundry.AutopilotInteractive.log";
    private const int RetainedLogFileCount = 5;
    private const int StartupFailureExitCode = 1;
    private static readonly TimeSpan LogFlushTimeout = TimeSpan.FromSeconds(2);

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

        Log.Logger = CreateLogger();
        Serilog.ILogger logger = Log.ForContext(typeof(Program));
        IHost? host = null;
        try
        {
            RegisterGlobalExceptionHandlers();
            logger.Information(
                "Foundry.AutopilotInteractive started. Version={Version}, SessionId={SessionId}, HasConfigPath={HasConfigPath}, HasDeadline={HasDeadline}",
                FoundryAutopilotInteractiveApplicationInfo.Version,
                DiagnosticSessionContext.CurrentSessionId,
                commandLine.ConfigPath is not null,
                commandLine.DeadlineUtc is not null);
            foreach (string warning in argumentWarnings)
            {
                logger.Warning("Command line: {Warning}", warning);
            }

            // The completed-registration guard runs here, before the configuration is read.
            AutopilotInteractiveRuntimeConfiguration? configuration = LoadConfiguration(commandLine, logger);
            if (configuration is null)
            {
                ShowConfigurationError();
                return StartupFailureExitCode;
            }

            host = BuildHost(args, commandLine, configuration);
            App app = host.Services.GetRequiredService<App>();
            app.DispatcherUnhandledException += (_, eventArgs) =>
            {
                logger.Error(eventArgs.Exception, "Unhandled UI exception. Exiting with code {ExitCode}.", StartupFailureExitCode);
                eventArgs.Handled = true;
                app.Shutdown(StartupFailureExitCode);
            };
            app.InitializeComponent();
            RuntimePreferencesApplier.Apply(
                configuration,
                host.Services.GetRequiredService<IThemeService>(),
                host.Services.GetRequiredService<ILocalizationService>(),
                host.Services.GetRequiredService<ILogger<App>>());

            int exitCode = app.Run(host.Services.GetRequiredService<MainWindow>());
            logger.Information("Foundry.AutopilotInteractive exited with code {ExitCode}.", exitCode);
            return exitCode;
        }
        catch (Exception exception)
        {
            logger.Error(exception, "Foundry.AutopilotInteractive failed to start or run. Exiting with code {ExitCode}.", StartupFailureExitCode);
            return StartupFailureExitCode;
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

    private static AutopilotInteractiveRuntimeConfiguration? LoadConfiguration(
        AutopilotInteractiveCommandLine commandLine,
        Serilog.ILogger logger)
    {
        try
        {
            AutopilotInteractiveRuntimeConfiguration configuration = AutopilotInteractiveConfigurationLoader.Load(
                commandLine.ConfigPath,
                allowDebugDefault: DebugSafetyMode.IsEnabled);
            logger.Information(
                "Configuration loaded. CultureName={CultureName}, Theme={Theme}, HasAssignedComputerName={HasAssignedComputerName}, PreselectCustomGroupTag={PreselectCustomGroupTag}, OfferAssignedUser={OfferAssignedUser}, ConfiguredSessionId={ConfiguredSessionId}",
                configuration.CultureName,
                configuration.Theme,
                !string.IsNullOrWhiteSpace(configuration.AssignedComputerName),
                configuration.PreselectCustomGroupTag,
                configuration.OfferAssignedUser,
                configuration.DiagnosticSessionId);
            return configuration;
        }
        catch (InvalidDataException exception)
        {
            logger.Error(exception, "The configuration is missing or invalid. Exiting with code {ExitCode}.", StartupFailureExitCode);
            return null;
        }
    }

    /// <summary>
    /// Shows a blocking message in the operating system language: the configuration that carries the language is the one
    /// that failed. The window cannot come from the container, which needs the configuration.
    /// </summary>
    private static void ShowConfigurationError()
    {
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
        _ = dialog.ShowDialog();
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
    private static Serilog.ILogger CreateLogger()
    {
        string sessionId = DiagnosticSessionContext.CurrentSessionId;
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
            Log.ForContext(typeof(Program)).Error(eventArgs.ExceptionObject as Exception, "Unhandled exception terminating the process.");
        TaskScheduler.UnobservedTaskException += (_, eventArgs) =>
        {
            Log.ForContext(typeof(Program)).Error(eventArgs.Exception, "Unobserved task exception.");
            eventArgs.SetObserved();
        };
    }
}
