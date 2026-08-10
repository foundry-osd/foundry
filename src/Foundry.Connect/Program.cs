// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using Foundry.Connect.DependencyInjection;
using Foundry.Connect.Models;
using Foundry.Connect.Services.Configuration;
using Foundry.Connect.Services.Logging;
using Foundry.Connect.Services.Runtime;
using Foundry.Telemetry;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;

namespace Foundry.Connect;

/// <summary>
/// Provides the Avalonia entry point for Foundry.Connect in WinPE.
/// </summary>
public static class Program
{
    /// <summary>
    /// Configures logging, validates runtime constraints, builds the host, and runs the Avalonia shell.
    /// </summary>
    /// <param name="args">Command-line arguments passed to Foundry.Connect.</param>
    /// <returns>The process exit code.</returns>
    [STAThread]
    public static int Main(string[] args)
    {
        string startupLogFilePath = FoundryConnectLogging.ResolveStartupLogFilePath();
        Log.Logger = FoundryConnectLogging.CreateLogger(startupLogFilePath);
        RegisterGlobalExceptionHandlers();

        try
        {
            Log.Information("Starting Foundry.Connect bootstrap.");
            if (!RuntimeStartupGuard.CanRun())
            {
                Log.Error("Foundry.Connect can only run in WinPE outside a DEBUG debugger session.");
                return (int)FoundryConnectExitCode.StartupFailure;
            }

            using IHost host = BuildHost(args);
            Log.Information("Host built successfully.");
            ITelemetryService telemetryService = host.Services.GetRequiredService<ITelemetryService>();
            App.Services = host.Services;
            Dispatcher.UIThread.UnhandledException += OnDispatcherUnhandledException;

            Log.Information("Entering Avalonia run loop.");
            int exitCode = BuildAvaloniaApp().StartWithClassicDesktopLifetime(
                args,
                ShutdownMode.OnMainWindowClose);
            Log.Debug("Flushing Foundry.Connect telemetry events.");
            telemetryService.FlushAsync().GetAwaiter().GetResult();
            Log.Debug("Foundry.Connect telemetry flush completed.");

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
            App.Services = null;
            Log.CloseAndFlush();
        }
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect();

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
        Log.Fatal(args.Exception, "Unhandled Avalonia dispatcher exception.");
        args.Handled = true;
        (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.Shutdown(
            (int)FoundryConnectExitCode.StartupFailure);
    }
}
