// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Services.Settings;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace Foundry.Services.Appearance;

internal sealed class AppThemeService(IAppSettingsService settingsService) : IAppThemeService
{
    private Window? window;
    private FrameworkElement? rootElement;

    public ElementTheme ElementTheme { get; private set; } = ParseElementTheme(settingsService.Current.Appearance.ElementTheme);

    public AppBackdropKind Backdrop { get; private set; } = ParseBackdrop(settingsService.Current.Appearance.BackdropType);

    public void Initialize(Window window, FrameworkElement rootElement)
    {
        this.window = window;
        this.rootElement = rootElement;
        rootElement.RequestedTheme = ElementTheme;
        window.SystemBackdrop = CreateBackdrop(Backdrop);
    }

    public void SetElementTheme(ElementTheme theme)
    {
        FrameworkElement target = rootElement ?? throw new InvalidOperationException("The theme service is not initialized.");
        target.RequestedTheme = theme;
        ElementTheme = theme;
        settingsService.Current.Appearance.ElementTheme = theme.ToString();
        settingsService.Save();
    }

    public void SetBackdrop(AppBackdropKind backdrop)
    {
        Window target = window ?? throw new InvalidOperationException("The theme service is not initialized.");
        target.SystemBackdrop = CreateBackdrop(backdrop);
        Backdrop = backdrop;
        settingsService.Current.Appearance.BackdropType = backdrop.ToString();
        settingsService.Save();
    }

    private static ElementTheme ParseElementTheme(string? value) =>
        Enum.TryParse(value, ignoreCase: true, out ElementTheme result) ? result : ElementTheme.Default;

    private static AppBackdropKind ParseBackdrop(string? value) =>
        Enum.TryParse(value, ignoreCase: true, out AppBackdropKind result) ? result : AppBackdropKind.Mica;

    private static SystemBackdrop CreateBackdrop(AppBackdropKind backdrop) => backdrop switch
    {
        AppBackdropKind.MicaAlt => new MicaBackdrop { Kind = MicaKind.BaseAlt },
        AppBackdropKind.Acrylic or AppBackdropKind.AcrylicThin => new DesktopAcrylicBackdrop(),
        _ => new MicaBackdrop { Kind = MicaKind.Base }
    };
}
