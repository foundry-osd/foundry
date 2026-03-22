using Foundry.Models.Configuration;

namespace Foundry.Services.ApplicationShell;

public interface IApplicationShellService
{
    void Shutdown();

    void ShowAbout();

    string? PickIsoOutputPath(string defaultFileName);

    string? PickOpenFilePath(string title, string filter);

    string? PickSaveFilePath(string title, string filter, string defaultFileName);

    IReadOnlyList<AutopilotProfileSettings>? PickAutopilotProfilesForImport(IReadOnlyList<AutopilotProfileSettings> availableProfiles);

    string? PickFolderPath(string title, string? initialPath = null);

    void OpenFolder(string path);

    bool ConfirmWarning(string title, string message);
}
