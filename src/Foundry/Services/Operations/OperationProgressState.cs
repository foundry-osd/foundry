namespace Foundry.Services.Operations;

public sealed record OperationProgressState(OperationKind Kind, int Progress, string Status)
{
    public static OperationProgressState Idle { get; } = new(OperationKind.None, 0, string.Empty);
    public bool IsRunning => Kind != OperationKind.None;
}
