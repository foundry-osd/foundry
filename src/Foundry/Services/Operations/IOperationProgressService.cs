namespace Foundry.Services.Operations;

public interface IOperationProgressService
{
    event EventHandler<OperationProgressChangedEventArgs>? StateChanged;
    OperationProgressState State { get; }
    void Start(OperationKind kind, string status);
    void Report(int progress, string status);
    void Report(int progress, string status, int? secondaryProgress, string secondaryStatus);
    void ClearSecondary();
    void Complete(string status);
    void Reset(string status = "");
}
