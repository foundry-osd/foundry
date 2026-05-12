using Serilog;

namespace Foundry.Services.Shell;

/// <summary>
/// Centralizes route-blocking state for the shell so views do not infer availability independently.
/// </summary>
internal sealed class ShellNavigationGuardService(ILogger logger) : IShellNavigationGuardService
{
    private readonly ILogger logger = logger.ForContext<ShellNavigationGuardService>();

    /// <inheritdoc />
    public event EventHandler? StateChanged;

    /// <inheritdoc />
    public ShellNavigationState State { get; private set; } = ShellNavigationState.AdkBlocked;

    /// <inheritdoc />
    public void SetState(ShellNavigationState state)
    {
        if (!Enum.IsDefined(state))
        {
            logger.Warning("Invalid shell navigation state requested. RequestedState={RequestedState}", state);
            return;
        }

        if (State == state)
        {
            return;
        }

        ShellNavigationState previousState = State;
        State = state;
        logger.Debug("Shell navigation state changed. PreviousState={PreviousState}, NewState={NewState}", previousState, State);
        StateChanged?.Invoke(this, EventArgs.Empty);
    }
}
