// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.IO;
using System.Windows;
using Foundry.Deploy.DependencyInjection;
using Foundry.Deploy.Services.Configuration;
using Foundry.Deploy.Services.Logging;
using Foundry.Deploy.Services.Runtime;
using Foundry.Core.Models.Runtime;
using Foundry.Core.Services.Runtime;
using Foundry.Telemetry;
using Foundry.Utilities.Diagnostics;
using Foundry.Utilities.Runtime;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;

namespace Foundry.Deploy;

/// <summary>Runs the WPF application on its original STA thread and protects each startup boundary.</summary>
public static class Program
{
    private const string DisableFluentBackdropSwitch = "Switch.System.Windows.Appearance.DisableFluentThemeWindowBackdrop";
    private static readonly TimeSpan DiagnosticsShutdownTimeout = TimeSpan.FromSeconds(2);

    /// <summary>Starts configuration and services before entering the synchronous WPF dispatcher loop.</summary>
    [STAThread]
    public static int Main(string[] args)
    {
        string startupLogFilePath = "<unavailable>";
        IHost? host = null;
        ITelemetryService? telemetryService = null;
        IRemoteDiagnosticsService? remoteDiagnosticsService = null;
        RuntimeStartupDiagnostics? startup = null;
        Serilog.ILogger programLogger = Serilog.Core.Logger.None;
        string stage = "managed_startup";
        try
        {
            startup = RuntimeStartupDiagnostics.Create(DeployConfigurationService.DefaultConfigurationPath, configurationRequired: false);
            RegisterGlobalExceptionHandlers(startup);
            try
            {
                startupLogFilePath = FoundryDeployLogging.ResolveStartupLogFilePath();
                Log.Logger = FoundryDeployLogging.CreateLogger(startupLogFilePath);
                startup.SetLocalLogPath(startupLogFilePath);
            }
            catch (Exception exception)
            {
                startupLogFilePath = "<unavailable>";
                Log.Logger = FoundryLogConfiguration.CreateDebugLogger(
                    "Foundry.Deploy", DiagnosticSessionContext.CurrentSessionId,
                    Serilog.Events.LogEventLevel.Debug, additionalSink: RemoteDiagnosticsSink.Instance);
                Log.ForContext(typeof(Program)).Error(exception, "File logging initialization failed. Falling back to debugger output.");
            }

            programLogger = Log.ForContext(typeof(Program));
            programLogger.Information(
                "Foundry.Deploy bootstrap started. Version={Version}, SessionId={SessionId}, LogFilePath={LogFilePath}",
                FoundryDeployApplicationInfo.Version, DiagnosticSessionContext.CurrentSessionId, startupLogFilePath);
            if (!RuntimeStartupGuard.CanRun())
                throw new InvalidOperationException("Foundry.Deploy requires Windows PE outside a DEBUG debugger session.");

            ConfigureRuntimeCompatibility();
            stage = "configuration";
            host = BuildHost(args, startup);
            DeployConfigurationLoadResult configuration = host.Services.GetRequiredService<IDeployConfigurationService>().LoadOptional();
            if (startup.ProtocolEnabled && configuration.Exists && configuration.Document is null)
            {
                startup.ReportFailure(configuration.FailureException ?? new InvalidDataException("Deployment configuration is invalid."), stage);
                return 1;
            }
            startup.ReportConfigurationLoaded();
            stage = "services";
            telemetryService = host.Services.GetRequiredService<ITelemetryService>();
            remoteDiagnosticsService = host.Services.GetRequiredService<IRemoteDiagnosticsService>();
            InitializeRemoteDiagnostics(host.Services, remoteDiagnosticsService);

            stage = "ui_startup";
            App app = host.Services.GetRequiredService<App>();
            app.DispatcherUnhandledException += (_, eventArgs) =>
            {
                startup.ReportFailure(eventArgs.Exception, "dispatcher");
                eventArgs.Handled = true;
                app.Shutdown(1);
            };
            app.InitializeComponent();
            MainWindow mainWindow = host.Services.GetRequiredService<MainWindow>();
            int exitCode = app.Run(mainWindow);
            programLogger.Information("Foundry.Deploy exited with code {ExitCode}.", exitCode);
            return exitCode;
        }
        catch (Exception exception)
        {
            ReportFailure(startup, exception, stage);
            return 1;
        }
        finally
        {
            ShutdownDiagnostics(telemetryService, remoteDiagnosticsService);
            try { host?.Dispose(); }
            catch (Exception exception) { programLogger.Warning(exception, "Application service disposal failed."); }
            try { Task.Run(() => Log.CloseAndFlushAsync().AsTask()).WaitAsync(DiagnosticsShutdownTimeout).GetAwaiter().GetResult(); }
            catch { }
            try { FoundryDeployLogging.PersistCurrentLogs(); }
            catch { }
        }
    }

    private static void ReportFailure(RuntimeStartupDiagnostics? startup, Exception exception, string category)
    {
        if (startup is not null)
        {
            startup.ReportFailure(exception, category);
            return;
        }
        try
        {
            RuntimeStartupReporter.FromEnvironment("Foundry.Deploy")?.Report(StartupStage.StartupFailed, category);
        }
        catch { }
        try
        {
            Log.ForContext(typeof(Program)).Fatal(exception, "Application initialization failed at {StartupCategory}.", category);
        }
        catch { }
    }

    private static void ConfigureRuntimeCompatibility()
    {
        if (!ShouldDisableFluentBackdrop())
        {
            return;
        }

        AppContext.SetSwitch(DisableFluentBackdropSwitch, true);
        Log.Information("Enabled '{SwitchName}'.", DisableFluentBackdropSwitch);
    }

    private static bool ShouldDisableFluentBackdrop()
    {
        string? overrideValue = Environment.GetEnvironmentVariable("FOUNDRY_DISABLE_FLUENT_BACKDROP");
        if (!string.IsNullOrWhiteSpace(overrideValue))
        {
            return IsTruthy(overrideValue);
        }

        return WinPeRuntimeDetector.IsWinPeRuntime();
    }

    private static bool IsTruthy(string value)
    {
        return value.Trim() switch
        {
            "1" => true,
            "true" => true,
            "TRUE" => true,
            "yes" => true,
            "YES" => true,
            "on" => true,
            "ON" => true,
            _ => false
        };
    }

    private static IHost BuildHost(string[] args, RuntimeStartupDiagnostics startup)
    {
        HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);
        builder.Logging.ClearProviders();
        builder.Logging.AddSerilog(dispose: false);
        builder.Services.AddSingleton(startup);
        builder.Services.AddFoundryDeployApplicationServices();
        return builder.Build();
    }

    private static void InitializeRemoteDiagnostics(IServiceProvider services, IRemoteDiagnosticsService remoteDiagnosticsService)
    {
        TelemetrySettings settings = services.GetRequiredService<TelemetrySettings>();
        TelemetryContext context = services.GetRequiredService<TelemetryContext>();
        RemoteDiagnosticsLifecycle.Initialize(remoteDiagnosticsService, settings, context);
    }

    private static void ShutdownDiagnostics(ITelemetryService? telemetry, IRemoteDiagnosticsService? diagnostics)
    {
        try
        {
            Task.Run(async () =>
            {
                using var timeout = new CancellationTokenSource(DiagnosticsShutdownTimeout);
                if (telemetry is not null)
                {
                    try { await telemetry.FlushAsync().WaitAsync(timeout.Token).ConfigureAwait(false); }
                    catch { }
                }
                if (diagnostics is not null)
                    await RemoteDiagnosticsLifecycle.ShutdownAsync(diagnostics, timeout.Token).ConfigureAwait(false);
            }).WaitAsync(DiagnosticsShutdownTimeout).GetAwaiter().GetResult();
        }
        catch { }
    }

    private static void RegisterGlobalExceptionHandlers(RuntimeStartupDiagnostics startup)
    {
        AppDomain.CurrentDomain.UnhandledException += (_, eventArgs) =>
            startup.RecordTerminatingException(eventArgs.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, eventArgs) =>
        {
            Log.ForContext(typeof(Program)).Error(eventArgs.Exception, "Unobserved task exception.");
            eventArgs.SetObserved();
        };
    }
}
