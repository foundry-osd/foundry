namespace Foundry.Services.Shell;

internal sealed class ShellNavigationGuardService : IShellNavigationGuardService
{
    public event EventHandler? StateChanged;

    public ShellNavigationState State { get; private set; } = ShellNavigationState.Ready;

    public void SetState(ShellNavigationState state)
    {
        if (State == state)
        {
            return;
        }

        State = state;
        StateChanged?.Invoke(this, EventArgs.Empty);
    }
}
