// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Services.Operations;

/// <summary>
/// Stores and broadcasts the current shell-level operation progress state.
/// </summary>
internal sealed class OperationProgressService : IOperationProgressService
{
    private Action? cancelOperation;

    /// <inheritdoc />
    public event EventHandler<OperationProgressChangedEventArgs>? StateChanged;

    /// <inheritdoc />
    public OperationProgressState State { get; private set; } = OperationProgressState.Idle;

    /// <inheritdoc />
    public void Start(OperationKind kind, string title, string status, Action? cancel = null)
    {
        if (kind == OperationKind.None)
        {
            throw new ArgumentOutOfRangeException(nameof(kind), kind, "An operation kind is required.");
        }

        cancelOperation = cancel;
        SetState(new(kind, 0, status, null, string.Empty)
        {
            Title = title,
            StartedAt = DateTimeOffset.Now,
            CanCancel = cancel is not null
        });
    }

    /// <inheritdoc />
    public void RequestCancellation()
    {
        if (!State.IsRunning || !State.CanCancel)
        {
            return;
        }

        Action? cancel = cancelOperation;
        cancelOperation = null;
        SetState(State with { CanCancel = false, IsCancellationRequested = true });
        cancel?.Invoke();
    }

    /// <inheritdoc />
    public void Report(int progress, string status, int? detailProgress = null, string detail = "")
    {
        if (!State.IsRunning || State.IsFinished)
        {
            return;
        }

        SetState(State with
        {
            Progress = Math.Max(State.Progress, Math.Clamp(progress, 0, 100)),
            Status = status,
            SecondaryProgress = detailProgress.HasValue ? Math.Clamp(detailProgress.Value, 0, 100) : null,
            SecondaryStatus = detail
        });
    }

    /// <inheritdoc />
    public void Finish(OperationOutcome outcome, string title, string message, OperationAction? action = null)
    {
        if (outcome == OperationOutcome.None)
        {
            throw new ArgumentOutOfRangeException(nameof(outcome), outcome, "A terminal outcome is required.");
        }

        if (!State.IsRunning || State.IsFinished)
        {
            return;
        }

        cancelOperation = null;
        SetState(State with
        {
            IsFinished = true,
            Outcome = outcome,
            Title = title,
            Status = message,
            FinishedAction = action,
            CanCancel = false,
            Progress = outcome == OperationOutcome.Success ? 100 : State.Progress,
            SecondaryProgress = null,
            SecondaryStatus = string.Empty
        });
    }

    /// <inheritdoc />
    public void Reset()
    {
        cancelOperation = null;
        SetState(State with { Kind = OperationKind.None, CanCancel = false, IsCancellationRequested = false });
    }

    private void SetState(OperationProgressState state)
    {
        State = state;
        StateChanged?.Invoke(this, new(State));
    }
}
