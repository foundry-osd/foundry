namespace Foundry.Deploy.Services.ApplicationShell;

public interface IApplicationShellService
{
    bool ConfirmWarning(string title, string message);
}
