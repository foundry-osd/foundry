namespace Foundry.Services.Operations;

public sealed class OperationProgressChangedEventArgs(OperationProgressState state) : EventArgs
{
    public OperationProgressState State { get; } = state;
}
