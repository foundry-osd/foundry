using Foundry.Services.Autopilot;
using Foundry.Services.Adk;
using Foundry.Services.ApplicationShell;
using Foundry.Services.Configuration;
using Foundry.Services.Execution;
using Foundry.Services.Localization;
using Foundry.Services.Operations;
using Foundry.Services.Theme;
using Foundry.Services.WinPe;
using Foundry.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace Foundry.DependencyInjection;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddFoundryApplicationServices(this IServiceCollection services)
    {
        services.AddSingleton<App>();
        services.AddSingleton<MainWindow>();
        services.AddSingleton<MainWindowViewModel>();
        services.AddSingleton<AutopilotSettingsViewModel>();

        services.AddSingleton<IApplicationShellService, ApplicationShellService>();
        services.AddSingleton<IProcessExecutionService, ProcessExecutionService>();
        services.AddSingleton<IAutopilotProfileService, AutopilotProfileService>();
        services.AddSingleton<IExpertConfigurationService, ExpertConfigurationService>();
        services.AddSingleton<IDeployConfigurationGenerator, DeployConfigurationGenerator>();
        services.AddSingleton<ILanguageRegistryService, EmbeddedLanguageRegistryService>();
        services.AddSingleton<IThemeService, ThemeService>();
        services.AddSingleton<ILocalizationService, LocalizationService>();
        services.AddSingleton<IOperationProgressService, OperationProgressService>();
        services.AddSingleton<IAdkService, AdkService>();
        services.AddSingleton<IWinPeBuildService, WinPeBuildService>();
        services.AddSingleton<IWinPeDriverCatalogService, WinPeDriverCatalogService>();
        services.AddSingleton<IWinPeDriverInjectionService, WinPeDriverInjectionService>();
        services.AddSingleton<WinPeToolResolver>();
        services.AddSingleton<WinPeProcessRunner>();
        services.AddSingleton<WinPeDriverPackageService>();
        services.AddSingleton<IWinPeDriverResolutionService, WinPeDriverResolutionService>();
        services.AddSingleton<IWinPeImageInternationalizationService, WinPeImageInternationalizationService>();
        services.AddSingleton<IWinPeLocalDeployEmbeddingService, WinPeLocalDeployEmbeddingService>();
        services.AddSingleton<IWinPeMountedImageAssetProvisioningService, WinPeMountedImageAssetProvisioningService>();
        services.AddSingleton<IWinPeMountedImageCustomizationService, WinPeMountedImageCustomizationService>();
        services.AddSingleton<WinPeUsbMediaService>();
        services.AddSingleton<IWinPeWorkspacePreparationService, WinPeWorkspacePreparationService>();
        services.AddSingleton<IMediaOutputService, MediaOutputService>();

        return services;
    }
}
