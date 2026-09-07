// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Markup;

namespace Foundry.Connect.Services.Localization;

/// <summary>Applies selected culture to WPF roots and follows explicit owners for detached dialogs.</summary>
internal static class LocalizationRoot
{
    public static void Apply(FrameworkElement element, CultureInfo culture)
    {
        element.Language = XmlLanguage.GetLanguage(culture.IetfLanguageTag);
        element.FlowDirection = culture.TextInfo.IsRightToLeft ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
    }

    public static void BindToOwner(Window dialog)
    {
        Apply(dialog, CultureInfo.CurrentUICulture);
        dialog.Loaded += OnLoaded;
        static void OnLoaded(object sender, RoutedEventArgs args)
        {
            var window = (Window)sender;
            window.Loaded -= OnLoaded;
            Window? owner = window.Owner ?? Application.Current?.MainWindow;
            if (owner is null || ReferenceEquals(owner, window)) return;
            window.SetBinding(FrameworkElement.LanguageProperty, new Binding(nameof(FrameworkElement.Language)) { Source = owner, Mode = BindingMode.OneWay });
            window.SetBinding(FrameworkElement.FlowDirectionProperty, new Binding(nameof(FrameworkElement.FlowDirection)) { Source = owner, Mode = BindingMode.OneWay });
        }
    }
}
