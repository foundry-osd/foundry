namespace Foundry.Services.ApplicationShell;

public interface IApplicationShellService
{
    void Shutdown();

    void ShowAbout();

    string? PickIsoOutputPath(string defaultFileName);

    string? PickFolderPath(string title, string? initialPath = null);

    void OpenFolder(string path);

    bool ConfirmWarning(string title, string message);
}
