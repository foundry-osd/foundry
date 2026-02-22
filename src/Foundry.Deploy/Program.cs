using System.IO;
using Foundry.Deploy.Services.ApplicationShell;
using Foundry.Deploy.Services.Catalog;
using Foundry.Deploy.Services.Deployment;
using Foundry.Deploy.Services.Autopilot;
using Foundry.Deploy.Services.Cache;
using Foundry.Deploy.Services.Download;
using Foundry.Deploy.Services.DriverPacks;
using Foundry.Deploy.Services.Hardware;
using Foundry.Deploy.Services.Logging;
using Foundry.Deploy.Services.Operations;
using Foundry.Deploy.Services.System;
using Foundry.Deploy.Services.Theme;
using Foundry.Deploy.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace Foundry.Deploy;

public static class Program
{
    private static readonly string StartupLogPath = ResolveStartupLogPath();

    [STAThread]
    public static int Main()
    {
        try
        {
            WriteStartupLog("Starting Foundry.Deploy bootstrap.");

            using ServiceProvider serviceProvider = BuildServiceProvider();

            App app = serviceProvider.GetRequiredService<App>();
            app.InitializeComponent();

            MainWindow mainWindow = serviceProvider.GetRequiredService<MainWindow>();
            int exitCode = app.Run(mainWindow);

            WriteStartupLog($"Foundry.Deploy exited with code {exitCode}.");
            return exitCode;
        }
        catch (Exception ex)
        {
            WriteStartupLog("Fatal startup error.", ex);
            Console.Error.WriteLine("Foundry.Deploy failed to start.");
            Console.Error.WriteLine($"Startup log: {StartupLogPath}");
            return 1;
        }
    }

    private static void WriteStartupLog(string message, Exception? exception = null)
    {
        try
        {
            string text = $"{DateTime.UtcNow:O} {message}";
            if (exception is not null)
            {
                text += Environment.NewLine + exception;
            }

            File.AppendAllText(StartupLogPath, text + Environment.NewLine);
        }
        catch
        {
            // Ignore startup log write failures.
        }
    }

    private static string ResolveStartupLogPath()
    {
        string[] candidateDirectories =
        [
            @"X:\Windows\Temp\Foundry\Deploy",
            Path.Combine(Path.GetTempPath(), "Foundry", "Deploy"),
            AppContext.BaseDirectory
        ];

        foreach (string candidateDirectory in candidateDirectories)
        {
            if (string.IsNullOrWhiteSpace(candidateDirectory))
            {
                continue;
            }

            try
            {
                Directory.CreateDirectory(candidateDirectory);
                return Path.Combine(candidateDirectory, "Foundry.Deploy.startup.log");
            }
            catch
            {
                // Try next location.
            }
        }

        return "Foundry.Deploy.startup.log";
    }

    private static ServiceProvider BuildServiceProvider()
    {
        ServiceCollection services = new();

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
        services.AddSingleton<IArtifactDownloadService, ArtifactDownloadService>();
        services.AddSingleton<IDriverPackPreparationService, DriverPackPreparationService>();
        services.AddSingleton<IWindowsDeploymentService, WindowsDeploymentService>();
        services.AddSingleton<IAutopilotService, AutopilotService>();
        services.AddSingleton<IDeploymentOrchestrator, DeploymentOrchestrator>();

        return services.BuildServiceProvider();
    }
}
