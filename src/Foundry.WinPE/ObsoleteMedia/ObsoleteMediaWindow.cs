// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Runtime.InteropServices;
using Foundry.WinPE.Shell;
using Foundry.WinPE.Ui;

namespace Foundry.WinPE.ObsoleteMedia;

/// <summary>Shows the localized obsolete-media message as a shell page in the Foundry WPF theme; it holds no decision logic.</summary>
internal static class ObsoleteMediaWindow
{
    // WinPE lacks the desktop composition the Fluent backdrop needs; the switch only removes the backdrop elsewhere.
    private const string DisableFluentBackdropSwitch = "Switch.System.Windows.Appearance.DisableFluentThemeWindowBackdrop";

    /// <summary>
    /// Detaches the inherited console, shows the shell window with the message on the calling STA thread until the operator
    /// closes it, and invokes <paramref name="onRendered"/> after its content is first rendered. The culture is the process's:
    /// no operator choice exists in this mode.
    /// </summary>
    internal static void Show(Action onRendered)
    {
        // The executable uses the console subsystem; the legacy launchers expect a window-only application.
        FreeConsole();
        AppContext.SetSwitch(DisableFluentBackdropSwitch, true);
        WinPeDialogApplication.EnsureCreated();

        var model = new MessagePageModel(
            WinPeDialogApplication.Localize("ObsoleteMedia.Title", "Obsolete boot media"),
            WinPeDialogApplication.Localize("ObsoleteMedia.Message", "This boot media is obsolete. Update Foundry OSD and recreate the media."),
            WinPeDialogApplication.Localize("ObsoleteMedia.Close", "Close"));
        MessageWindow.ShowModal(model, topmost: false, onRendered);
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool FreeConsole();
}
