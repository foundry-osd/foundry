// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Input;
using Foundry.Localization;

namespace Foundry.Wpf.Progress;

/// <summary>
/// Data context of <see cref="ProgressPageView"/>: exactly what the page binds or listens to. The host owns every value,
/// including the localized texts, and raises <see cref="INotifyPropertyChanged.PropertyChanged"/> on the UI thread.
/// </summary>
/// <remarks>
/// The page animates its central state when <see cref="IsSuccessPage"/> or <see cref="IsErrorPage"/> is raised as
/// <see langword="true"/>, and announces its live region when a page flag is raised as <see langword="true"/> or when
/// <see cref="CurrentStepName"/>, <see cref="TerminalErrorTitle"/> or <see cref="CompletionInstructionText"/> changes;
/// announcements raised in the same dispatcher turn are coalesced into one. The page does not track the previous value:
/// raising <see cref="IsSuccessPage"/> or <see cref="IsErrorPage"/> again while it is already <see langword="true"/>
/// re-animates and re-announces, so hosts raise the page flags only when the page changes.
/// </remarks>
public interface IProgressPageViewModel : INotifyPropertyChanged
{
    /// <summary>Gets the host's localized strings; the page reads the keys listed in <see cref="ProgressPageResourceKeys"/>.</summary>
    LocalizedStrings Strings { get; }

    /// <summary>Gets the page title, the name of the computer being prepared.</summary>
    string ComputerNameText { get; }

    /// <summary>Gets the IPv4 address shown in the session panel.</summary>
    string IpAddress { get; }

    /// <summary>Gets the MAC address shown in the session panel.</summary>
    string MacAddress { get; }

    /// <summary>Gets the subnet mask shown in the session panel.</summary>
    string SubnetMask { get; }

    /// <summary>Gets the default gateway shown in the session panel.</summary>
    string GatewayAddress { get; }

    /// <summary>Gets the formatted start time of the run.</summary>
    string StartTimeText { get; }

    /// <summary>Gets the formatted elapsed time of the run.</summary>
    string ElapsedTimeText { get; }

    /// <summary>Gets the timeline steps; the page scrolls the first <see cref="ProgressTimelineEntry.IsActive"/> entry into view.</summary>
    ObservableCollection<ProgressTimelineEntry> TimelineEntries { get; }

    /// <summary>Gets whether the run is in progress: the ring, the current step and the cancel area are shown.</summary>
    bool IsProgressPage { get; }

    /// <summary>Gets whether the run succeeded: the success glyph, instruction and completion action are shown.</summary>
    bool IsSuccessPage { get; }

    /// <summary>Gets whether the run ended on a failure or a cancellation: the terminal error title and instruction are shown.</summary>
    bool IsErrorPage { get; }

    /// <summary>Gets whether the error page reports a cancellation rather than a failure.</summary>
    bool IsCancelled { get; }

    /// <summary>Gets whether the error page reports a failure: the failure glyph and the technical details action are shown.</summary>
    bool HasFailure { get; }

    /// <summary>Gets whether the overall ring shows indeterminate progress.</summary>
    bool IsGlobalProgressIndeterminate { get; }

    /// <summary>Gets the overall progress, from 0 to 100.</summary>
    double OverallProgress { get; }

    /// <summary>Gets the localized name of the running step.</summary>
    string CurrentStepName { get; }

    /// <summary>Gets whether the step progress bar is indeterminate.</summary>
    bool IsCurrentStepProgressIndeterminate { get; }

    /// <summary>Gets the step progress, from 0 to 100.</summary>
    double CurrentStepProgress { get; }

    /// <summary>Gets the localized step progress text.</summary>
    string CurrentStepProgressText { get; }

    /// <summary>Gets the localized step counter shown at the bottom of the page.</summary>
    string StepCounterText { get; }

    /// <summary>Gets the localized instruction shown on success.</summary>
    string CompletionInstructionText { get; }

    /// <summary>Gets the localized label of the completion action button.</summary>
    string CompletionActionText { get; }

    /// <summary>Gets the completion action; the button is hidden when it is <see langword="null"/>.</summary>
    ICommand? CompletionCommand { get; }

    /// <summary>Gets the localized title of the error page, for a failure or a cancellation.</summary>
    string TerminalErrorTitle { get; }

    /// <summary>Gets the localized instruction of the error page.</summary>
    string TerminalErrorInstruction { get; }

    /// <summary>Gets whether the run can be cancelled: the cancel button is shown while it is <see langword="true"/>.</summary>
    bool CanCancel { get; }

    /// <summary>Gets whether a cancellation was requested and the run has not ended yet.</summary>
    bool IsCancellationPending { get; }

    /// <summary>Gets the command bound to the cancel button; its <c>CanExecute</c> drives the button state.</summary>
    ICommand? CancelCommand { get; }
}
