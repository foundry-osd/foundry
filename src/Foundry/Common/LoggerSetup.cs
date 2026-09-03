// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Serilog;
using Serilog.Core;
using Serilog.Events;
using Foundry.Telemetry;
using Foundry.Utilities.Diagnostics;
using Foundry.Utilities.IO;

namespace Foundry.Common
{
    /// <summary>
    /// Configures process-wide Serilog logging and global exception capture for the WinUI app.
    /// </summary>
    public static partial class LoggerSetup
    {
        private static readonly LoggingLevelSwitch MinimumLevelSwitch = new(LogEventLevel.Information);
        private static bool globalExceptionHandlersRegistered;
        private const int RetainedLogFileCount = 10;

        /// <summary>
        /// Gets the active Serilog logger instance.
        /// </summary>
        public static ILogger Logger { get; private set; } = Serilog.Core.Logger.None;
        public static string LogFilePath { get; private set; } = Constants.LogFilePath;
        private static ILogger SetupLogger => Log.ForContext("SourceContext", typeof(LoggerSetup).FullName);

        /// <summary>
        /// Initializes the Foundry log sinks and assigns <see cref="Log.Logger"/>.
        /// </summary>
        public static void ConfigureLogger()
        {
            Exception? initializationException = null;
            LogFilePath = WritableFilePathResolver.Resolve(
                (string[])
                [
                    Constants.LogDirectoryPath,
                    Path.Combine(Constants.UserRootDirectoryPath, "Logs"),
                    Path.Combine(Path.GetTempPath(), Constants.ApplicationName, "Logs"),
                    AppContext.BaseDirectory
                ],
                Path.GetFileName(Constants.LogFilePath));

            try
            {
                Logger = FoundryLogConfiguration.CreateFileLogger(
                    LogFilePath,
                    "Foundry.OSD",
                    DiagnosticSessionContext.CurrentSessionId,
                    LogEventLevel.Information,
                    RetainedLogFileCount,
                    MinimumLevelSwitch,
                    RemoteDiagnosticsSink.Instance);
            }
            catch (Exception ex)
            {
                initializationException = ex;
                LogFilePath = "<unavailable>";
                Logger = FoundryLogConfiguration.CreateDebugLogger(
                    "Foundry.OSD",
                    DiagnosticSessionContext.CurrentSessionId,
                    LogEventLevel.Information,
                    MinimumLevelSwitch,
                    RemoteDiagnosticsSink.Instance);
            }

            Log.Logger = Logger;
            if (initializationException is not null)
            {
                SetupLogger.Error(initializationException, "File logging initialization failed. Falling back to debugger output.");
            }
        }

        /// <summary>
        /// Updates the minimum logging level used by runtime diagnostics.
        /// </summary>
        /// <param name="isEnabled">Whether developer diagnostics should enable debug logging.</param>
        public static void SetDeveloperModeEnabled(bool isEnabled)
        {
            LogEventLevel targetLevel = isEnabled ? LogEventLevel.Debug : LogEventLevel.Information;
            if (MinimumLevelSwitch.MinimumLevel == targetLevel)
            {
                return;
            }

            MinimumLevelSwitch.MinimumLevel = targetLevel;
            SetupLogger.Information("Developer diagnostics logging level changed. DeveloperMode={DeveloperMode}, MinimumLevel={MinimumLevel}", isEnabled, targetLevel);
        }

        /// <summary>
        /// Registers process-wide exception handlers once for non-UI failures.
        /// </summary>
        public static void RegisterGlobalExceptionHandlers()
        {
            if (globalExceptionHandlersRegistered)
            {
                return;
            }

            AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
            TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
            globalExceptionHandlersRegistered = true;
        }

        private static void OnUnhandledException(object sender, System.UnhandledExceptionEventArgs e)
        {
            if (e.ExceptionObject is Exception ex)
            {
                SetupLogger.Fatal(ex, "Unhandled AppDomain exception. IsTerminating={IsTerminating}", e.IsTerminating);
                if (e.IsTerminating)
                {
                    Log.CloseAndFlush();
                }

                return;
            }

            SetupLogger.Fatal("Unhandled AppDomain exception. IsTerminating={IsTerminating}, ExceptionObject={ExceptionObject}", e.IsTerminating, e.ExceptionObject);
            if (e.IsTerminating)
            {
                Log.CloseAndFlush();
            }
        }

        private static void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
        {
            SetupLogger.Error(e.Exception, "Unobserved task exception.");
            e.SetObserved();
        }

    }
}
