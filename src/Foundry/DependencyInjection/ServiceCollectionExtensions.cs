using Foundry.Core.Services.Application;
using Foundry.Core.Services.Adk;
using Foundry.Core.Services.Configuration;
using Foundry.Core.Services.WinPe;
using Foundry.Services.Application;
using Foundry.Services.Adk;
using Foundry.Services.Configuration;
using Foundry.Services.Localization;
using Foundry.Services.Operations;
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
        services.AddSingleton<IAdkInstallationProbe, WindowsAdkInstallationProbe>();
        services.AddSingleton<IExpertConfigurationService, ExpertConfigurationService>();
        services.AddSingleton<IDeployConfigurationGenerator, DeployConfigurationGenerator>();
        services.AddSingleton<ILanguageRegistryService, EmbeddedLanguageRegistryService>();
        services.AddSingleton<IExpertDeployConfigurationStateService, ExpertDeployConfigurationStateService>();
        services.AddSingleton<IWinPeLanguageDiscoveryService, WinPeLanguageDiscoveryService>();
        services.AddSingleton<IWinPeUsbMediaService, WinPeUsbMediaService>();
        services.AddSingleton<IOperationProgressService, OperationProgressService>();
        services.AddSingleton<IAdkService, AdkService>();
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
        services.AddTransient<GeneralConfigurationViewModel>();
        services.AddTransient<LocalizationConfigurationViewModel>();
        services.AddTransient<StartMediaViewModel>();
        services.AddTransient<GeneralSettingViewModel>();
        services.AddTransient<AdkPageViewModel>();
        services.AddTransient<AppUpdateSettingViewModel>();
        services.AddTransient<AboutUsSettingViewModel>();

        return services;
    }
}
