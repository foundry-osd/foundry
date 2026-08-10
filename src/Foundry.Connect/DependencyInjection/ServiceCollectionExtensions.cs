// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Globalization;
using System.Net.Http;
using System.Runtime.InteropServices;
using Foundry.Avalonia.Services.Theme;
using Foundry.Avalonia.Services.Threading;
using Foundry.Connect.Models.Configuration;
using Foundry.Connect.Services.ApplicationLifetime;
using Foundry.Connect.Services.Configuration;
using Foundry.Connect.Services.Diagnostics;
using Foundry.Connect.Services.Localization;
using Foundry.Connect.Services.Network;
using Foundry.Connect.Services.Readiness;
using Foundry.Connect.Services.Runtime;
using Foundry.Connect.Platform;
using Foundry.Connect.ViewModels;
using Foundry.Telemetry;
using Foundry.Utilities.Networking;
using Foundry.Utilities.Runtime;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Foundry.Connect.DependencyInjection;

public static class ServiceCollectionExtensions
{
    private const string DeploymentModeEnvironmentVariable = "FOUNDRY_DEPLOYMENT_MODE";

    public static IServiceCollection AddFoundryConnectApplicationServices(this IServiceCollection services, string[] args)
    {
        services.AddSingleton<MainWindow>();
        services.AddSingleton<MainWindowViewModel>();
        services.AddSingleton(args ?? Array.Empty<string>());

        services.AddSingleton<IUiDispatcher, AvaloniaUiDispatcher>();
        services.AddSingleton<IUiTimerFactory, AvaloniaUiTimerFactory>();
        services.AddSingleton<IFoundryThemeService, AvaloniaFoundryThemeService>();
        services.AddSingleton<IApplicationExitHandler, AvaloniaApplicationExitHandler>();
        services.AddSingleton<IApplicationLifetimeService, ApplicationLifetimeService>();
        services.AddSingleton<IConnectConfigurationService, ConnectConfigurationService>();
        services.AddSingleton(sp => sp.GetRequiredService<IConnectConfigurationService>().Load());
        services.AddSingleton(CreateTelemetryOptions);
        services.AddSingleton(CreateTelemetryContext);
        services.AddSingleton<ITelemetryService>(sp =>
        {
            TelemetryOptions options = sp.GetRequiredService<TelemetryOptions>();
            ILogger<PostHogTelemetryService> logger = sp.GetRequiredService<ILogger<PostHogTelemetryService>>();
            logger.LogDebug(
                "Configuring telemetry service. App={App}, IsEnabled={IsEnabled}, HasProjectToken={HasProjectToken}, HasInstallId={HasInstallId}, HostUrl={HostUrl}.",
                TelemetryApps.FoundryConnect,
                options.IsEnabled,
                !string.IsNullOrWhiteSpace(options.ProjectToken),
                !string.IsNullOrWhiteSpace(options.InstallId),
                options.HostUrl);

            if (!options.CanSend)
            {
                logger.LogDebug("Telemetry service disabled for Foundry.Connect because runtime options are incomplete or disabled.");
                return new NullTelemetryService();
            }

            return new PostHogTelemetryService(
                new HttpClient(),
                options,
                sp.GetRequiredService<TelemetryContext>(),
                logger);
        });
        services.AddSingleton<ILocalizationService, LocalizationService>();
        services.AddSingleton<INetworkAdapterSnapshotProvider, WindowsNetworkAdapterSnapshotProvider>();
        services.AddSingleton<INetworkProfileRoamingService, NetworkProfileRoamingService>();
        services.AddSingleton<INetworkBootstrapService, NetworkBootstrapService>();
        services.AddSingleton<INetworkStatusService, NetworkStatusService>();
        services.AddSingleton<IConnectReadinessEvaluator, ConnectReadinessEvaluator>();
        services.AddSingleton<IConnectDiagnosticsSnapshotProvider, ConnectDiagnosticsSnapshotProvider>();
        return services;
    }

    private static TelemetryOptions CreateTelemetryOptions(IServiceProvider serviceProvider)
    {
        TelemetrySettings settings = serviceProvider.GetRequiredService<FoundryConnectConfiguration>().Telemetry;
        return new TelemetryOptions(
            settings.IsEnabled,
            string.IsNullOrWhiteSpace(settings.HostUrl) ? TelemetryDefaults.PostHogEuHost : settings.HostUrl,
            string.IsNullOrWhiteSpace(settings.ProjectToken) ? TelemetryDefaults.ProjectToken : settings.ProjectToken,
            settings.InstallId);
    }

    private static TelemetryContext CreateTelemetryContext(IServiceProvider serviceProvider)
    {
        FoundryConnectConfiguration configuration = serviceProvider.GetRequiredService<FoundryConnectConfiguration>();
        string runtime = WinPeRuntimeDetector.IsWinPeRuntime() ? TelemetryRuntimeModes.WinPe : TelemetryRuntimeModes.Desktop;
        return new TelemetryContext(
            TelemetryApps.FoundryConnect,
            FoundryConnectApplicationInfo.Version,
            TelemetryBuildConfiguration.Current,
            runtime,
            string.IsNullOrWhiteSpace(configuration.Telemetry.RuntimePayloadSource)
                ? TelemetryRuntimePayloadSources.Unknown
                : configuration.Telemetry.RuntimePayloadSource,
            TelemetryBootMediaTargetResolver.Resolve(
                runtime,
                Environment.GetEnvironmentVariable(DeploymentModeEnvironmentVariable)),
            RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant(),
            CultureInfo.CurrentUICulture.Name,
            Guid.NewGuid().ToString("D"));
    }
}
