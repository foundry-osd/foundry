namespace Foundry.Services.Shell;

/// <summary>
/// Coordinates shell navigation availability across startup checks and long-running operations.
/// </summary>
public interface IShellNavigationGuardService
{
    /// <summary>
    /// Occurs when the navigation guard state changes.
    /// </summary>
    event EventHandler? StateChanged;

    /// <summary>
    /// Gets the current navigation guard state.
    /// </summary>
    ShellNavigationState State { get; }

    /// <summary>
    /// Applies a new navigation guard state and notifies subscribers when it changes.
    /// </summary>
    /// <param name="state">New shell navigation state.</param>
    void SetState(ShellNavigationState state);
}
