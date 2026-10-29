// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Windows;
using System.Windows.Threading;
using Foundry.Wpf.Dialogs;
using Foundry.Wpf.Localization;

namespace Foundry.Connect.Security;

/// <summary>
/// Shows the shared <see cref="PasswordPromptDialog"/> with the Connect unlock texts on the UI thread, owned by the
/// active window or the main window (the rule Deploy's password dialog service uses).
/// </summary>
public sealed class DeploymentPasswordPrompt(ILocalizationService localization) : IDeploymentPasswordPrompt
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

        return dialog.ShowDialog() == true ? dialog.TakePassword() : null;
    }

    private static Window? ResolveOwnerWindow()
    {
        if (Application.Current?.Windows is not null)
        {
            foreach (Window window in Application.Current.Windows)
            {
                if (window.IsActive)
                {
                    return window;
                }
            }
        }

        return Application.Current?.MainWindow;
    }
}
