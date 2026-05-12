using Foundry.Core.Services.Application;
using Foundry.Core.Services.Adk;
using Foundry.Core.Services.Configuration;
using Foundry.Core.Services.WinPe;
using Foundry.Services.Application;
using Foundry.Services.Adk;
using Foundry.Services.Autopilot;
using Foundry.Services.Configuration;
using Foundry.Services.GitHub;
using Foundry.Services.Localization;
using Foundry.Services.Operations;
using Foundry.Services.Settings;
using Foundry.Services.Shell;
using Foundry.Services.Startup;
using Foundry.Services.Updates;
using Serilog;

namespace Foundry.DependencyInjection;

/// <summary>
/// Registers the Foundry WinUI composition root.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds application services, view models, shell services, and Core service integrations.
    /// </summary>
    /// <param name="services">Service collection to update.</param>
    /// <returns>The same service collection for chaining.</returns>
    public static IServiceCollection AddFoundryApplicationServices(this IServiceCollection services)
    {
        services.AddSingleton(Log.Logger);

        services.AddSingleton<MainWindow>();

        services.AddSingleton<IAppSettingsService, JsonAppSettingsService>();
        services.AddSingleton<IAdkInstallationProbe, WindowsAdkInstallationProbe>();
        services.AddSingleton<IExpertConfigurationService, ExpertConfigurationService>();
        services.AddSingleton<IDeployConfigurationGenerator, DeployConfigurationGenerator>();
        services.AddSingleton<IConnectConfigurationGenerator, ConnectConfigurationGenerator>();
        services.AddSingleton<IAutopilotProfileImportService, AutopilotProfileImportService>();
        services.AddSingleton<IAutopilotTenantProfileService, AutopilotTenantProfileService>();
        services.AddSingleton<IAutopilotTenantDownloadDialogService, AutopilotTenantDownloadDialogService>();
        services.AddSingleton<IAutopilotProfileSelectionDialogService, AutopilotProfileSelectionDialogService>();
        services.AddSingleton<ILanguageRegistryService, EmbeddedLanguageRegistryService>();
        services.AddSingleton<INetworkSecretStateService, NetworkSecretStateService>();
        services.AddSingleton<IExpertDeployConfigurationStateService, ExpertDeployConfigurationStateService>();
        services.AddSingleton<IWinPeLanguageDiscoveryService, WinPeLanguageDiscoveryService>();
        services.AddSingleton<IWinPeEmbeddedAssetService, WinPeEmbeddedAssetService>();
        services.AddSingleton<IWinPeBuildService, WinPeBuildService>();
        services.AddSingleton<IWinPeWorkspacePreparationService, WinPeWorkspacePreparationService>();
        services.AddSingleton<IWinPeIsoMediaService, WinPeIsoMediaService>();
        services.AddSingleton<IWinPeUsbMediaService, WinPeUsbMediaService>();
        services.AddSingleton<IOperationProgressService, OperationProgressService>();
        services.AddSingleton<IAdkService, AdkService>();
        services.AddSingleton<IShellNavigationGuardService, ShellNavigationGuardService>();
        services.AddSingleton<IApplicationLocalizationService, ApplicationLocalizationService>();
        services.AddSingleton<IApplicationUpdateStateService, ApplicationUpdateStateService>();
        services.AddSingleton<IApplicationUpdateService, ApplicationUpdateService>();
        services.AddSingleton<IStartupReadinessService, StartupReadinessService>();
        services.AddSingleton<IGitHubRepositoryContributorService, GitHubRepositoryContributorService>();

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
        services.AddTransient<NetworkConfigurationViewModel>();
        services.AddTransient<AutopilotConfigurationViewModel>();
        services.AddTransient<CustomizationConfigurationViewModel>();
        services.AddTransient<StartMediaViewModel>();
        services.AddTransient<HomeLandingViewModel>();
        services.AddTransient<GeneralSettingViewModel>();
        services.AddTransient<AdkPageViewModel>();
        services.AddTransient<AppUpdateSettingViewModel>();
        services.AddTransient<AboutUsSettingViewModel>();

        return services;
    }
}
