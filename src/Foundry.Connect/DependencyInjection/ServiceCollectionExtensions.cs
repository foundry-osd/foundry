using Foundry.Connect.Services.ApplicationLifetime;
using Foundry.Connect.Services.ApplicationShell;
using Foundry.Connect.Services.Configuration;
using Foundry.Connect.Services.Network;
using Foundry.Connect.Services.Theme;
using Foundry.Connect.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace Foundry.Connect.DependencyInjection;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddFoundryConnectApplicationServices(this IServiceCollection services, string[] args)
    {
        services.AddSingleton<App>();
        services.AddSingleton<MainWindow>();
        services.AddSingleton<MainWindowViewModel>();
        services.AddSingleton(args ?? Array.Empty<string>());

        services.AddSingleton<IApplicationShellService, ApplicationShellService>();
        services.AddSingleton<IApplicationLifetimeService, ApplicationLifetimeService>();
        services.AddSingleton<IConnectConfigurationService, ConnectConfigurationService>();
        services.AddSingleton(sp => sp.GetRequiredService<IConnectConfigurationService>().Load());
        services.AddSingleton<INetworkBootstrapService, NetworkBootstrapService>();
        services.AddSingleton<INetworkStatusService, NetworkStatusService>();
        services.AddSingleton<IThemeService, ThemeService>();

        return services;
    }
}
