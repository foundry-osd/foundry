// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Globalization;
using System.Windows;
using Foundry.Wpf.Dialogs;

namespace Foundry.WinPE.Ui;

/// <summary>What the operator chose in the crash window.</summary>
internal enum UiCrashChoice
{
    /// <summary>The window was closed: the boot ends.</summary>
    Close,

    /// <summary>A new UI process starts, from the Connect phase.</summary>
    Relaunch
}

/// <summary>
/// Shows the localized crash window of a UI process that stopped unexpectedly: a message, the log folder, "Relaunch" when
/// allowed, and "Close". It holds no decision logic beyond mapping the button to a <see cref="UiCrashChoice"/>.
/// </summary>
/// <remarks>
/// The guardian is a console process: the window is shown without detaching the startnet console, which keeps showing the
/// boot progress. The window stays on top, so a console that owns the foreground cannot hide it.
/// </remarks>
internal static class UiCrashWindow
{
    /// <summary>
    /// Shows the window modally on the calling STA thread, creating the process's WPF application when needed, and returns the
    /// operator's choice.
    /// </summary>
    /// <param name="relaunchAllowed">Whether "Relaunch" is offered: only when no destructive step had started.</param>
    /// <param name="logDirectory">The folder holding the session logs, or <see langword="null"/> when none is known.</param>
    internal static UiCrashChoice Show(bool relaunchAllowed, string? logDirectory)
    {
        WinPeUiHost.ConfigureFluentBackdrop();
        WinPeDialogApplication.EnsureCreated();
        string message = relaunchAllowed
            ? WinPeDialogApplication.Localize("UiCrash.MessageRelaunchAllowed",
                "Foundry stopped unexpectedly before the deployment changed the target disk. You can relaunch Foundry, which starts again from the network connection.")
            : WinPeDialogApplication.Localize("UiCrash.MessageRelaunchRefused",
                "Foundry stopped unexpectedly. The deployment may already have changed the target disk, so Foundry cannot be relaunched. Restart the computer to start a new deployment.");
        if (!string.IsNullOrWhiteSpace(logDirectory))
        {
            message += Environment.NewLine + Environment.NewLine + string.Format(CultureInfo.CurrentCulture,
                WinPeDialogApplication.Localize("UiCrash.LogFolder", "Logs: {0}"), logDirectory);
        }

        string title = WinPeDialogApplication.Localize("UiCrash.Title", "Foundry stopped");
        string close = WinPeDialogApplication.Localize("UiCrash.Close", "Close");
        LocalizedMessageDialog window = relaunchAllowed
            ? new LocalizedMessageDialog(title, message, WinPeDialogApplication.Localize("UiCrash.Relaunch", "Relaunch"), close)
            : new LocalizedMessageDialog(title, message, close);
        window.ShowInTaskbar = true;
        window.Topmost = true;
        window.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        try
        {
            return ToChoice(relaunchAllowed, window.ShowDialog());
        }
        finally
        {
            // The application adopts the first window as its main window; a later crash window must not take a closed owner.
            if (ReferenceEquals(Application.Current?.MainWindow, window))
            {
                Application.Current!.MainWindow = null;
            }
        }
    }

    /// <summary>
    /// Maps the window's result to the choice: only the confirming button of a window that offers "Relaunch" relaunches.
    /// </summary>
    /// <param name="relaunchAllowed">Whether the window offered "Relaunch" as its confirming button.</param>
    /// <param name="dialogResult">The <see cref="Window.ShowDialog"/> result.</param>
    internal static UiCrashChoice ToChoice(bool relaunchAllowed, bool? dialogResult) =>
        relaunchAllowed && dialogResult == true ? UiCrashChoice.Relaunch : UiCrashChoice.Close;
}
