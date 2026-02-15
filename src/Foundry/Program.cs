using Foundry.Services;
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

        return services.BuildServiceProvider();
    }
}
