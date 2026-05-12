namespace Foundry.Services.Operations;

/// <summary>
/// Stores and broadcasts the current shell-level operation progress state.
/// </summary>
internal sealed class OperationProgressService : IOperationProgressService
{
    /// <inheritdoc />
    public event EventHandler<OperationProgressChangedEventArgs>? StateChanged;

    /// <inheritdoc />
    public OperationProgressState State { get; private set; } = OperationProgressState.Idle;

    /// <inheritdoc />
    public void Start(OperationKind kind, string status)
    {
        if (kind == OperationKind.None)
        {
            throw new ArgumentOutOfRangeException(nameof(kind), kind, "An operation kind is required.");
        }

        SetState(new(kind, 0, status, null, string.Empty));
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

        SetState(State with
        {
            Progress = 100,
            Status = status,
            SecondaryProgress = null,
            SecondaryStatus = string.Empty
        });
    }

    /// <inheritdoc />
    public void Reset(string status = "")
    {
        SetState(OperationProgressState.Idle with { Status = status });
    }

    private void SetState(OperationProgressState state)
    {
        State = state;
        StateChanged?.Invoke(this, new(State));
    }
}
