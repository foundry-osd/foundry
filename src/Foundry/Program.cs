using System.Diagnostics;
using System.Security.Principal;
using System.Windows;
using Foundry.Services.Adk;
using Foundry.Services.ApplicationShell;
using Foundry.Services.Localization;
using Foundry.Services.Operations;
using Foundry.Services.Theme;
using Foundry.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace Foundry;

public static class Program
{
    [STAThread]
    public static void Main()
    {
        if (!IsRunningAsAdministrator())
        {
            RelaunchAsAdministrator();
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
        using WindowsIdentity identity = WindowsIdentity.GetCurrent();
        WindowsPrincipal principal = new(identity);
        return principal.IsInRole(WindowsBuiltInRole.Administrator);
    }

    private static void RelaunchAsAdministrator()
    {
        string? processPath = Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule?.FileName;
        if (string.IsNullOrWhiteSpace(processPath))
        {
            MessageBox.Show("Administrator privileges are required to run this application.", "Foundry", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        ProcessStartInfo startInfo = new()
        {
            UseShellExecute = true,
            FileName = processPath,
            Verb = "runas"
        };

        try
        {
            Process.Start(startInfo);
        }
        catch
        {
            MessageBox.Show("Administrator privileges are required to run this application.", "Foundry", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
