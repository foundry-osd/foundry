// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Globalization;
using System.Resources;
using System.Windows;

namespace Foundry.WinPE.Ui;

/// <summary>
/// The WPF application and the localized texts of the standalone windows Foundry.WinPE shows outside the UI process: the
/// obsolete-media window and the crash window. It holds no decision logic.
/// </summary>
internal static class WinPeDialogApplication
{
    private static readonly ResourceManager Strings = new("Foundry.WinPE.Strings.Resources", typeof(WinPeDialogApplication).Assembly);

    /// <summary>
    /// Creates the process's WPF <see cref="Application"/> on the calling STA thread when none exists yet, with the Fluent
    /// theme and the shared Foundry styles. Later windows of the process must be shown on that thread.
    /// </summary>
    internal static void EnsureCreated()
    {
        if (Application.Current is not null)
        {
            return;
        }

        var application = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        // Same order as Ui/WinPeApplicationResources.xaml: the shared styles are based on the Fluent styles merged before them.
        application.Resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri("pack://application:,,,/PresentationFramework.Fluent;component/Themes/Fluent.xaml", UriKind.Absolute)
        });
        application.Resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri("pack://application:,,,/Foundry.Wpf;component/Themes/FoundryWpfStyles.xaml", UriKind.Absolute)
        });
    }

    /// <summary>Returns the resource for the current UI culture, or the English text when the key is missing.</summary>
    internal static string Localize(string key, string english) =>
        Strings.GetString(key, CultureInfo.CurrentUICulture) ?? english;
}
