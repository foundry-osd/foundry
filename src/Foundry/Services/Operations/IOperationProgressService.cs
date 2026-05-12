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
    /// <param name="kind">Operation category that drives shell behavior. <see cref="OperationKind.None"/> is rejected.</param>
    /// <param name="status">Initial user-visible status text.</param>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="kind"/> is <see cref="OperationKind.None"/>.</exception>
    void Start(OperationKind kind, string status);

    /// <summary>
    /// Updates primary operation progress and clears secondary progress when an operation is active.
    /// </summary>
    /// <param name="progress">Primary progress percentage clamped to the <c>0..100</c> range.</param>
    /// <param name="status">User-visible status text.</param>
    /// <remarks>No state is changed when the service is idle.</remarks>
    void Report(int progress, string status);

    /// <summary>
    /// Updates primary and nested operation progress when an operation is active.
    /// </summary>
    /// <param name="progress">Primary progress percentage clamped to the <c>0..100</c> range.</param>
    /// <param name="status">User-visible primary status text.</param>
    /// <param name="secondaryProgress">Nested operation progress percentage, when available, clamped to the <c>0..100</c> range.</param>
    /// <param name="secondaryStatus">User-visible nested operation status text.</param>
    /// <remarks>No state is changed when the service is idle.</remarks>
    void Report(int progress, string status, int? secondaryProgress, string secondaryStatus);

    /// <summary>
    /// Clears nested operation progress while preserving the active primary operation.
    /// </summary>
    /// <remarks>No state is changed when the service is idle or no nested progress is active.</remarks>
    void ClearSecondary();

    /// <summary>
    /// Marks the active operation as fully progressed without returning the service to idle.
    /// </summary>
    /// <param name="status">Completion status text shown before the operation is reset.</param>
    /// <remarks>No state is changed when the service is idle.</remarks>
    void Complete(string status);

    /// <summary>
    /// Returns the service to the idle operation state.
    /// </summary>
    /// <param name="status">Optional idle status text retained by subscribers.</param>
    void Reset(string status = "");
}
