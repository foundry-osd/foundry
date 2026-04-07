using System.Windows;
using System.Windows.Threading;
using Foundry.Connect.DependencyInjection;
using Foundry.Connect.Models;
using Foundry.Connect.Services.Configuration;
using Foundry.Connect.Services.Logging;
using Foundry.Connect.Services.Runtime;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;

namespace Foundry.Connect;

public static class Program
{
    private const string DisableFluentBackdropSwitch = "Switch.System.Windows.Appearance.DisableFluentThemeWindowBackdrop";

    [STAThread]
    public static int Main(string[] args)
    {
        string startupLogFilePath = FoundryConnectLogging.ResolveStartupLogFilePath();
        Log.Logger = FoundryConnectLogging.CreateLogger(startupLogFilePath);
        RegisterGlobalExceptionHandlers();

        try
        {
            Log.Information("Starting Foundry.Connect bootstrap.");
            ConfigureRuntimeCompatibility();
            Log.Information("Runtime compatibility configuration completed.");

            using IHost host = BuildHost(args);
            Log.Information("Host built successfully.");

            App app = host.Services.GetRequiredService<App>();
            Log.Information("Resolved App instance.");
            app.DispatcherUnhandledException += OnDispatcherUnhandledException;
            app.InitializeComponent();
            Log.Information("App.InitializeComponent completed.");

            MainWindow mainWindow = host.Services.GetRequiredService<MainWindow>();
            Log.Information("Resolved MainWindow instance.");
            Log.Information("Entering WPF run loop.");
            int exitCode = app.Run(mainWindow);

            Log.Information("Foundry.Connect exited with code {ExitCode}.", exitCode);
            return exitCode;
        }
        catch (FoundryConnectConfigurationException ex)
        {
            Log.Fatal(ex, "Foundry.Connect configuration could not be loaded.");
            return (int)FoundryConnectExitCode.ConfigurationFailure;
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Foundry.Connect failed to start or terminated unexpectedly.");
            return (int)FoundryConnectExitCode.StartupFailure;
        }
        finally
        {
            Log.CloseAndFlush();
        }
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

    private static IHost BuildHost(string[] args)
    {
        HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);

        builder.Logging.ClearProviders();
        builder.Logging.AddSerilog(dispose: false);
        builder.Services.AddFoundryConnectApplicationServices(args);

        return builder.Build();
    }

    private static void RegisterGlobalExceptionHandlers()
    {
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception exception)
            {
                Log.Fatal(exception, "Unhandled AppDomain exception (IsTerminating={IsTerminating}).", args.IsTerminating);
                return;
            }

            Log.Fatal("Unhandled AppDomain exception object (IsTerminating={IsTerminating}): {ExceptionObject}",
                args.IsTerminating,
                args.ExceptionObject);
        };

        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            Log.Error(args.Exception, "Unobserved task exception.");
            args.SetObserved();
        };
    }

    private static void OnDispatcherUnhandledException(object? sender, DispatcherUnhandledExceptionEventArgs args)
    {
        Log.Fatal(args.Exception, "Unhandled WPF dispatcher exception.");
        args.Handled = true;

        Application? app = Application.Current;
        if (app is null)
        {
            return;
        }

        app.Shutdown((int)FoundryConnectExitCode.StartupFailure);
    }
}
