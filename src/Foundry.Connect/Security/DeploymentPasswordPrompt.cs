// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Windows;
using System.Windows.Threading;
using Foundry.Core.Services.Runtime;
using Foundry.Wpf.Dialogs;
using Foundry.Wpf.Localization;

namespace Foundry.Connect.Security;

/// <summary>
/// Shows the shared <see cref="PasswordPromptDialog"/> with the Connect unlock texts on the UI thread, owned by the
/// active window, else by the main window once it is loaded and visible, else by no window.
/// </summary>
/// <remarks>
/// The wired startup prompt runs before the page initialization completes, so the first rendered prompt reports the UI
/// ready: otherwise Bootstrap's readiness window would expire while the technician types the password.
/// </remarks>
public sealed class DeploymentPasswordPrompt(ILocalizationService localization, RuntimeStartupDiagnostics startup)
    : IDeploymentPasswordPrompt
{
    /// <inheritdoc />
    public char[]? Prompt(bool previousAttemptFailed)
    {
        Dispatcher dispatcher = Application.Current?.Dispatcher
            ?? throw new InvalidOperationException("The deployment password prompt requires a running WPF application.");

        return dispatcher.Invoke(() => ShowDialog(previousAttemptFailed));
    }

    private char[]? ShowDialog(bool previousAttemptFailed)
    {
        var dialog = new PasswordPromptDialog
        {
            Title = localization.Strings["Unlock.Title"],
            HeadingText = localization.Strings["Unlock.Heading"],
            DescriptionText = localization.Strings["Unlock.Description"],
            PasswordPlaceholder = localization.Strings["Unlock.PasswordPlaceholder"],
            ContinueText = localization.Strings["Unlock.Continue"],
            CancelText = localization.Strings["Common.Cancel"],
            TogglePasswordVisibilityText = localization.Strings["Unlock.TogglePasswordVisibility"],
            ErrorText = previousAttemptFailed
                ? localization.Strings["Unlock.InvalidPassword"]
                : string.Empty,
            Owner = ResolveOwnerWindow()
        };

        dialog.ContentRendered += (_, _) => OnDialogRendered();
        return dialog.ShowDialog() == true ? dialog.TakePassword() : null;
    }

    /// <summary>Reports the first usable UI; later calls do nothing.</summary>
    internal void OnDialogRendered() => startup.ReportUiReady();

    /// <summary>
    /// Picks the dialog owner. <see cref="Window.Owner"/> throws for a window whose handle is not created yet or that was
    /// closed, so the main window is used only once it is loaded and visible; without an owner the dialog is still modal
    /// for the UI thread.
    /// </summary>
    private static Window? ResolveOwnerWindow()
    {
        Application? application = Application.Current;
        if (application is null)
        {
            return null;
        }

        foreach (Window window in application.Windows)
        {
            if (window.IsActive)
            {
                return window;
            }
        }

        return application.MainWindow is { IsLoaded: true, IsVisible: true } mainWindow ? mainWindow : null;
    }
}
