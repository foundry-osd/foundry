using Foundry.Core.Services.Application;
using Foundry.Services.Application;
using Foundry.Services.Localization;
using Foundry.Services.Settings;
using Foundry.Services.Shell;
using Foundry.Services.Startup;
using Foundry.Services.Updates;
using Serilog;

namespace Foundry.DependencyInjection;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddFoundryApplicationServices(this IServiceCollection services)
    {
        services.AddSingleton(Log.Logger);

        services.AddSingleton<MainWindow>();

        services.AddSingleton<IAppSettingsService, JsonAppSettingsService>();
        services.AddSingleton<IShellNavigationGuardService, ShellNavigationGuardService>();
        services.AddSingleton<IApplicationLocalizationService, ApplicationLocalizationService>();
        services.AddSingleton<IApplicationUpdateStateService, ApplicationUpdateStateService>();
        services.AddSingleton<IApplicationUpdateService, ApplicationUpdateService>();
        services.AddSingleton<IStartupReadinessService, StartupReadinessService>();

        services.AddSingleton<IThemeService, ThemeService>();
        services.AddSingleton<IJsonNavigationService, JsonNavigationService>();
        services.AddSingleton<IApplicationLifetimeService, WinUiApplicationLifetimeService>();
        services.AddSingleton<IAppDispatcher, WinUiAppDispatcher>();
        services.AddSingleton<IDialogService, WinUiDialogService>();
        services.AddSingleton<IExternalProcessLauncher, WinUiExternalProcessLauncher>();
        services.AddSingleton<IFilePickerService, WinUiFilePickerService>();

        services.AddTransient<MainViewModel>();
        services.AddSingleton<ContextMenuService>();
        services.AddTransient<GeneralSettingViewModel>();
        services.AddTransient<AppUpdateSettingViewModel>();
        services.AddTransient<AboutUsSettingViewModel>();

        return services;
    }
}
