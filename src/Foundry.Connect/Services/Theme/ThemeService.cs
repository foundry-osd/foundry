using System.ComponentModel;
using System.Windows;

namespace Foundry.Connect.Services.Theme;

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
                ApplyTheme(value);
                OnPropertyChanged(nameof(CurrentTheme));
            }
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ThemeService()
    {
        CurrentTheme = ThemeMode.System;
    }

    public void SetTheme(ThemeMode theme)
    {
        CurrentTheme = theme;
    }

    private static void ApplyTheme(ThemeMode theme)
    {
        Application? app = Application.Current;
        if (app is null)
        {
            return;
        }

        ResourceDictionary resources = app.Resources;
        resources.MergedDictionaries.Clear();

        ResourceDictionary dictionary = theme switch
        {
            ThemeMode.Light => new ResourceDictionary { Source = new Uri("pack://application:,,,/PresentationFramework.Fluent;component/Themes/Fluent.Light.xaml", UriKind.Absolute) },
            ThemeMode.Dark => new ResourceDictionary { Source = new Uri("pack://application:,,,/PresentationFramework.Fluent;component/Themes/Fluent.Dark.xaml", UriKind.Absolute) },
            _ => new ResourceDictionary { Source = new Uri("pack://application:,,,/PresentationFramework.Fluent;component/Themes/Fluent.xaml", UriKind.Absolute) }
        };

        resources.MergedDictionaries.Add(dictionary);
    }

    private void OnPropertyChanged(string propertyName)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
