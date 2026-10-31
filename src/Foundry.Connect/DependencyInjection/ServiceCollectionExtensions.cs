// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Diagnostics;
using System.Globalization;
using System.Net.Http;
using System.Resources;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;
using Foundry.Connect.Lifetime;
using Foundry.Wpf.Shell;
using Foundry.Connect.Configuration;
using Foundry.Connect.Network;
using Foundry.Connect.Network.Native;
using Foundry.Connect.Security;
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

    /// <summary>Request timeout of the telemetry HTTP client.</summary>
    internal static readonly TimeSpan TelemetryHttpTimeout = TimeSpan.FromSeconds(10);

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
        AddPageViewModels(services);
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
                CreateTelemetryHttpClient(),
                options,
                sp.GetRequiredService<TelemetryContext>(),
                logger);
        });
        services.AddSingleton<ILocalizationService>(_ => new ResourceLocalizationService(new ResourceManager("Foundry.Connect.Strings.Resources", typeof(App).Assembly)));
        services.AddSingleton<INetworkAdapterSnapshotProvider, WindowsNetworkAdapterSnapshotProvider>();
        services.AddSingleton<INetworkProfileRoamingService, NetworkProfileRoamingService>();
        AddNetworkServices(services);
        AddSecurityServices(services);
        services.AddSingleton<IThemeService, ThemeService>();
        services.AddSingleton<IKeyboardLayoutSwitcher>(_ => new WindowsKeyboardLayoutSwitcher(KeyboardLayoutHandoff.DefaultPath));

        return services;
    }

    /// <summary>
    /// Registers the single page and its sections as singletons with the window's lifetime. One
    /// <see cref="ConnectNetworkActions"/> is shared by the page and every section, so a debug scenario set on the page
    /// gates every network action and every action pauses the countdown.
    /// </summary>
    private static void AddPageViewModels(IServiceCollection services)
    {
        // Program creates the App before it resolves the window, so this is the UI dispatcher whatever the resolving
        // thread; without an Application (tests) it falls back to the resolving thread's dispatcher.
        services.AddSingleton<IUiDispatcher>(_ => new WpfUiDispatcher(Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher));
        services.AddSingleton<ConnectNetworkActions>();
        services.AddSingleton(sp => new AutoContinueViewModel(
            sp.GetRequiredService<ILocalizationService>(),
            sp.GetRequiredService<ISuccessfulExitService>(),
            sp.GetRequiredService<IApplicationLifetimeService>(),
            sp.GetRequiredService<TimeProvider>(),
            sp.GetRequiredService<IUiDispatcher>(),
            sp.GetRequiredService<ILogger<AutoContinueViewModel>>(),
            isAutoContinueEnabled: !Debugger.IsAttached));
        services.AddSingleton<EthernetSectionViewModel>();
        services.AddSingleton<ProvisionedWifiViewModel>();
        services.AddSingleton<WifiNetworksViewModel>();
        services.AddSingleton<DiagnosticsViewModel>();
        services.AddSingleton<ConnectPageViewModel>();
    }

    /// <summary>
    /// Registers the typed network stack. The coordinator is one singleton exposed as both
    /// <see cref="IConnectionCoordinator"/> and <see cref="IWifiConnectionHistory"/>, so roaming capture reads the
    /// connections this same coordinator made.
    /// </summary>
    private static void AddNetworkServices(IServiceCollection services)
    {
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton(sp => ConnectNetworkOptions.FromConfiguration(
            sp.GetRequiredService<FoundryConnectConfigurationDocument>(),
            Debugger.IsAttached));
        services.AddSingleton<NetworkRefreshSignal>();
        services.AddSingleton<IWlanClient, WlanClient>();
        services.AddSingleton<IConnectProcessRunner, ConnectProcessRunner>();
        services.AddSingleton<IWiredDot1xClient, WiredDot1xClient>();
        services.AddSingleton<ICertificateStore, LocalMachineCertificateStore>();
        services.AddSingleton<ICertificateInstaller, CertificateInstaller>();
        services.AddSingleton<IConnectivityProbe, ConnectivityProbe>();
        services.AddSingleton<IProvisioningStep, ProvisioningStep>();
        services.AddSingleton<ConnectionCoordinator>();
        services.AddSingleton<IConnectionCoordinator>(sp => sp.GetRequiredService<ConnectionCoordinator>());
        services.AddSingleton<IWifiConnectionHistory>(sp => sp.GetRequiredService<ConnectionCoordinator>());
        services.AddSingleton<INetworkObservationSource, NetworkObservationSource>();
        services.AddSingleton<INetworkMonitor, NetworkMonitor>();
        services.AddSingleton<IRoamingCapture, RoamingCapture>();
        services.AddSingleton<IRuntimePreferencesPublisher, RuntimePreferencesPublisher>();
        services.AddSingleton<ISuccessfulExitService, SuccessfulExitService>();
    }

    /// <summary>
    /// Registers the deployment password unlock. The hand-off sender reads and removes its pipe name when it is
    /// constructed with the main window graph, before provisioning starts any child process.
    /// </summary>
    private static void AddSecurityServices(IServiceCollection services)
    {
        services.AddSingleton<IDeploymentPasswordPrompt, DeploymentPasswordPrompt>();
        services.AddSingleton<IDeploymentKeyUnwrapper, DeploymentKeyUnwrapper>();
        services.AddSingleton<IDeploymentKeyHandoffSender, EnvironmentDeploymentKeyHandoffSender>();
        services.AddSingleton<IConnectUnlockService, ConnectUnlockService>();
    }

    /// <summary>
    /// Creates the telemetry <see cref="HttpClient"/> with an explicit timeout, shorter than the 100 seconds default, so a
    /// stalled send cannot linger. The exit path stops waiting after its own, shorter budget.
    /// </summary>
    internal static HttpClient CreateTelemetryHttpClient() => new() { Timeout = TelemetryHttpTimeout };

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
