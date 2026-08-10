// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Windows;
using Foundry.Avalonia.Services.Theme;

namespace Foundry.Connect.Platform;

internal sealed class WpfFoundryThemeService : IFoundryThemeService
{
    public FoundryThemeMode CurrentTheme { get; private set; }

    public void SetTheme(FoundryThemeMode theme)
    {
        CurrentTheme = theme;

        Application? app = Application.Current;
        if (app is null)
        {
            return;
        }

        ResourceDictionary dictionary = theme switch
        {
            FoundryThemeMode.Light => CreateDictionary("Fluent.Light.xaml"),
            FoundryThemeMode.Dark => CreateDictionary("Fluent.Dark.xaml"),
            _ => CreateDictionary("Fluent.xaml")
        };

        app.Resources.MergedDictionaries.Clear();
        app.Resources.MergedDictionaries.Add(dictionary);
    }

    private static ResourceDictionary CreateDictionary(string fileName)
    {
        return new ResourceDictionary
        {
            Source = new Uri($"pack://application:,,,/PresentationFramework.Fluent;component/Themes/{fileName}", UriKind.Absolute)
        };
    }
}
