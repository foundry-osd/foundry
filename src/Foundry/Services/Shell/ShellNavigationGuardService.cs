using Serilog;

namespace Foundry.Services.Shell;

internal sealed class ShellNavigationGuardService(ILogger logger) : IShellNavigationGuardService
{
    private readonly ILogger logger = logger.ForContext<ShellNavigationGuardService>();

    public event EventHandler? StateChanged;

    public ShellNavigationState State { get; private set; } = ShellNavigationState.AdkBlocked;

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
