// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Windows;
using System.Windows.Threading;

namespace Foundry.WinPE.Shell;

/// <summary>
/// Shows a <see cref="MessagePage"/> in a <see cref="WinPeShellWindow"/> and waits for the operator's answer, for the screens
/// that run outside the UI process: the guardian's crash screen and the obsolete-media screen.
/// </summary>
internal static class MessageWindow
{
    /// <summary>
    /// Shows the window on the calling STA thread, which must run the process's WPF <see cref="Application"/>, and returns once
    /// it closes: a button press, or any close request, which this window always accepts.
    /// </summary>
    /// <param name="model">The page texts; it receives the pressed button.</param>
    /// <param name="topmost">Whether the window stays in front of every other window, such as the startnet console.</param>
    /// <param name="onRendered">Called once, on the calling thread, when the page rendered, or <see langword="null"/>.</param>
    /// <returns>The button the operator pressed, or <see cref="MessageChoice.None"/> when the window was closed.</returns>
    internal static MessageChoice ShowModal(MessagePageModel model, bool topmost, Action? onRendered = null)
    {
        var window = new WinPeShellWindow { Topmost = topmost };
        var frame = new DispatcherFrame();
        window.Closed += (_, _) => frame.Continue = false;
        // A handler that does not cancel accepts the close: without one the shell refuses it.
        window.CloseRequested += static (_, _) => { };
        model.Chosen += (_, _) =>
        {
            window.AllowClose = true;
            window.Close();
        };
        if (onRendered is not null)
        {
            window.ShellContentRendered += (_, _) => onRendered();
        }

        try
        {
            window.ShowContent(new MessagePage(model));
            Dispatcher.PushFrame(frame);
        }
        finally
        {
            // The application adopts the first window as its main window; a later screen must not take a closed owner.
            if (ReferenceEquals(Application.Current?.MainWindow, window))
            {
                Application.Current!.MainWindow = null;
            }
        }

        return model.Choice;
    }
}
