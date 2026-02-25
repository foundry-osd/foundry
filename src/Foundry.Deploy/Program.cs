using System.Windows.Threading;
using Foundry.Deploy.Services.ApplicationShell;
using Foundry.Deploy.Services.Autopilot;
using Foundry.Deploy.Services.Cache;
using Foundry.Deploy.Services.Catalog;
using Foundry.Deploy.Services.Deployment;
using Foundry.Deploy.Services.Download;
using Foundry.Deploy.Services.DriverPacks;
using Foundry.Deploy.Services.Hardware;
using Foundry.Deploy.Services.Logging;
using Foundry.Deploy.Services.Operations;
using Foundry.Deploy.Services.System;
using Foundry.Deploy.Services.Theme;
using Foundry.Deploy.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using Serilog;

namespace Foundry.Deploy;

public static class Program
{
    private const string DisableFluentBackdropSwitch = "Switch.System.Windows.Appearance.DisableFluentThemeWindowBackdrop";

    [STAThread]
    public static int Main()
    {
        string startupLogFilePath = FoundryDeployLogging.ResolveStartupLogFilePath();
        Log.Logger = FoundryDeployLogging.CreateLogger(startupLogFilePath);
        RegisterGlobalExceptionHandlers();

        try
        {
            Log.Information("Starting Foundry.Deploy bootstrap.");
            ConfigureRuntimeCompatibility();

            using ServiceProvider serviceProvider = BuildServiceProvider();

            App app = serviceProvider.GetRequiredService<App>();
            app.DispatcherUnhandledException += OnDispatcherUnhandledException;
            app.InitializeComponent();

            MainWindow mainWindow = serviceProvider.GetRequiredService<MainWindow>();
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

        return IsRunningInWinPe();
    }

    private static bool IsRunningInWinPe()
    {
        string? systemDrive = Environment.GetEnvironmentVariable("SystemDrive");
        if (systemDrive is not null && systemDrive.Equals("X:", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        string windowsDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        if (!string.IsNullOrWhiteSpace(windowsDirectory) &&
            windowsDirectory.StartsWith(@"X:\", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        try
        {
            using RegistryKey? miniNt = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\MiniNT");
            if (miniNt is not null)
            {
                return true;
            }
        }
        catch
        {
            // Ignore registry access errors and continue with fallback checks.
        }

        return false;
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

    private static ServiceProvider BuildServiceProvider()
    {
        ServiceCollection services = new();

        services.AddLogging(builder =>
        {
            builder.ClearProviders();
            builder.AddSerilog(dispose: false);
        });

        services.AddSingleton<App>();
        services.AddSingleton<MainWindow>();
        services.AddSingleton<MainWindowViewModel>();

        services.AddSingleton<IThemeService, ThemeService>();
        services.AddSingleton<IApplicationShellService, ApplicationShellService>();
        services.AddSingleton<IOperationProgressService, OperationProgressService>();
        services.AddSingleton<IProcessRunner, ProcessRunner>();
        services.AddSingleton<ICacheLocatorService, CacheLocatorService>();
        services.AddSingleton<IDeploymentLogService, DeploymentLogService>();
        services.AddSingleton<IHardwareProfileService, HardwareProfileService>();
        services.AddSingleton<ITargetDiskService, TargetDiskService>();
        services.AddSingleton<IOperatingSystemCatalogService, OperatingSystemCatalogService>();
        services.AddSingleton<IDriverPackCatalogService, DriverPackCatalogService>();
        services.AddSingleton<IDriverPackSelectionService, DriverPackSelectionService>();
        services.AddSingleton<IMicrosoftUpdateCatalogDriverService, MicrosoftUpdateCatalogDriverService>();
        services.AddSingleton<IArtifactDownloadService, ArtifactDownloadService>();
        services.AddSingleton<IDriverPackPreparationService, DriverPackPreparationService>();
        services.AddSingleton<IWindowsDeploymentService, WindowsDeploymentService>();
        services.AddSingleton<IAutopilotService, AutopilotService>();
        services.AddSingleton<IDeploymentOrchestrator, DeploymentOrchestrator>();

        return services.BuildServiceProvider();
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
