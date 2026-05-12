namespace Foundry.Services.Operations;

/// <summary>
/// Carries the latest operation progress snapshot to shell and view-model subscribers.
/// </summary>
/// <param name="state">Updated progress snapshot.</param>
public sealed class OperationProgressChangedEventArgs(OperationProgressState state) : EventArgs
{
    /// <summary>
    /// Gets the updated progress snapshot.
    /// </summary>
    public OperationProgressState State { get; } = state;
}
