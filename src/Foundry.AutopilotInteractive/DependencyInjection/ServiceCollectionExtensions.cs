// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Net.Http;
using System.Resources;
using System.Windows;
using System.Windows.Threading;
using Foundry.AutopilotInteractive.Services;
using Foundry.AutopilotInteractive.Services.Authentication;
using Foundry.AutopilotInteractive.Services.Device;
using Foundry.AutopilotInteractive.Services.Registration;
using Foundry.AutopilotInteractive.Services.Simulation;
using Foundry.AutopilotInteractive.Startup;
using Foundry.AutopilotInteractive.ViewModels;
using Foundry.Core.Models.Autopilot;
using Foundry.Core.Services.Autopilot;
using Foundry.Wpf.Localization;
using Foundry.Wpf.Runtime;
using Foundry.Wpf.Shell;
using Foundry.Wpf.Theme;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Foundry.AutopilotInteractive.DependencyInjection;

/// <summary>
/// Composition root of Foundry.AutopilotInteractive.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>Base name of the embedded string resources, shared by the DI container and the startup error dialog.</summary>
    internal const string ResourceBaseName = "Foundry.AutopilotInteractive.Strings.Resources";

    private static readonly TimeSpan DeviceCodeRequestTimeout = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Registers the application, its window and view models, and the services they use.
    /// </summary>
    /// <remarks>
    /// In debug safety mode only the simulated services are registered (<see cref="SimulatedAutopilotServiceSetFactory"/>):
    /// the real device code client, registration service and Graph client are not even in the container, so a debug session
    /// can never reach Microsoft Entra ID or Microsoft Graph or read the development machine's hardware hash. Nothing talks to
    /// telemetry: the application uses local file logging only.
    /// </remarks>
    /// <param name="services">The service collection to update.</param>
    /// <param name="commandLine">Arguments of the process.</param>
    /// <param name="configuration">Validated runtime configuration.</param>
    /// <returns>The same service collection.</returns>
    public static IServiceCollection AddFoundryAutopilotInteractiveApplicationServices(
        this IServiceCollection services,
        AutopilotInteractiveCommandLine commandLine,
        AutopilotInteractiveRuntimeConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(commandLine);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddSingleton(commandLine);
        services.AddSingleton(configuration);
        services.AddSingleton(TimeProvider.System);

        services.AddSingleton<App>();
        services.AddSingleton<MainWindow>();
        services.AddSingleton<MainWindowViewModel>();
        services.AddSingleton<SignInViewModel>();
        services.AddSingleton<GroupTagViewModel>();
        services.AddSingleton<IUiDispatcher>(_ => new WpfUiDispatcher(Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher));
        services.AddSingleton<ISkipConfirmation, DialogSkipConfirmation>();
        services.AddSingleton(_ => new RegistrationResultStore(RegistrationResultStore.ResolveStateDirectory(DebugSafetyMode.IsEnabled)));

        services.AddSingleton(WpfApplicationIdentity.FromAssembly(typeof(App).Assembly));
        services.AddSingleton<IApplicationShellService, ApplicationShellService>();
        services.AddSingleton<ILocalizationService>(_ => CreateLocalizationService());
        services.AddSingleton<IThemeService, ThemeService>();
        services.AddSingleton<IDeviceInformationReader, DeviceInformationReader>();

        if (DebugSafetyMode.IsEnabled)
        {
            services.AddSingleton<IAutopilotServiceSetFactory, SimulatedAutopilotServiceSetFactory>();
        }
        else
        {
            AddLiveAutopilotServices(services);
        }

        return services;
    }

    // The real services: Microsoft Entra ID device code endpoints, the shared Graph import client and the WMI hardware reader.
    private static void AddLiveAutopilotServices(IServiceCollection services)
    {
        services.AddSingleton<IHardwareIdentityReader, HardwareIdentityReader>();
        services.AddSingleton<IDeviceCodeClient>(sp => new DeviceCodeClient(
            new HttpClient { Timeout = DeviceCodeRequestTimeout },
            sp.GetRequiredService<TimeProvider>()));
        services.AddSingleton(sp => new AutopilotGraphImportClient(
            new HttpClient { BaseAddress = new Uri("https://graph.microsoft.com/", UriKind.Absolute) },
            sp.GetRequiredService<ILogger<AutopilotGraphImportClient>>(),
            RegistrationService.GraphClientOptions));
        services.AddSingleton<IRegistrationService, RegistrationService>();
        services.AddSingleton<IProfileAssignmentService>(sp =>
        {
            TimeProvider timeProvider = sp.GetRequiredService<TimeProvider>();
            return new ProfileAssignmentWaiter(
                sp.GetRequiredService<AutopilotGraphImportClient>(),
                (duration, ct) => Task.Delay(duration, timeProvider, ct),
                timeProvider.GetUtcNow,
                sp.GetRequiredService<ILogger<ProfileAssignmentWaiter>>());
        });
        services.AddSingleton<IAutopilotServiceSetFactory, LiveAutopilotServiceSetFactory>();
    }

    /// <summary>Creates the localization service over the application's embedded string resources.</summary>
    /// <returns>A service starting from the current UI culture.</returns>
    internal static ResourceLocalizationService CreateLocalizationService()
    {
        return new ResourceLocalizationService(new ResourceManager(ResourceBaseName, typeof(App).Assembly));
    }
}
