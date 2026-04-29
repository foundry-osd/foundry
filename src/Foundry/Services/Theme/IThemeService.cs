namespace Foundry.Services.Theme;

public enum ThemeMode
{
    System,
    Light,
    Dark
}

public interface IThemeService
{
    ThemeMode CurrentTheme { get; }

    event EventHandler<ThemeMode>? ThemeChanged;

    void SetTheme(ThemeMode theme);
}
