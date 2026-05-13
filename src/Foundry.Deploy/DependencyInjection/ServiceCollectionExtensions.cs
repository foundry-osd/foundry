using System.Globalization;
using System.Net.Http;
using System.Runtime.InteropServices;
using Foundry.Deploy.Models.Configuration;
using Foundry.Deploy.Services.ApplicationShell;
using Foundry.Deploy.Services.Autopilot;
using Foundry.Deploy.Services.Cache;
using Foundry.Deploy.Services.Catalog;
using Foundry.Deploy.Services.Configuration;
using Foundry.Deploy.Services.Deployment;
using Foundry.Deploy.Services.Deployment.PreOobe;
using Foundry.Deploy.Services.Deployment.Steps;
using Foundry.Deploy.Services.Download;
using Foundry.Deploy.Services.DriverPacks;
using Foundry.Deploy.Services.Hardware;
using Foundry.Deploy.Services.Logging;
using Foundry.Deploy.Services.Localization;
using Foundry.Deploy.Services.Operations;
using Foundry.Deploy.Services.Runtime;
using Foundry.Deploy.Services.Startup;
using Foundry.Deploy.Services.System;
using Foundry.Deploy.Services.Theme;
using Foundry.Deploy.Services.Wizard;
using Foundry.Deploy.ViewModels;
using Foundry.Telemetry;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Foundry.Deploy.DependencyInjection;

public static class ServiceCollectionExtensions
{
    private const string DeploymentModeEnvironmentVariable = "FOUNDRY_DEPLOYMENT_MODE";

    public static IServiceCollection AddFoundryDeployApplicationServices(this IServiceCollection services)
    {
        services.AddSingleton<App>();
        services.AddSingleton<MainWindow>();
        services.AddSingleton<MainWindowViewModel>();

        services.AddSingleton<ILocalizationService, LocalizationService>();
        services.AddSingleton<IThemeService, ThemeService>();
        services.AddSingleton<IApplicationShellService, ApplicationShellService>();
        services.AddSingleton<IDeploymentWizardStateService, DeploymentWizardStateService>();
        services.AddSingleton<IDeploymentWizardContextFactory, DeploymentWizardContextFactory>();
        services.AddSingleton<IDeploymentStartupCoordinator, DeploymentStartupCoordinator>();
        services.AddSingleton<IOperationProgressService, OperationProgressService>();
        services.AddSingleton<IDeploymentRuntimeContextService, DeploymentRuntimeContextService>();
        services.AddSingleton<IExpertDeployConfigurationService, ExpertDeployConfigurationService>();
        services.AddSingleton(CreateTelemetryOptions);
        services.AddSingleton(CreateTelemetryContext);
        services.AddSingleton<ITelemetryService>(sp =>
        {
            TelemetryOptions options = sp.GetRequiredService<TelemetryOptions>();
            ILogger<PostHogTelemetryService> logger = sp.GetRequiredService<ILogger<PostHogTelemetryService>>();
            logger.LogDebug(
                "Configuring telemetry service. App={App}, IsEnabled={IsEnabled}, HasProjectToken={HasProjectToken}, HasInstallId={HasInstallId}, HostUrl={HostUrl}.",
                TelemetryApps.FoundryDeploy,
                options.IsEnabled,
                !string.IsNullOrWhiteSpace(options.ProjectToken),
                !string.IsNullOrWhiteSpace(options.InstallId),
                options.HostUrl);

            if (!options.CanSend)
            {
                logger.LogDebug("Telemetry service disabled for Foundry.Deploy because runtime options are incomplete or disabled.");
                return new NullTelemetryService();
            }

            return new PostHogTelemetryService(
                new HttpClient(),
                options,
                sp.GetRequiredService<TelemetryContext>(),
                logger);
        });
        services.AddSingleton<IDeploymentLaunchPreparationService, DeploymentLaunchPreparationService>();
        services.AddSingleton<IDeploymentExecutionService, DeploymentExecutionService>();
        services.AddSingleton<IProcessRunner, ProcessRunner>();
        services.AddSingleton<IArchiveExtractionService, ArchiveExtractionService>();
        services.AddSingleton<ICacheLocatorService, CacheLocatorService>();
        services.AddSingleton<IDeploymentLogService, DeploymentLogService>();
        services.AddSingleton<IHardwareProfileService, HardwareProfileService>();
        services.AddSingleton<IOfflineWindowsComputerNameService, OfflineWindowsComputerNameService>();
        services.AddSingleton<ITargetDiskService, TargetDiskService>();
        services.AddSingleton<IOperatingSystemCatalogService, OperatingSystemCatalogService>();
        services.AddSingleton<IDriverPackCatalogService, DriverPackCatalogService>();
        services.AddSingleton<IDeploymentCatalogLoadService, DeploymentCatalogLoadService>();
        services.AddSingleton<IDriverPackSelectionService, DriverPackSelectionService>();
        services.AddSingleton<IMicrosoftUpdateCatalogClient, MicrosoftUpdateCatalogClient>();
        services.AddSingleton<IMicrosoftUpdateCatalogDriverService, MicrosoftUpdateCatalogDriverService>();
        services.AddSingleton<IMicrosoftUpdateCatalogFirmwareService, MicrosoftUpdateCatalogFirmwareService>();
        services.AddSingleton<IArtifactDownloadService, ArtifactDownloadService>();
        services.AddSingleton<IDriverPackStrategyResolver, DriverPackStrategyResolver>();
        services.AddSingleton<IDriverPackExtractionService, DriverPackExtractionService>();
        services.AddSingleton<IWindowsDeploymentService, WindowsDeploymentService>();
        services.AddSingleton<ISetupCompleteScriptService, SetupCompleteScriptService>();
        services.AddSingleton<IPreOobeScriptProvisioningService, PreOobeScriptProvisioningService>();
        services.AddSingleton<IAutopilotProfileCatalogService, AutopilotProfileCatalogService>();
        services.AddSingleton<IDeploymentStep, GatherDeploymentVariablesStep>();
        services.AddSingleton<IDeploymentStep, InitializeDeploymentWorkspaceStep>();
        services.AddSingleton<IDeploymentStep, ValidateTargetConfigurationStep>();
        services.AddSingleton<IDeploymentStep, ResolveCacheStrategyStep>();
        services.AddSingleton<IDeploymentStep, PrepareTargetDiskLayoutStep>();
        services.AddSingleton<IDeploymentStep, DownloadOperatingSystemImageStep>();
        services.AddSingleton<IDeploymentStep, ApplyOperatingSystemImageStep>();
        services.AddSingleton<IDeploymentStep, ConfigureTargetComputerNameStep>();
        services.AddSingleton<IDeploymentStep, ConfigureRecoveryEnvironmentStep>();
        services.AddSingleton<IDeploymentStep, DownloadDriverPackStep>();
        services.AddSingleton<IDeploymentStep, ExtractDriverPackStep>();
        services.AddSingleton<IDeploymentStep, ApplyDriverPackStep>();
        services.AddSingleton<IDeploymentStep, DownloadFirmwareUpdateStep>();
        services.AddSingleton<IDeploymentStep, ApplyFirmwareUpdateStep>();
        services.AddSingleton<IDeploymentStep, SealRecoveryPartitionStep>();
        services.AddSingleton<IDeploymentStep, StageAutopilotConfigurationStep>();
        services.AddSingleton<IDeploymentStep, FinalizeDeploymentAndWriteLogsStep>();
        services.AddSingleton<IDeploymentOrchestrator, DeploymentOrchestrator>();

        return services;
    }

    private static TelemetryOptions CreateTelemetryOptions(IServiceProvider serviceProvider)
    {
        TelemetrySettings settings = LoadTelemetrySettings(serviceProvider);
        return new TelemetryOptions(
            settings.IsEnabled,
            string.IsNullOrWhiteSpace(settings.HostUrl) ? TelemetryDefaults.PostHogEuHost : settings.HostUrl,
            string.IsNullOrWhiteSpace(settings.ProjectToken) ? TelemetryDefaults.ProjectToken : settings.ProjectToken,
            settings.InstallId);
    }

    private static TelemetryContext CreateTelemetryContext(IServiceProvider serviceProvider)
    {
        TelemetrySettings settings = LoadTelemetrySettings(serviceProvider);
        string runtime = WinPeRuntimeDetector.IsWinPeRuntime() ? TelemetryRuntimeModes.WinPe : TelemetryRuntimeModes.Desktop;
        return new TelemetryContext(
            TelemetryApps.FoundryDeploy,
            FoundryDeployApplicationInfo.Version,
            TelemetryBuildConfiguration.Current,
            runtime,
            string.IsNullOrWhiteSpace(settings.RuntimePayloadSource)
                ? TelemetryRuntimePayloadSources.Unknown
                : settings.RuntimePayloadSource,
            TelemetryBootMediaTargetResolver.Resolve(
                runtime,
                Environment.GetEnvironmentVariable(DeploymentModeEnvironmentVariable)),
            RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant(),
            CultureInfo.CurrentUICulture.Name,
            Guid.NewGuid().ToString("D"));
    }

    private static TelemetrySettings LoadTelemetrySettings(IServiceProvider serviceProvider)
    {
        return serviceProvider.GetRequiredService<IExpertDeployConfigurationService>()
            .LoadOptional()
            .Document
            ?.Telemetry ?? new TelemetrySettings();
    }
}
