using Foundry.Services.Adk;
using Foundry.Services.ApplicationShell;
using Foundry.Services.Localization;
using Foundry.Services.Operations;
using Foundry.Services.Theme;
using Foundry.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using System.Security.Principal;
using System.Windows;

namespace Foundry;

public static class Program
{
    [STAThread]
    public static void Main()
    {
        if (!IsRunningAsAdministrator())
        {
            MessageBox.Show(
                "Foundry must be started with administrator privileges.",
                "Administrator privileges required",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            return;
        }

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

        return services.BuildServiceProvider();
    }

    private static bool IsRunningAsAdministrator()
    {
        WindowsIdentity? identity = WindowsIdentity.GetCurrent();
        if (identity is null)
        {
            return false;
        }

        WindowsPrincipal principal = new(identity);
        return principal.IsInRole(WindowsBuiltInRole.Administrator);
    }
}
