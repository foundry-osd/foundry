// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Resources;
using Foundry.AutopilotInteractive.Services.Device;
using Foundry.AutopilotInteractive.Startup;
using Foundry.AutopilotInteractive.ViewModels;
using Foundry.Core.Models.Autopilot;
using Foundry.Wpf.Localization;
using Foundry.Wpf.Shell;
using Foundry.Wpf.Theme;
using Microsoft.Extensions.DependencyInjection;

namespace Foundry.AutopilotInteractive.DependencyInjection;

/// <summary>
/// Composition root of Foundry.AutopilotInteractive.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>Base name of the embedded string resources, shared by the DI container and the startup error dialog.</summary>
    internal const string ResourceBaseName = "Foundry.AutopilotInteractive.Strings.Resources";

    /// <summary>
    /// Registers the application, its window and view models, and the services they use.
    /// </summary>
    /// <remarks>
    /// Pages and their services are added here by later changes. Nothing registered here talks to the network or to
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

        services.AddSingleton(WpfApplicationIdentity.FromAssembly(typeof(App).Assembly));
        services.AddSingleton<IApplicationShellService, ApplicationShellService>();
        services.AddSingleton<ILocalizationService>(_ => CreateLocalizationService());
        services.AddSingleton<IThemeService, ThemeService>();
        services.AddSingleton<IDeviceInformationReader, DeviceInformationReader>();

        return services;
    }

    /// <summary>Creates the localization service over the application's embedded string resources.</summary>
    /// <returns>A service starting from the current UI culture.</returns>
    internal static ResourceLocalizationService CreateLocalizationService()
    {
        return new ResourceLocalizationService(new ResourceManager(ResourceBaseName, typeof(App).Assembly));
    }
}
