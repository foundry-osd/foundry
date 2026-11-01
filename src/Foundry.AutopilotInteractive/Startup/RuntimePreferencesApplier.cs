// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Globalization;
using Foundry.Core.Models.Autopilot;
using Foundry.Core.Services.Runtime;
using Foundry.Wpf.Localization;
using Foundry.Wpf.Theme;
using Microsoft.Extensions.Logging;

namespace Foundry.AutopilotInteractive.Startup;

/// <summary>
/// Applies the theme and culture carried over from the previous runtime application.
/// </summary>
internal static class RuntimePreferencesApplier
{
    /// <summary>
    /// Applies the configured theme and culture. A culture name the operating system does not know is logged and ignored, so
    /// the application starts in the default language instead of failing.
    /// </summary>
    /// <remarks>The application resources must be loaded first: the theme service swaps the Fluent dictionary in them.</remarks>
    /// <param name="configuration">Runtime configuration.</param>
    /// <param name="themeService">Applies the theme.</param>
    /// <param name="localizationService">Applies the culture.</param>
    /// <param name="logger">Receives the warning for an unknown culture.</param>
    public static void Apply(
        AutopilotInteractiveRuntimeConfiguration configuration,
        IThemeService themeService,
        ILocalizationService localizationService,
        ILogger logger)
    {
        themeService.SetTheme(configuration.Theme switch
        {
            RuntimeThemePreference.Light => ThemeMode.Light,
            RuntimeThemePreference.Dark => ThemeMode.Dark,
            _ => ThemeMode.System
        });

        try
        {
            localizationService.SetCulture(CultureInfo.GetCultureInfo(configuration.CultureName));
        }
        catch (CultureNotFoundException)
        {
            logger.LogWarning("The configured culture is not available and is ignored. CultureName={CultureName}", configuration.CultureName);
        }
    }
}
