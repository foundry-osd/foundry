// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.IO;
using System.Windows;
using Foundry.Connect.DependencyInjection;
using Foundry.Core.Models.Configuration;
using Foundry.Connect.Configuration;
using Foundry.Connect.Lifetime;
using Foundry.Connect.Services.Logging;
using Foundry.Connect.Services.Runtime;
using Foundry.Connect.Startup;
using Foundry.Core.Models.Runtime;
using Foundry.Core.Services.Runtime;
using Foundry.Telemetry;
using Foundry.Utilities.Diagnostics;
using Foundry.Utilities.Runtime;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Extensions.Logging;

namespace Foundry.Connect;

/// <summary>Runs the WPF application on its original STA thread and protects each startup boundary.</summary>
public static class Program
{
    private const string DisableFluentBackdropSwitch = "Switch.System.Windows.Appearance.DisableFluentThemeWindowBackdrop";
    private static readonly RuntimeApplicationIdentity Identity = new("Foundry.Connect", TelemetryApps.FoundryConnect, FoundryConnectApplicationInfo.Version);
    private static readonly TimeSpan DiagnosticsShutdownTimeout = TimeSpan.FromSeconds(2);

    /// <summary>Starts configuration and services before entering the synchronous WPF dispatcher loop.</summary>
    [STAThread]
    public static int Main(string[] args)
    {
        DiagnosticClock.Current.InitializeRuntime(Environment.GetEnvironmentVariable(DiagnosticClock.EnvironmentVariableName));
        string startupLogFilePath = "<unavailable>";
        IHost? host = null;
        ITelemetryService? telemetryService = null;
        IRemoteDiagnosticsService? remoteDiagnosticsService = null;
        RuntimeStartupDiagnostics? startup = null;
        SingleInstanceGuard? instanceGuard = null;
        Serilog.ILogger programLogger = Serilog.Core.Logger.None;
        // Managed startup (logging, diagnostics, runtime checks) precedes the policy stages.
        ConnectStartupStage? stage = null;
        try
        {
            var configurationPath = ConnectConfigurationService.ResolveConfigurationPath(args);
            startup = RuntimeStartupDiagnostics.Create(Identity, configurationPath.Path ?? "", configurationPath.IsRequired);
            RegisterGlobalExceptionHandlers(startup);
            try
            {
                startupLogFilePath = FoundryConnectLogging.ResolveStartupLogFilePath();
                Log.Logger = FoundryConnectLogging.CreateLogger(startupLogFilePath);
                startup.SetLocalLogPath(startupLogFilePath);
            }
            catch (Exception exception)
            {
                startupLogFilePath = "<unavailable>";
                Log.Logger = FoundryLogConfiguration.CreateDebugLogger(
                    Identity.ApplicationId, DiagnosticSessionContext.CurrentSessionId,
                    Serilog.Events.LogEventLevel.Verbose, additionalSink: RemoteDiagnosticsSink.Instance);
                Log.ForContext(typeof(Program)).Error(exception, "File logging initialization failed. Falling back to debugger output.");
            }

            remoteDiagnosticsService = new PostHogRemoteDiagnosticsSink();
            startup.InitializeRemoteDiagnostics(remoteDiagnosticsService);
            programLogger = Log.ForContext(typeof(Program));
            programLogger.Information(
                "Foundry.Connect bootstrap started. Version={Version}, SessionId={SessionId}, LogFilePath={LogFilePath}",
                FoundryConnectApplicationInfo.Version, DiagnosticSessionContext.CurrentSessionId, startupLogFilePath);
            if (!RuntimeStartupGuard.CanRun())
                throw new InvalidOperationException("Foundry.Connect requires Windows PE outside a DEBUG debugger session.");

            if (!SingleInstanceGuard.TryAcquire(SingleInstanceGuard.ConnectInstanceName, out instanceGuard))
            {
                programLogger.Error("Another Foundry.Connect instance is already running. Exiting before any UI is shown.");
                return (int)ChildRuntimeExitCode.StartupFailure;
            }

            ConfigureRuntimeCompatibility();
            stage = ConnectStartupStage.Configuration;
            var configurationService = new ConnectConfigurationService(
                args, new SerilogLoggerFactory(Log.Logger).CreateLogger<ConnectConfigurationService>());
            ConnectConfigurationLoadResult loadResult = configurationService.Load();
            if (!loadResult.IsSuccess)
            {
                ReportFailure(startup, loadResult.FailureException, GetFailureCategory(stage));
                return (int)ConnectStartupFailurePolicy.Map(ConnectStartupStage.Configuration);
            }

            stage = ConnectStartupStage.Services;
            host = BuildHost(args, startup, remoteDiagnosticsService, configurationService, loadResult.Configuration, loadResult.Secrets);
            startup.ReportConfigurationLoaded();
            telemetryService = host.Services.GetRequiredService<ITelemetryService>();
            InitializeRemoteDiagnostics(host.Services, remoteDiagnosticsService);

            stage = ConnectStartupStage.UiStartup;
            App app = host.Services.GetRequiredService<App>();
            IApplicationLifetimeService lifetime = host.Services.GetRequiredService<IApplicationLifetimeService>();
            app.DispatcherUnhandledException += (_, eventArgs) =>
            {
                startup.ReportFailure(eventArgs.Exception, "dispatcher");
                eventArgs.Handled = true;
                lifetime.TryExit(GetFailureExitCode(ConnectStartupStage.UiStartup, startup));
            };
            app.InitializeComponent();
            MainWindow mainWindow = host.Services.GetRequiredService<MainWindow>();
            int exitCode = app.Run(mainWindow);
            programLogger.Information("Foundry.Connect exited with code {ExitCode}.", exitCode);
            return exitCode;
        }
        catch (Exception exception)
        {
            ReportFailure(startup, exception, GetFailureCategory(stage));
            return (int)GetFailureExitCode(stage, startup);
        }
        finally
        {
            try { host?.Dispose(); }
            catch (Exception exception) { programLogger.Warning(exception, "Application service disposal failed."); }
            programLogger.Information("Foundry.Connect application shutdown completed. Closing diagnostics.");
            ShutdownDiagnostics(telemetryService, remoteDiagnosticsService);
            try { Task.Run(() => Log.CloseAndFlushAsync().AsTask()).WaitAsync(DiagnosticsShutdownTimeout).GetAwaiter().GetResult(); }
            catch { }
            // Released last, on the thread that acquired it, so a next instance cannot start while this one still flushes.
            try { instanceGuard?.Dispose(); }
            catch { }
        }
    }

    /// <summary>
    /// Maps a failure to its exit code; failures before a policy stage keep the generic startup failure code, and once
    /// the UI reported readiness to Foundry.Bootstrap every failure is a runtime failure.
    /// </summary>
    private static ChildRuntimeExitCode GetFailureExitCode(ConnectStartupStage? stage, RuntimeStartupDiagnostics? startup)
    {
        if (stage is not { } current)
        {
            return ChildRuntimeExitCode.StartupFailure;
        }

        return ConnectStartupFailurePolicy.Map(startup?.IsUiReady == true ? ConnectStartupStage.Running : current);
    }

    /// <summary>Returns the failure category reported to Foundry.Bootstrap for a failure in <paramref name="stage"/>.</summary>
    private static string GetFailureCategory(ConnectStartupStage? stage) => stage switch
    {
        null => "managed_startup",
        ConnectStartupStage.Configuration => "configuration",
        ConnectStartupStage.Services => "services",
        _ => "ui_startup"
    };

    private static void ReportFailure(RuntimeStartupDiagnostics? startup, Exception exception, string category)
    {
        if (startup is not null)
        {
            startup.ReportFailure(exception, category);
            return;
        }
        try
        {
            RuntimeStartupReporter.FromEnvironment(Identity.ApplicationId)?.Report(StartupStage.StartupFailed, category);
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

        // WinPE does not provide the full desktop composition stack required by WPF fluent backdrop effects.
        return ConnectWorkspacePaths.IsWinPeRuntime();
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

    private static IHost BuildHost(
        string[] args,
        RuntimeStartupDiagnostics startup,
        IRemoteDiagnosticsService remoteDiagnosticsService,
        IConnectConfigurationService configurationService,
        FoundryConnectConfigurationDocument configuration,
        ConnectRuntimeSecrets secrets)
    {
        HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);
        builder.Logging.ClearProviders();
        builder.Logging.SetMinimumLevel(LogLevel.Trace);
        builder.Logging.AddSerilog(dispose: false);
        builder.Services.AddSingleton(startup);
        builder.Services.AddSingleton(configurationService);
        builder.Services.AddSingleton(configuration);
        builder.Services.AddSingleton(secrets);
        builder.Services.AddFoundryConnectApplicationServices(args);
        builder.Services.AddSingleton(remoteDiagnosticsService);
        return builder.Build();
    }

    private static void InitializeRemoteDiagnostics(IServiceProvider services, IRemoteDiagnosticsService remoteDiagnosticsService)
    {
        TelemetrySettings settings = services.GetRequiredService<FoundryConnectConfigurationDocument>().Telemetry;
        TelemetryContext context = services.GetRequiredService<TelemetryContext>();
        RemoteDiagnosticsLifecycle.Initialize(remoteDiagnosticsService, settings, context);
    }

    private static void ShutdownDiagnostics(ITelemetryService? telemetry, IRemoteDiagnosticsService? diagnostics)
    {
        Serilog.ILogger transportLogger = Log.ForContext(typeof(Program)).ForContext("PostHogTransportInternal", true);
        transportLogger.Debug("Flushing remote diagnostics.");
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
            transportLogger.Debug("Remote diagnostics shutdown completed.");
        }
        catch (Exception exception)
        {
            transportLogger.Warning(exception, "Remote diagnostics shutdown did not complete within the shutdown budget.");
        }
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
