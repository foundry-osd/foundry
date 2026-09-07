// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Globalization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;

namespace Foundry.Services.Localization;

/// <summary>Applies selected language metadata and direction to WinUI roots and detached dialogs.</summary>
internal static class LocalizationRoot
{
    public static void Apply(FrameworkElement element, string language)
    {
        CultureInfo culture = CultureInfo.GetCultureInfo(language);
        element.Language = culture.IetfLanguageTag;
        element.FlowDirection = culture.TextInfo.IsRightToLeft ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
    }

    public static void BindToMainRoot(FrameworkElement element)
    {
        if (App.MainWindow.Content is not FrameworkElement root) return;
        element.SetBinding(FrameworkElement.LanguageProperty, new Binding { Source = root, Path = new PropertyPath(nameof(FrameworkElement.Language)), Mode = BindingMode.OneWay });
        element.SetBinding(FrameworkElement.FlowDirectionProperty, new Binding { Source = root, Path = new PropertyPath(nameof(FrameworkElement.FlowDirection)), Mode = BindingMode.OneWay });
    }
}
