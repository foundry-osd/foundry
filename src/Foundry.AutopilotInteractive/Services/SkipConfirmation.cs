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
    /// <param name="registrationInProgress">
    /// A registration is running and its upload may already be sent: the question warns that the device can still appear in
    /// Windows Autopilot.
    /// </param>
    /// <param name="ct">
    /// Cancelled when the question no longer applies (deadline, countdown exit, device registered meanwhile): the question
    /// closes and the answer is "no", so it never blocks a graceful exit.
    /// </param>
    /// <returns><see langword="true"/> when the technician confirmed the skip before <paramref name="ct"/> was cancelled.</returns>
    Task<bool> ConfirmAsync(bool registrationInProgress, CancellationToken ct);
}

/// <summary>
/// <see cref="ISkipConfirmation"/> shown as the shared Foundry message dialog, owned by the main window so it stays above
/// the topmost shell.
/// </summary>
/// <param name="localization">Source of the dialog texts.</param>
public sealed class DialogSkipConfirmation(ILocalizationService localization) : ISkipConfirmation
{
    /// <inheritdoc />
    /// <remarks>
    /// The dialog is modal: the run keeps going in its nested message loop, so a cancellation raised there closes it with a
    /// "no" answer.
    /// </remarks>
    public Task<bool> ConfirmAsync(bool registrationInProgress, CancellationToken ct)
    {
        if (ct.IsCancellationRequested)
        {
            return Task.FromResult(false);
        }

        LocalizedMessageDialog dialog = new(
            localization.GetString("App.Name"),
            localization.GetString(registrationInProgress ? "Close.ConfirmMessageDuringUpload" : "Close.ConfirmMessage"),
            localization.GetString("Close.ConfirmSkip"),
            localization.GetString("Close.Cancel"));
        using CancellationTokenRegistration registration = ct.Register(() => dialog.Dispatcher.InvokeAsync(() =>
        {
            if (dialog.IsVisible)
            {
                dialog.DialogResult = false;
            }
        }));
        return Task.FromResult(dialog.ShowDialog() == true && !ct.IsCancellationRequested);
    }
}
