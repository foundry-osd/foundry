// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Globalization;
using System.Resources;
using System.Runtime.InteropServices;
using System.Windows;
using Foundry.Wpf.Dialogs;
using Foundry.Wpf.Theme;

namespace Foundry.WinPE.ObsoleteMedia;

/// <summary>Shows the localized obsolete-media message in the Foundry WPF theme; it holds no decision logic.</summary>
internal static class ObsoleteMediaWindow
{
    // WinPE lacks the desktop composition the Fluent backdrop needs; the switch only removes the backdrop elsewhere.
    private const string DisableFluentBackdropSwitch = "Switch.System.Windows.Appearance.DisableFluentThemeWindowBackdrop";

    private static readonly ResourceManager Strings = new("Foundry.WinPE.Strings.Resources", typeof(ObsoleteMediaWindow).Assembly);

    /// <summary>
    /// Detaches the inherited console, shows the window modally on the calling STA thread until the operator closes it,
    /// and invokes <paramref name="onRendered"/> after its content is first rendered.
    /// </summary>
    internal static void Show(Action onRendered)
    {
        // The executable uses the console subsystem; the legacy launchers expect a window-only application.
        FreeConsole();
        AppContext.SetSwitch(DisableFluentBackdropSwitch, true);
        var application = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        _ = new ThemeService();
        application.Resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri("pack://application:,,,/Foundry.Wpf;component/Themes/FoundryWpfStyles.xaml", UriKind.Absolute)
        });

        CultureInfo culture = CultureInfo.CurrentUICulture;
        var window = new LocalizedMessageDialog(
            Strings.GetString("ObsoleteMedia.Title", culture)!,
            Strings.GetString("ObsoleteMedia.Message", culture)!,
            Strings.GetString("ObsoleteMedia.Close", culture)!)
        {
            ShowInTaskbar = true,
            WindowStartupLocation = WindowStartupLocation.CenterScreen
        };
        window.ContentRendered += (_, _) => onRendered();
        window.ShowDialog();
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool FreeConsole();
}
