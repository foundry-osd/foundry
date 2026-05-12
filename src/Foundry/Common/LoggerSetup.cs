using Serilog;
using Serilog.Core;
using Serilog.Events;

namespace Foundry.Common
{
    /// <summary>
    /// Configures process-wide Serilog logging and global exception capture for the WinUI app.
    /// </summary>
    public static partial class LoggerSetup
    {
        private static readonly LoggingLevelSwitch MinimumLevelSwitch = new(LogEventLevel.Information);
        private static bool globalExceptionHandlersRegistered;

        /// <summary>
        /// Gets the active Serilog logger instance.
        /// </summary>
        public static ILogger Logger { get; private set; } = Serilog.Core.Logger.None;
        private static ILogger SetupLogger => Log.ForContext("SourceContext", typeof(LoggerSetup).FullName);

        /// <summary>
        /// Initializes the Foundry log sinks and assigns <see cref="Log.Logger"/>.
        /// </summary>
        public static void ConfigureLogger()
        {
            Directory.CreateDirectory(Constants.LogDirectoryPath);

            Logger = new LoggerConfiguration()
                .MinimumLevel.ControlledBy(MinimumLevelSwitch)
                .Enrich.FromLogContext()
                .Enrich.With<LogComponentEnricher>()
                .WriteTo.File(
                    Constants.LogFilePath,
                    outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] [{Component}] {Message:lj}{NewLine}{Exception}",
                    shared: true)
                .WriteTo.Debug()
                .CreateLogger();

            Log.Logger = Logger;
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
                return;
            }

            SetupLogger.Fatal("Unhandled AppDomain exception. IsTerminating={IsTerminating}, ExceptionObject={ExceptionObject}", e.IsTerminating, e.ExceptionObject);
        }

        private static void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
        {
            SetupLogger.Error(e.Exception, "Unobserved task exception.");
            e.SetObserved();
        }

        private sealed class LogComponentEnricher : ILogEventEnricher
        {
            /// <inheritdoc />
            public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
            {
                string component = Constants.ApplicationName;
                if (logEvent.Properties.TryGetValue("SourceContext", out LogEventPropertyValue? sourceContextValue) &&
                    sourceContextValue is ScalarValue { Value: string sourceContext } &&
                    !string.IsNullOrWhiteSpace(sourceContext))
                {
                    component = sourceContext[(sourceContext.LastIndexOf('.') + 1)..];
                }

                logEvent.AddPropertyIfAbsent(propertyFactory.CreateProperty("Component", component));
            }
        }
    }
}
