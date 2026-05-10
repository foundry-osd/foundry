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

    void SetTheme(ThemeMode theme);
}
