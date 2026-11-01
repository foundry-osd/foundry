// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Wpf.Dialogs;
using Foundry.Wpf.Localization;

namespace Foundry.AutopilotInteractive.Services;

/// <summary>
/// Asks the technician to confirm that closing the window skips the Autopilot registration.
/// </summary>
public interface ISkipConfirmation
{
    /// <summary>
    /// Shows the confirmation.
    /// </summary>
    /// <returns><see langword="true"/> when the technician confirmed the skip.</returns>
    Task<bool> ConfirmAsync();
}

/// <summary>
/// <see cref="ISkipConfirmation"/> shown as the shared Foundry message dialog, owned by the main window so it stays above
/// the topmost shell.
/// </summary>
/// <param name="localization">Source of the dialog texts.</param>
public sealed class DialogSkipConfirmation(ILocalizationService localization) : ISkipConfirmation
{
    /// <inheritdoc />
    public Task<bool> ConfirmAsync()
    {
        LocalizedMessageDialog dialog = new(
            localization.GetString("App.Name"),
            localization.GetString("Close.ConfirmMessage"),
            localization.GetString("Close.ConfirmSkip"),
            localization.GetString("Close.Cancel"));
        return Task.FromResult(dialog.ShowDialog() == true);
    }
}
