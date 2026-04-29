using System.ComponentModel;

namespace Foundry.Services.Theme;

public sealed class ThemeService : IThemeService, INotifyPropertyChanged
{
    private ThemeMode _currentTheme;

    public ThemeMode CurrentTheme
    {
        get => _currentTheme;
        private set
        {
            if (_currentTheme != value)
            {
                _currentTheme = value;
                ThemeChanged?.Invoke(this, value);
                OnPropertyChanged(nameof(CurrentTheme));
            }
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public event EventHandler<ThemeMode>? ThemeChanged;

    private void OnPropertyChanged(string propertyName)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    public ThemeService()
    {
        CurrentTheme = ThemeMode.System;
    }

    public void SetTheme(ThemeMode theme)
    {
        CurrentTheme = theme;
    }
}
