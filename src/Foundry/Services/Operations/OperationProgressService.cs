namespace Foundry.Services.Operations;

internal sealed class OperationProgressService : IOperationProgressService
{
    public event EventHandler<OperationProgressChangedEventArgs>? StateChanged;

    public OperationProgressState State { get; private set; } = OperationProgressState.Idle;

    public void Start(OperationKind kind, string status)
    {
        if (kind == OperationKind.None)
        {
            throw new ArgumentOutOfRangeException(nameof(kind), kind, "An operation kind is required.");
        }

        SetState(new(kind, 0, status));
    }

    public void Report(int progress, string status)
    {
        if (!State.IsRunning)
        {
            return;
        }

        SetState(State with { Progress = Math.Clamp(progress, 0, 100), Status = status });
    }

    public void Complete(string status)
    {
        if (!State.IsRunning)
        {
            return;
        }

        SetState(State with { Progress = 100, Status = status });
    }

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
