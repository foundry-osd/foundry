using Foundry.Services.Adk;
using Foundry.Services.ApplicationShell;
using Foundry.Services.Localization;
using Foundry.Services.Operations;
using Foundry.Services.Theme;
using Foundry.Services.WinPe;
using Foundry.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using System.Diagnostics;

namespace Foundry;

public static class Program
{
    [STAThread]
    public static void Main()
    {
        ConfigureLocalWinPeDeployForDebugSession();

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

        services.AddSingleton<IApplicationShellService, ApplicationShellService>();
        services.AddSingleton<IThemeService, ThemeService>();
        services.AddSingleton<ILocalizationService, LocalizationService>();
        services.AddSingleton<IOperationProgressService, OperationProgressService>();
        services.AddSingleton<IAdkService, AdkService>();
        services.AddSingleton<IWinPeBuildService, WinPeBuildService>();
        services.AddSingleton<IWinPeDriverCatalogService, WinPeDriverCatalogService>();
        services.AddSingleton<IWinPeDriverInjectionService, WinPeDriverInjectionService>();
        services.AddSingleton<IMediaOutputService, MediaOutputService>();

        return services.BuildServiceProvider();
    }

    private static void ConfigureLocalWinPeDeployForDebugSession()
    {
#if DEBUG
        if (!Debugger.IsAttached)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(WinPeDefaults.LocalDeployEnableEnvironmentVariable)))
        {
            Environment.SetEnvironmentVariable(WinPeDefaults.LocalDeployEnableEnvironmentVariable, "1");
        }

        if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(WinPeDefaults.LocalDeployProjectEnvironmentVariable)))
        {
            return;
        }

        if (!TryFindFoundryDeployProjectPath(out string projectPath))
        {
            return;
        }

        Environment.SetEnvironmentVariable(WinPeDefaults.LocalDeployProjectEnvironmentVariable, projectPath);
#endif
    }

    private static bool TryFindFoundryDeployProjectPath(out string projectPath)
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            string candidate = Path.Combine(current.FullName, "src", "Foundry.Deploy", "Foundry.Deploy.csproj");
            if (File.Exists(candidate))
            {
                projectPath = candidate;
                return true;
            }

            current = current.Parent;
        }

        projectPath = string.Empty;
        return false;
    }
}
