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
    public void Start(OperationKind kind, string status, Action? cancel = null)
    {
        if (kind == OperationKind.None)
        {
            throw new ArgumentOutOfRangeException(nameof(kind), kind, "An operation kind is required.");
        }

        cancelOperation = cancel;
        SetState(new(kind, 0, status, null, string.Empty) { CanCancel = cancel is not null });
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
    public void Report(int progress, string status)
    {
        Report(progress, status, null, string.Empty);
    }

    /// <inheritdoc />
    public void Report(int progress, string status, int? secondaryProgress, string secondaryStatus)
    {
        if (!State.IsRunning)
        {
            return;
        }

        SetState(State with
        {
            Progress = Math.Clamp(progress, 0, 100),
            Status = status,
            SecondaryProgress = secondaryProgress.HasValue
                ? Math.Clamp(secondaryProgress.Value, 0, 100)
                : null,
            SecondaryStatus = secondaryStatus
        });
    }

    /// <inheritdoc />
    public void ClearSecondary()
    {
        if (!State.IsRunning || !State.HasSecondaryProgress)
        {
            return;
        }

        SetState(State with { SecondaryProgress = null, SecondaryStatus = string.Empty });
    }

    /// <inheritdoc />
    public void Complete(string status)
    {
        if (!State.IsRunning)
        {
            return;
        }

        cancelOperation = null;
        SetState(State with
        {
            CanCancel = false,
            Progress = 100,
            Status = status,
            SecondaryProgress = null,
            SecondaryStatus = string.Empty
        });
    }

    /// <inheritdoc />
    public void Reset(string status = "", OperationOutcome outcome = OperationOutcome.None)
    {
        cancelOperation = null;
        SetState(OperationProgressState.Idle with { Progress = State.Progress, Status = status, Outcome = outcome });
    }

    private void SetState(OperationProgressState state)
    {
        State = state;
        StateChanged?.Invoke(this, new(State));
    }
}
