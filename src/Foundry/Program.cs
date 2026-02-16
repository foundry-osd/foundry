using Foundry.Services.Adk;
using Foundry.Services.ApplicationShell;
using Foundry.Services.Localization;
using Foundry.Services.Operations;
using Foundry.Services.Theme;
using Foundry.ViewModels;
using Foundry.Views;
using Microsoft.Extensions.DependencyInjection;

namespace Foundry;

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

        services.AddSingleton<StandardPage>();
        services.AddSingleton<AdvancedPage>();
        services.AddSingleton<StandardPageViewModel>();
        services.AddSingleton<AdvancedPageViewModel>();

        services.AddSingleton<IApplicationShellService, ApplicationShellService>();
        services.AddSingleton<IThemeService, ThemeService>();
        services.AddSingleton<ILocalizationService, LocalizationService>();
        services.AddSingleton<IOperationProgressService, OperationProgressService>();
        services.AddSingleton<IAdkService, AdkService>();

        return services.BuildServiceProvider();
    }
}
