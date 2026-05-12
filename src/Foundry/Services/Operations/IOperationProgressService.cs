namespace Foundry.Services.Operations;

/// <summary>
/// Publishes process-wide progress for long-running operations that temporarily block navigation.
/// </summary>
public interface IOperationProgressService
{
    /// <summary>
    /// Occurs after the operation progress snapshot changes.
    /// </summary>
    event EventHandler<OperationProgressChangedEventArgs>? StateChanged;

    /// <summary>
    /// Gets the latest operation progress snapshot.
    /// </summary>
    OperationProgressState State { get; }

    /// <summary>
    /// Starts a new operation and resets primary and secondary progress.
    /// </summary>
    /// <param name="kind">Operation category that drives shell behavior.</param>
    /// <param name="status">Initial user-visible status text.</param>
    void Start(OperationKind kind, string status);

    /// <summary>
    /// Updates primary operation progress and clears secondary progress.
    /// </summary>
    /// <param name="progress">Primary progress percentage.</param>
    /// <param name="status">User-visible status text.</param>
    void Report(int progress, string status);

    /// <summary>
    /// Updates primary and nested operation progress.
    /// </summary>
    /// <param name="progress">Primary progress percentage.</param>
    /// <param name="status">User-visible primary status text.</param>
    /// <param name="secondaryProgress">Nested operation progress percentage, when available.</param>
    /// <param name="secondaryStatus">User-visible nested operation status text.</param>
    void Report(int progress, string status, int? secondaryProgress, string secondaryStatus);

    /// <summary>
    /// Clears nested operation progress while preserving the active primary operation.
    /// </summary>
    void ClearSecondary();

    /// <summary>
    /// Marks the active operation as fully progressed without returning the service to idle.
    /// </summary>
    /// <param name="status">Completion status text shown before the operation is reset.</param>
    void Complete(string status);

    /// <summary>
    /// Returns the service to the idle operation state.
    /// </summary>
    /// <param name="status">Optional idle status text retained by subscribers.</param>
    void Reset(string status = "");
}
