// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Services.Operations;

/// <summary>
/// Represents the immutable progress snapshot broadcast to shell-level operation UI.
/// </summary>
/// <param name="Kind">The active operation kind, or <see cref="OperationKind.None"/> when idle.</param>
/// <param name="Progress">Primary operation progress clamped to the 0-100 range.</param>
/// <param name="Status">Primary status text displayed to the user, or the terminal message once finished.</param>
/// <param name="SecondaryProgress">Optional progress of the current detail action, clamped to the 0-100 range.</param>
/// <param name="SecondaryStatus">Optional text describing the current detail action.</param>
public sealed record OperationProgressState(
    OperationKind Kind,
    int Progress,
    string Status,
    int? SecondaryProgress,
    string SecondaryStatus)
{
    /// <summary>
    /// Gets the canonical idle operation state.
    /// </summary>
    public static OperationProgressState Idle { get; } = new(OperationKind.None, 0, string.Empty, null, string.Empty);

    /// <summary>
    /// Gets a value indicating whether the snapshot represents an active operation.
    /// </summary>
    public bool IsRunning => Kind != OperationKind.None;

    /// <summary>
    /// Gets the title of the operation dialog: what is running, or the outcome once finished.
    /// </summary>
    public string Title { get; init; } = string.Empty;

    /// <summary>
    /// Gets when the operation started, used to show the elapsed time.
    /// </summary>
    public DateTimeOffset StartedAt { get; init; }

    /// <summary>
    /// Gets whether the operation reached its outcome. The dialog may close although cleanup can still run.
    /// </summary>
    public bool IsFinished { get; init; }

    /// <summary>
    /// Gets the terminal result retained after the operation finishes.
    /// </summary>
    public OperationOutcome Outcome { get; init; }

    /// <summary>
    /// Gets an optional follow-up action offered once the operation is finished, such as opening the output folder.
    /// </summary>
    public OperationAction? FinishedAction { get; init; }

    /// <summary>
    /// Gets whether the operation currently accepts a user cancellation request.
    /// </summary>
    public bool CanCancel { get; init; }

    /// <summary>
    /// Gets whether the operation is stopping at a safe boundary and still owns its resources.
    /// </summary>
    public bool IsCancellationRequested { get; init; }

    /// <summary>
    /// Gets a value indicating whether a detail line should be shown.
    /// </summary>
    public bool HasSecondaryProgress => !string.IsNullOrWhiteSpace(SecondaryStatus);
}

/// <summary>
/// Describes a follow-up action offered by a finished operation.
/// </summary>
/// <param name="Text">Localized button text.</param>
/// <param name="Invoke">Action run when the button is selected; the dialog stays open.</param>
public sealed record OperationAction(string Text, Action Invoke);
