using Foundry.Deploy.Services.ApplicationShell;
using Foundry.Deploy.Services.Autopilot;
using Foundry.Deploy.Services.Cache;
using Foundry.Deploy.Services.Catalog;
using Foundry.Deploy.Services.Configuration;
using Foundry.Deploy.Services.Deployment;
using Foundry.Deploy.Services.Deployment.Steps;
using Foundry.Deploy.Services.Download;
using Foundry.Deploy.Services.DriverPacks;
using Foundry.Deploy.Services.Hardware;
using Foundry.Deploy.Services.Logging;
using Foundry.Deploy.Services.Operations;
using Foundry.Deploy.Services.Runtime;
using Foundry.Deploy.Services.System;
using Foundry.Deploy.Services.Theme;
using Foundry.Deploy.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace Foundry.Deploy.DependencyInjection;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddFoundryDeployApplicationServices(this IServiceCollection services)
    {
        services.AddSingleton<App>();
        services.AddSingleton<MainWindow>();
        services.AddSingleton<MainWindowViewModel>();

        services.AddSingleton<IThemeService, ThemeService>();
        services.AddSingleton<IApplicationShellService, ApplicationShellService>();
        services.AddSingleton<IOperationProgressService, OperationProgressService>();
        services.AddSingleton<IDeploymentRuntimeContextService, DeploymentRuntimeContextService>();
        services.AddSingleton<IExpertDeployConfigurationService, ExpertDeployConfigurationService>();
        services.AddSingleton<IDeploymentLaunchPreparationService, DeploymentLaunchPreparationService>();
        services.AddSingleton<IProcessRunner, ProcessRunner>();
        services.AddSingleton<IArchiveExtractionService, ArchiveExtractionService>();
        services.AddSingleton<ICacheLocatorService, CacheLocatorService>();
        services.AddSingleton<IDeploymentLogService, DeploymentLogService>();
        services.AddSingleton<IHardwareProfileService, HardwareProfileService>();
        services.AddSingleton<IOfflineWindowsComputerNameService, OfflineWindowsComputerNameService>();
        services.AddSingleton<ITargetDiskService, TargetDiskService>();
        services.AddSingleton<IOperatingSystemCatalogService, OperatingSystemCatalogService>();
        services.AddSingleton<IDriverPackCatalogService, DriverPackCatalogService>();
        services.AddSingleton<IDriverPackSelectionService, DriverPackSelectionService>();
        services.AddSingleton<IMicrosoftUpdateCatalogClient, MicrosoftUpdateCatalogClient>();
        services.AddSingleton<IMicrosoftUpdateCatalogDriverService, MicrosoftUpdateCatalogDriverService>();
        services.AddSingleton<IMicrosoftUpdateCatalogFirmwareService, MicrosoftUpdateCatalogFirmwareService>();
        services.AddSingleton<IArtifactDownloadService, ArtifactDownloadService>();
        services.AddSingleton<IDriverPackStrategyResolver, DriverPackStrategyResolver>();
        services.AddSingleton<IDriverPackExtractionService, DriverPackExtractionService>();
        services.AddSingleton<IWindowsDeploymentService, WindowsDeploymentService>();
        services.AddSingleton<ISetupCompleteScriptService, SetupCompleteScriptService>();
        services.AddSingleton<IAutopilotService, AutopilotService>();
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
        services.AddSingleton<IDeploymentStep, ExecuteFullAutopilotWorkflowStep>();
        services.AddSingleton<IDeploymentStep, FinalizeDeploymentAndWriteLogsStep>();
        services.AddSingleton<IDeploymentOrchestrator, DeploymentOrchestrator>();

        return services;
    }
}
