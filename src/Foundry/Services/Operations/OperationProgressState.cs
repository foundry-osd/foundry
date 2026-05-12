namespace Foundry.Services.Operations;

/// <summary>
/// Represents the immutable progress snapshot broadcast to shell-level operation UI.
/// </summary>
/// <param name="Kind">The active operation kind, or <see cref="OperationKind.None"/> when idle.</param>
/// <param name="Progress">Primary operation progress clamped to the 0-100 range.</param>
/// <param name="Status">Primary status text displayed to the user.</param>
/// <param name="SecondaryProgress">Optional nested operation progress clamped to the 0-100 range.</param>
/// <param name="SecondaryStatus">Optional nested operation status text.</param>
public sealed record OperationProgressState(
    OperationKind Kind,
    int Progress,
    string Status,
    int? SecondaryProgress,
    string SecondaryStatus)
{
    /// <summary>
    /// Gets the canonical idle operation state.
    /// </summary>
    public static OperationProgressState Idle { get; } = new(OperationKind.None, 0, string.Empty, null, string.Empty);

    /// <summary>
    /// Gets a value indicating whether the snapshot represents an active operation.
    /// </summary>
    public bool IsRunning => Kind != OperationKind.None;

    /// <summary>
    /// Gets a value indicating whether nested progress should be shown.
    /// </summary>
    public bool HasSecondaryProgress => SecondaryProgress.HasValue || !string.IsNullOrWhiteSpace(SecondaryStatus);
}
