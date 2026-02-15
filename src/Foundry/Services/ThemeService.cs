using System.ComponentModel;
using System.Windows;
using Foundry.Services;

namespace Foundry.Services;

public sealed class ThemeService : IThemeService, INotifyPropertyChanged
{
    private ThemeMode _currentTheme;
    private ResourceDictionary? _currentThemeDictionary;

    public ThemeMode CurrentTheme
    {
        get => _currentTheme;
        private set
        {
            if (_currentTheme != value)
            {
                _currentTheme = value;
                ApplyTheme(value);
                OnPropertyChanged(nameof(CurrentTheme));
            }
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

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

    private void ApplyTheme(ThemeMode theme)
    {
        var app = Application.Current;
        if (app == null)
        {
            return;
        }

        var resources = app.Resources.MergedDictionaries;
        resources.Clear();

        _currentThemeDictionary = theme switch
        {
            ThemeMode.Light => new ResourceDictionary { Source = new Uri("pack://application:,,,/PresentationFramework.Fluent;component/Themes/Fluent.Light.xaml", UriKind.Absolute) },
            ThemeMode.Dark => new ResourceDictionary { Source = new Uri("pack://application:,,,/PresentationFramework.Fluent;component/Themes/Fluent.Dark.xaml", UriKind.Absolute) },
            ThemeMode.System => new ResourceDictionary { Source = new Uri("pack://application:,,,/PresentationFramework.Fluent;component/Themes/Fluent.xaml", UriKind.Absolute) },
            _ => new ResourceDictionary { Source = new Uri("pack://application:,,,/PresentationFramework.Fluent;component/Themes/Fluent.xaml", UriKind.Absolute) }
        };

        resources.Add(_currentThemeDictionary);
    }
}
