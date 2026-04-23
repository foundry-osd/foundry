using System.Windows.Threading;
using Foundry.Deploy.Services.ApplicationShell;
using Foundry.Deploy.Services.Cache;
using Foundry.Deploy.Services.Catalog;
using Foundry.Deploy.Services.Configuration;
using Foundry.Deploy.DependencyInjection;
using Foundry.Deploy.Services.Deployment;
using Foundry.Deploy.Services.Deployment.Steps;
using Foundry.Deploy.Services.Download;
using Foundry.Deploy.Services.DriverPacks;
using Foundry.Deploy.Services.Hardware;
using Foundry.Deploy.Services.Logging;
using Foundry.Deploy.Services.Operations;
using Foundry.Deploy.Services.Runtime;
using Foundry.Deploy.Services.System;
using Foundry.Deploy.Services.Theme;
using Foundry.Deploy.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;

namespace Foundry.Deploy;

public static class Program
{
    private const string DisableFluentBackdropSwitch = "Switch.System.Windows.Appearance.DisableFluentThemeWindowBackdrop";

    [STAThread]
    public static int Main(string[] args)
    {
        string startupLogFilePath = FoundryDeployLogging.ResolveStartupLogFilePath();
        Log.Logger = FoundryDeployLogging.CreateLogger(startupLogFilePath);
        RegisterGlobalExceptionHandlers();

        try
        {
            Log.Information("Starting Foundry.Deploy bootstrap.");
            if (!RuntimeStartupGuard.CanRun())
            {
                Log.Error("Foundry.Deploy can only run in WinPE outside a DEBUG debugger session.");
                return 1;
            }

            ConfigureRuntimeCompatibility();

            using IHost host = BuildHost(args);

            App app = host.Services.GetRequiredService<App>();
            app.DispatcherUnhandledException += OnDispatcherUnhandledException;
            app.InitializeComponent();

            MainWindow mainWindow = host.Services.GetRequiredService<MainWindow>();
            int exitCode = app.Run(mainWindow);

            Log.Information("Foundry.Deploy exited with code {ExitCode}.", exitCode);
            return exitCode;
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Foundry.Deploy failed to start or terminated unexpectedly.");
            return 1;
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

    private static IHost BuildHost(string[] args)
    {
        HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);

        builder.Logging.ClearProviders();
        builder.Logging.AddSerilog(dispose: false);

        builder.Services.AddFoundryDeployApplicationServices();

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
    }
}
