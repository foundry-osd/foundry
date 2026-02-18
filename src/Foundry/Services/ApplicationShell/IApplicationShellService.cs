namespace Foundry.Services.ApplicationShell;

public interface IApplicationShellService
{
    void Shutdown();

    void ShowAbout();

    string? PickIsoOutputPath(string defaultFileName);

    bool ConfirmWarning(string title, string message);
}
