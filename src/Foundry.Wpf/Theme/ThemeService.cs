// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;

namespace Foundry.Wpf.Theme;

/// <summary>
/// Applies the Fluent theme dictionary to the running WPF application and notifies when the theme changes.
/// </summary>
public sealed class ThemeService : IThemeService, INotifyPropertyChanged
{
    private ThemeMode _currentTheme;

    /// <inheritdoc />
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

    /// <inheritdoc />
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// Initializes a new instance of the <see cref="ThemeService" /> class using the system theme.
    /// </summary>
    public ThemeService()
    {
        CurrentTheme = ThemeMode.System;
    }

    /// <inheritdoc />
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

        ResourceDictionary dictionary = theme switch
        {
            ThemeMode.Light => new ResourceDictionary { Source = new Uri("pack://application:,,,/PresentationFramework.Fluent;component/Themes/Fluent.Light.xaml", UriKind.Absolute) },
            ThemeMode.Dark => new ResourceDictionary { Source = new Uri("pack://application:,,,/PresentationFramework.Fluent;component/Themes/Fluent.Dark.xaml", UriKind.Absolute) },
            _ => new ResourceDictionary { Source = new Uri("pack://application:,,,/PresentationFramework.Fluent;component/Themes/Fluent.xaml", UriKind.Absolute) }
        };

        ApplyFluentDictionary(app.Resources.MergedDictionaries, dictionary);
    }

    /// <summary>
    /// Installs the Fluent dictionary as the first merged dictionary, leaving every other merged
    /// dictionary (shared styles registered after it) untouched.
    /// </summary>
    /// <param name="mergedDictionaries">Merged dictionaries of the application resources.</param>
    /// <param name="fluent">Fluent dictionary matching the requested theme.</param>
    internal static void ApplyFluentDictionary(Collection<ResourceDictionary> mergedDictionaries, ResourceDictionary fluent)
    {
        if (mergedDictionaries.Count == 0)
        {
            mergedDictionaries.Add(fluent);
            return;
        }

        mergedDictionaries[0] = fluent;
    }

    private void OnPropertyChanged(string propertyName)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
