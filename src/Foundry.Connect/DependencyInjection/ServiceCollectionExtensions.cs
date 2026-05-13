using System.Globalization;
using System.Net.Http;
using System.Runtime.InteropServices;
using Foundry.Connect.Models.Configuration;
using Foundry.Connect.Services.ApplicationLifetime;
using Foundry.Connect.Services.ApplicationShell;
using Foundry.Connect.Services.Configuration;
using Foundry.Connect.Services.Localization;
using Foundry.Connect.Services.Network;
using Foundry.Connect.Services.Runtime;
using Foundry.Connect.Services.Theme;
using Foundry.Connect.ViewModels;
using Foundry.Telemetry;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

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
        services.AddSingleton(CreateTelemetryOptions);
        services.AddSingleton(CreateTelemetryContext);
        services.AddSingleton<ITelemetryService>(sp =>
        {
            TelemetryOptions options = sp.GetRequiredService<TelemetryOptions>();
            ILogger<PostHogTelemetryService> logger = sp.GetRequiredService<ILogger<PostHogTelemetryService>>();
            logger.LogDebug(
                "Configuring telemetry service. App={App}, IsEnabled={IsEnabled}, HasProjectToken={HasProjectToken}, HasInstallId={HasInstallId}, HostUrl={HostUrl}.",
                "foundry-connect",
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
        services.AddSingleton<INetworkBootstrapService, NetworkBootstrapService>();
        services.AddSingleton<INetworkStatusService, NetworkStatusService>();
        services.AddSingleton<IThemeService, ThemeService>();

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
        return new TelemetryContext(
            "foundry-connect",
            FoundryConnectApplicationInfo.Version,
            TelemetryBuildConfiguration.Current,
            ConnectWorkspacePaths.IsWinPeRuntime() ? TelemetryRuntimeModes.WinPe : TelemetryRuntimeModes.Desktop,
            string.IsNullOrWhiteSpace(configuration.Telemetry.RuntimePayloadSource)
                ? TelemetryRuntimePayloadSources.Unknown
                : configuration.Telemetry.RuntimePayloadSource,
            RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant(),
            CultureInfo.CurrentUICulture.Name,
            Guid.NewGuid().ToString("D"));
    }
}
