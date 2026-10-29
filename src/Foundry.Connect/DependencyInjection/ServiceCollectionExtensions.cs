// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Globalization;
using System.Net.Http;
using System.Resources;
using System.Runtime.InteropServices;
using System.Windows;
using Foundry.Connect.Lifetime;
using Foundry.Wpf.Shell;
using Foundry.Connect.Configuration;
using Foundry.Connect.Services.Network;
using Foundry.Connect.Services.Runtime;
using Foundry.Wpf.Theme;
using Foundry.Connect.ViewModels;
using Foundry.Core.Models.Configuration;
using Foundry.Telemetry;
using Foundry.Utilities.Globalization;
using Foundry.Utilities.Networking;
using Foundry.Utilities.Runtime;
using Foundry.Wpf.Localization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Foundry.Connect.DependencyInjection;

public static class ServiceCollectionExtensions
{
    private const string DeploymentModeEnvironmentVariable = "FOUNDRY_DEPLOYMENT_MODE";

    /// <summary>
    /// Registers the Foundry.Connect application, UI, lifetime, telemetry and network services.
    /// </summary>
    /// <remarks>The caller pre-registers <see cref="IConnectConfigurationService"/>, the loaded <see cref="FoundryConnectConfigurationDocument"/> and its <see cref="ConnectRuntimeSecrets"/>.</remarks>
    /// <param name="services">The service collection to update.</param>
    /// <param name="args">The command-line arguments of the process.</param>
    /// <returns>The same service collection.</returns>
    public static IServiceCollection AddFoundryConnectApplicationServices(this IServiceCollection services, string[] args)
    {
        services.AddSingleton<App>();
        services.AddSingleton<MainWindow>();
        services.AddSingleton<MainWindowViewModel>();
        services.AddSingleton(args ?? Array.Empty<string>());

        services.AddSingleton<IApplicationShellService, ApplicationShellService>();
        services.AddSingleton(WpfApplicationIdentity.FromAssembly(typeof(App).Assembly));
        services.AddSingleton<IApplicationLifetimeService>(sp => new ApplicationLifetimeService(
            ShutdownApplication, sp.GetRequiredService<ILogger<ApplicationLifetimeService>>()));
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
        services.AddSingleton<ILocalizationService>(_ => new ResourceLocalizationService(new ResourceManager("Foundry.Connect.Strings.Resources", typeof(App).Assembly)));
        services.AddSingleton<INetworkAdapterSnapshotProvider, WindowsNetworkAdapterSnapshotProvider>();
        services.AddSingleton<INetworkProfileRoamingService, NetworkProfileRoamingService>();
        services.AddSingleton<INetworkBootstrapService, NetworkBootstrapService>();
        services.AddSingleton<INetworkStatusService, NetworkStatusService>();
        services.AddSingleton<IThemeService, ThemeService>();
        services.AddSingleton<IKeyboardLayoutSwitcher>(_ => new WindowsKeyboardLayoutSwitcher(KeyboardLayoutHandoff.DefaultPath));

        return services;
    }

    /// <summary>Shuts the WPF application down on its UI thread; exit requests may come from background work.</summary>
    private static void ShutdownApplication(int exitCode)
    {
        Application? app = Application.Current;
        if (app is null)
        {
            return;
        }

        if (app.Dispatcher.CheckAccess())
        {
            app.Shutdown(exitCode);
            return;
        }

        _ = app.Dispatcher.InvokeAsync(() => app.Shutdown(exitCode));
    }

    private static TelemetryOptions CreateTelemetryOptions(IServiceProvider serviceProvider)
    {
        TelemetrySettings settings = serviceProvider.GetRequiredService<FoundryConnectConfigurationDocument>().Telemetry;
        return new TelemetryOptions(
            settings.IsEnabled,
            string.IsNullOrWhiteSpace(settings.HostUrl) ? TelemetryDefaults.PostHogEuHost : settings.HostUrl,
            string.IsNullOrWhiteSpace(settings.ProjectToken) ? TelemetryDefaults.ProjectToken : settings.ProjectToken,
            settings.InstallId);
    }

    private static TelemetryContext CreateTelemetryContext(IServiceProvider serviceProvider)
    {
        FoundryConnectConfigurationDocument configuration = serviceProvider.GetRequiredService<FoundryConnectConfigurationDocument>();
        string runtime = WinPeRuntimeDetector.IsWinPeRuntime() ? TelemetryRuntimeModes.WinPe : TelemetryRuntimeModes.Desktop;
        return TelemetryContextFactory.Create(
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
            CultureInfo.CurrentUICulture.Name);
    }
}
