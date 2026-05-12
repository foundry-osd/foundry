namespace Foundry.Services.Updates;

/// <summary>
/// Carries the latest published application update check result.
/// </summary>
/// <param name="currentResult">Current update result, or <see langword="null"/> before the first result is published.</param>
public sealed class ApplicationUpdateStateChangedEventArgs(ApplicationUpdateCheckResult? currentResult) : EventArgs
{
    /// <summary>
    /// Gets the current update result.
    /// </summary>
    public ApplicationUpdateCheckResult? CurrentResult { get; } = currentResult;
}
