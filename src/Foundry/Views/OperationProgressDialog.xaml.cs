// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Globalization;
using Foundry.Services.Localization;
using Foundry.Services.Operations;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Media;

namespace Foundry.Views;

/// <summary>
/// Shows the progress of the long-running operation published by <see cref="IOperationProgressService"/>, then
/// its outcome. The dialog cannot be dismissed until the operation is finished; its close button requests
/// cancellation while the operation runs.
/// </summary>
public sealed partial class OperationProgressDialog : ContentDialog
{
    private readonly IOperationProgressService operationProgressService;
    private readonly IApplicationLocalizationService localizationService;
    private readonly DispatcherQueueTimer elapsedTimer;
    private static readonly TimeSpan CancelConfirmationWindow = TimeSpan.FromSeconds(10);
    private DateTimeOffset? cancelRequestedAt;
    private bool isDetached;

    /// <summary>
    /// Initializes a new instance of the <see cref="OperationProgressDialog"/> class.
    /// </summary>
    public OperationProgressDialog(
        IOperationProgressService operationProgressService,
        IApplicationLocalizationService localizationService)
    {
        this.operationProgressService = operationProgressService;
        this.localizationService = localizationService;
        InitializeComponent();

        elapsedTimer = DispatcherQueue.CreateTimer();
        elapsedTimer.Interval = TimeSpan.FromSeconds(1);
        elapsedTimer.Tick += OnElapsedTimerTick;
        operationProgressService.StateChanged += OnStateChanged;
        Apply(operationProgressService.State);
        elapsedTimer.Start();
    }

    /// <summary>
    /// Gets the warning shown while the user confirms a cancellation.
    /// </summary>
    public string ConfirmStopHint => localizationService.GetString("Shell.Operation.ConfirmStopHint");

    private bool IsFinished => operationProgressService.State is { IsFinished: true } or { IsRunning: false };

    private void OnStateChanged(object? sender, OperationProgressChangedEventArgs e)
    {
        if (!DispatcherQueue.HasThreadAccess)
        {
            DispatcherQueue.TryEnqueue(() => Apply(operationProgressService.State));
            return;
        }

        Apply(e.State);
    }

    private void Apply(OperationProgressState state)
    {
        bool isFinished = state.IsFinished || !state.IsRunning;
        Title = state.Title;

        StatusText.Text = !isFinished && state.IsCancellationRequested
            ? localizationService.GetString("Shell.OperationStopping")
            : state.Status;
        StatusText.IsTextSelectionEnabled = isFinished;

        PrimaryProgressRow.Visibility = isFinished ? Visibility.Collapsed : Visibility.Visible;
        PrimaryProgressBar.Value = state.Progress;
        PrimaryPercentText.Text = FormatPercent(state.Progress);
        AutomationProperties.SetName(PrimaryProgressBar, StatusText.Text);

        DetailPanel.Visibility = !isFinished && state.HasSecondaryProgress ? Visibility.Visible : Visibility.Collapsed;
        DetailText.Text = state.SecondaryStatus;
        DetailProgressRow.Visibility = state.SecondaryProgress.HasValue ? Visibility.Visible : Visibility.Collapsed;
        DetailProgressBar.Value = state.SecondaryProgress ?? 0;
        DetailPercentText.Text = state.SecondaryProgress is int detailProgress ? FormatPercent(detailProgress) : string.Empty;

        ApplyOutcomeIcon(isFinished ? state.Outcome : OperationOutcome.None);
        ApplyButtons(state, isFinished);
        UpdateElapsedText(state, isFinished);
    }

    private void ApplyOutcomeIcon(OperationOutcome outcome)
    {
        (string? glyph, string brushKey) = outcome switch
        {
            OperationOutcome.Success => ("\uE73E", "SystemFillColorSuccessBrush"),
            OperationOutcome.Error => ("\uEA39", "SystemFillColorCriticalBrush"),
            OperationOutcome.Cancelled => ("\uE946", "TextFillColorSecondaryBrush"),
            _ => ((string?)null, string.Empty)
        };

        OutcomeIcon.Visibility = glyph is null ? Visibility.Collapsed : Visibility.Visible;
        if (glyph is not null)
        {
            OutcomeIcon.Glyph = glyph;
            OutcomeIcon.Foreground = (Brush)Application.Current.Resources[brushKey];
        }
    }

    private void ApplyButtons(OperationProgressState state, bool isFinished)
    {
        if (isFinished)
        {
            PrimaryButtonText = state.FinishedAction?.Text ?? string.Empty;
            CloseButtonText = localizationService.GetString("Common.Close");
            DefaultButton = ContentDialogButton.Close;
            return;
        }

        // While running, Enter must not cancel a long operation, so no button is the default. Cancel needs a second
        // selection, so a single Escape key press cannot stop a long disk or image step.
        bool isConfirmingCancel = state.CanCancel && cancelRequestedAt is not null;
        PrimaryButtonText = string.Empty;
        CloseButtonText = !state.CanCancel
            ? string.Empty
            : localizationService.GetString(isConfirmingCancel ? "Shell.Operation.ConfirmStop" : "Common.Cancel");
        DefaultButton = ContentDialogButton.None;
        CancelConfirmationText.Visibility = isConfirmingCancel ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnElapsedTimerTick(DispatcherQueueTimer sender, object args)
    {
        if (cancelRequestedAt is DateTimeOffset requestedAt && DateTimeOffset.Now - requestedAt > CancelConfirmationWindow)
        {
            cancelRequestedAt = null;
            Apply(operationProgressService.State);
            return;
        }

        UpdateElapsedText(operationProgressService.State, IsFinished);
    }

    private void UpdateElapsedText(OperationProgressState state, bool isFinished)
    {
        if (isFinished)
        {
            elapsedTimer.Stop();
        }

        if (state.StartedAt == default)
        {
            ElapsedText.Visibility = Visibility.Collapsed;
            return;
        }

        if (!isFinished)
        {
            TimeSpan elapsed = DateTimeOffset.Now - state.StartedAt;
            string format = elapsed.TotalHours >= 1 ? @"h\:mm\:ss" : @"mm\:ss";
            ElapsedText.Text = localizationService.FormatString(
                "Shell.Operation.ElapsedFormat",
                elapsed.ToString(format, CultureInfo.CurrentUICulture));
        }

        ElapsedText.Visibility = Visibility.Visible;
    }

    private void OnCloseButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        if (IsFinished)
        {
            return;
        }

        args.Cancel = true;
        if (cancelRequestedAt is null)
        {
            cancelRequestedAt = DateTimeOffset.Now;
            Apply(operationProgressService.State);
            return;
        }

        cancelRequestedAt = null;
        operationProgressService.RequestCancellation();
    }

    private void OnPrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        args.Cancel = true;
        operationProgressService.State.FinishedAction?.Invoke();
    }

    private void OnClosing(ContentDialog sender, ContentDialogClosingEventArgs args)
    {
        if (!IsFinished)
        {
            args.Cancel = true;
        }
    }

    private void OnClosed(ContentDialog sender, ContentDialogClosedEventArgs args)
    {
        Detach();
    }

    /// <summary>
    /// Stops following the operation. Safe to call more than once, including when the dialog never opened.
    /// </summary>
    public void Detach()
    {
        if (isDetached)
        {
            return;
        }

        isDetached = true;
        elapsedTimer.Stop();
        elapsedTimer.Tick -= OnElapsedTimerTick;
        operationProgressService.StateChanged -= OnStateChanged;
    }

    private static string FormatPercent(int progress)
    {
        return (Math.Clamp(progress, 0, 100) / 100.0).ToString("P0", CultureInfo.CurrentUICulture);
    }
}
