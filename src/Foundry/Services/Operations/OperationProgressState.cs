namespace Foundry.Services.Operations;

public sealed record OperationProgressState(
    OperationKind Kind,
    int Progress,
    string Status,
    int? SecondaryProgress,
    string SecondaryStatus)
{
    public static OperationProgressState Idle { get; } = new(OperationKind.None, 0, string.Empty, null, string.Empty);
    public bool IsRunning => Kind != OperationKind.None;
    public bool HasSecondaryProgress => SecondaryProgress.HasValue || !string.IsNullOrWhiteSpace(SecondaryStatus);
}
