namespace Foundry.Services.Shell;

public interface IShellNavigationGuardService
{
    event EventHandler? StateChanged;
    ShellNavigationState State { get; }
    void SetState(ShellNavigationState state);
}
