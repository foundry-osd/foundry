// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Wpf.Theme;

/// <summary>
/// Theme choices offered by the WPF runtime applications.
/// </summary>
public enum ThemeMode
{
    /// <summary>Follows the Windows theme.</summary>
    System,

    /// <summary>Always uses the light theme.</summary>
    Light,

    /// <summary>Always uses the dark theme.</summary>
    Dark
}

/// <summary>
/// Switches the application between the Fluent system, light and dark themes.
/// </summary>
public interface IThemeService
{
    /// <summary>
    /// Gets the theme currently applied.
    /// </summary>
    ThemeMode CurrentTheme { get; }

    /// <summary>
    /// Applies the requested theme to the running application.
    /// </summary>
    /// <param name="theme">Theme to apply.</param>
    void SetTheme(ThemeMode theme);
}
