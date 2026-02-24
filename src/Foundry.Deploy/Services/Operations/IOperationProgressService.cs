namespace Foundry.Deploy.Services.Operations;

public interface IOperationProgressService
{
    bool IsOperationInProgress { get; }
    int Progress { get; }
    string? Status { get; }
    OperationKind? CurrentOperation { get; }
    bool CanStartOperation { get; }

    event EventHandler? ProgressChanged;

    bool TryStart(OperationKind kind, string initialStatus, int initialProgress = 0);
    void Report(int progress, string? status = null);
    void Complete(string? status = null);
    void Fail(string status);
    void ResetToIdle();
}
