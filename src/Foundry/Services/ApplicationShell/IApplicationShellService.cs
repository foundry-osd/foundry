using System.Windows;
using Foundry.Models.Configuration;
using Foundry.Services.ApplicationUpdate;

namespace Foundry.Services.ApplicationShell;

public interface IApplicationShellService
{
    void Shutdown();

    void ShowAbout();

    void ShowUpdateAvailable(ApplicationUpdateInfo updateInfo);

    void OpenUrl(string url);

    string? PickIsoOutputPath(string defaultFileName);

    string? PickOpenFilePath(string title, string filter);

    string? PickSaveFilePath(string title, string filter, string defaultFileName);

    IReadOnlyList<AutopilotProfileSettings>? PickAutopilotProfilesForImport(IReadOnlyList<AutopilotProfileSettings> availableProfiles);

    string? PickFolderPath(string title, string? initialPath = null);

    void OpenFolder(string path);

    void ShowMessage(string title, string message, MessageBoxImage image);

    bool ConfirmWarning(string title, string message);
}
