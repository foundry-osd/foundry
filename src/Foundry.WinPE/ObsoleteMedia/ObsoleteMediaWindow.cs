// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Runtime.InteropServices;
using System.Windows;
using Foundry.WinPE.Ui;
using Foundry.Wpf.Dialogs;

namespace Foundry.WinPE.ObsoleteMedia;

/// <summary>Shows the localized obsolete-media message in the Foundry WPF theme; it holds no decision logic.</summary>
internal static class ObsoleteMediaWindow
{
    // WinPE lacks the desktop composition the Fluent backdrop needs; the switch only removes the backdrop elsewhere.
    private const string DisableFluentBackdropSwitch = "Switch.System.Windows.Appearance.DisableFluentThemeWindowBackdrop";

    /// <summary>
    /// Detaches the inherited console, shows the window modally on the calling STA thread until the operator closes it,
    /// and invokes <paramref name="onRendered"/> after its content is first rendered.
    /// </summary>
    internal static void Show(Action onRendered)
    {
        // The executable uses the console subsystem; the legacy launchers expect a window-only application.
        FreeConsole();
        AppContext.SetSwitch(DisableFluentBackdropSwitch, true);
        WinPeDialogApplication.EnsureCreated();

        var window = new LocalizedMessageDialog(
            WinPeDialogApplication.Localize("ObsoleteMedia.Title", "Obsolete boot media"),
            WinPeDialogApplication.Localize("ObsoleteMedia.Message", "This boot media is obsolete. Update Foundry OSD and recreate the media."),
            WinPeDialogApplication.Localize("ObsoleteMedia.Close", "Close"))
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
