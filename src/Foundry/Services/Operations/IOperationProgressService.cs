// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Services.Operations;

/// <summary>
/// Publishes process-wide progress for long-running operations that temporarily block navigation.
/// </summary>
public interface IOperationProgressService
{
    /// <summary>
    /// Occurs after the operation progress snapshot changes.
    /// </summary>
    event EventHandler<OperationProgressChangedEventArgs>? StateChanged;

    /// <summary>
    /// Gets the latest operation progress snapshot.
    /// </summary>
    OperationProgressState State { get; }

    /// <summary>
    /// Starts a new operation and resets its progress, detail and outcome.
    /// </summary>
    /// <param name="kind">Operation category that drives shell behavior. <see cref="OperationKind.None"/> is rejected.</param>
    /// <param name="title">User-visible title describing what is running.</param>
    /// <param name="status">Initial user-visible status text.</param>
    /// <param name="cancel">Optional request to cancel the operation at its next safe boundary.</param>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="kind"/> is <see cref="OperationKind.None"/>.</exception>
    void Start(OperationKind kind, string title, string status, Action? cancel = null);

    /// <summary>
    /// Requests cancellation once while preserving running state until the operation finishes cleanup.
    /// </summary>
    void RequestCancellation();

    /// <summary>
    /// Updates progress while an operation is running and not finished.
    /// </summary>
    /// <param name="progress">Primary progress percentage clamped to <c>0..100</c>; a lower value than the current one is ignored.</param>
    /// <param name="status">User-visible status text.</param>
    /// <param name="detailProgress">Progress of the current detail action, when measurable, clamped to <c>0..100</c>.</param>
    /// <param name="detail">User-visible text of the current detail action; empty hides the detail line.</param>
    void Report(int progress, string status, int? detailProgress = null, string detail = "");

    /// <summary>
    /// Shows the outcome of the running operation. Cancellation is no longer offered, and progress becomes 100 only
    /// on success so a failure keeps the point it reached.
    /// </summary>
    /// <param name="outcome">Terminal result; <see cref="OperationOutcome.None"/> is rejected.</param>
    /// <param name="title">User-visible title describing the outcome.</param>
    /// <param name="message">User-visible terminal message.</param>
    /// <param name="action">Optional follow-up action offered with the outcome.</param>
    /// <remarks>No state is changed when the service is idle or already finished.</remarks>
    void Finish(OperationOutcome outcome, string title, string message, OperationAction? action = null);

    /// <summary>
    /// Returns the service to the idle operation state while keeping the finished title, message and outcome for
    /// a dialog that is still open.
    /// </summary>
    void Reset();
}
