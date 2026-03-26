namespace Foundry.Deploy.Services.ApplicationShell;

public interface IApplicationShellService
{
    void ShowAbout();

    bool ConfirmWarning(string title, string message);
}
