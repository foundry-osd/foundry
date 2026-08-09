// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Avalonia;
using Avalonia.Styling;

namespace Foundry.Avalonia.Services.Theme;

public sealed class AvaloniaFoundryThemeService : IFoundryThemeService
{
    public FoundryThemeMode CurrentTheme { get; private set; } = FoundryThemeMode.System;

    public void SetTheme(FoundryThemeMode theme)
    {
        Application application = Application.Current
            ?? throw new InvalidOperationException("Avalonia application is not initialized.");

        application.RequestedThemeVariant = theme switch
        {
            FoundryThemeMode.Light => ThemeVariant.Light,
            FoundryThemeMode.Dark => ThemeVariant.Dark,
            _ => ThemeVariant.Default,
        };
        CurrentTheme = theme;
    }
}
