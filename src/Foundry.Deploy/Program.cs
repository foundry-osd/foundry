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
    [STAThread]
    public static void Main()
    {
        using ServiceProvider serviceProvider = BuildServiceProvider();

        App app = serviceProvider.GetRequiredService<App>();
        app.InitializeComponent();

        MainWindow mainWindow = serviceProvider.GetRequiredService<MainWindow>();
        app.Run(mainWindow);
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
