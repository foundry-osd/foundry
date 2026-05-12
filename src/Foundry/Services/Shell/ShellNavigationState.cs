namespace Foundry.Services.Shell;

/// <summary>
/// Describes which shell routes and actions are currently available.
/// </summary>
public enum ShellNavigationState
{
    /// <summary>
    /// Navigation is blocked because required Windows ADK components are missing or invalid.
    /// </summary>
    AdkBlocked,

    /// <summary>
    /// The shell can navigate normally.
    /// </summary>
    Ready,

    /// <summary>
    /// Navigation is restricted while a long-running operation is active.
    /// </summary>
    OperationRunning
}
