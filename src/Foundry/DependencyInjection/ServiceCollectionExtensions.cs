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
using Foundry.Telemetry;
using Microsoft.Extensions.Logging;
using Serilog;
using System.Globalization;
using System.Net.Http;
using System.Runtime.InteropServices;

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
        services.AddSingleton(sp =>
        {
            FoundryAppSettings settings = sp.GetRequiredService<IAppSettingsService>().Current;
            return new TelemetryOptions(
                settings.Telemetry.IsEnabled,
                TelemetryDefaults.PostHogEuHost,
                TelemetryDefaults.ProjectToken,
                settings.Telemetry.InstallId);
        });
        services.AddSingleton(_ => new TelemetryContext(
            "foundry",
            FoundryApplicationInfo.Version,
            TelemetryBuildConfiguration.Current,
            TelemetryRuntimeModes.Desktop,
            TelemetryRuntimePayloadSources.None,
            RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant(),
            CultureInfo.CurrentUICulture.Name,
            Guid.NewGuid().ToString("D")));
        services.AddSingleton<ITelemetryService>(sp =>
        {
            TelemetryOptions options = sp.GetRequiredService<TelemetryOptions>();
            Microsoft.Extensions.Logging.ILogger<PostHogTelemetryService> logger =
                sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<PostHogTelemetryService>>();
            logger.LogDebug(
                "Configuring telemetry service. App={App}, IsEnabled={IsEnabled}, HasProjectToken={HasProjectToken}, HasInstallId={HasInstallId}, HostUrl={HostUrl}.",
                "foundry",
                options.IsEnabled,
                !string.IsNullOrWhiteSpace(options.ProjectToken),
                !string.IsNullOrWhiteSpace(options.InstallId),
                options.HostUrl);

            if (!options.CanSend)
            {
                logger.LogDebug("Telemetry service disabled for Foundry because runtime options are incomplete or disabled.");
                return new NullTelemetryService();
            }

            return new PostHogTelemetryService(
                new HttpClient(),
                options,
                sp.GetRequiredService<TelemetryContext>(),
                logger);
        });
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
        services.AddTransient<SettingsPageViewModel>();
        services.AddTransient<GeneralSettingViewModel>();
        services.AddTransient<AdkPageViewModel>();
        services.AddTransient<AppUpdateSettingViewModel>();
        services.AddTransient<AboutUsSettingViewModel>();

        return services;
    }
}
