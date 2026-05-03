namespace Foundry.Services.Operations;

public interface IOperationProgressService
{
    event EventHandler<OperationProgressChangedEventArgs>? StateChanged;
    OperationProgressState State { get; }
    void Start(OperationKind kind, string status);
    void Report(int progress, string status);
    void Complete(string status);
    void Reset(string status = "");
}
