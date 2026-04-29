using Foundry.Models.Configuration;
using Foundry.Services.ApplicationUpdate;

namespace Foundry.Services.ApplicationShell;

public enum ApplicationMessageKind
{
    Information,
    Warning,
    Error
}

public interface IApplicationShellService
{
    void Shutdown();

    void ShowAbout();

    void ShowUpdateAvailable(ApplicationUpdateInfo updateInfo);

    void OpenUrl(string url);

    Task<string?> PickIsoOutputPathAsync(string defaultFileName);

    Task<string?> PickOpenFilePathAsync(string title, string filter);

    Task<string?> PickSaveFilePathAsync(string title, string filter, string defaultFileName);

    Task<IReadOnlyList<AutopilotProfileSettings>?> PickAutopilotProfilesForImportAsync(IReadOnlyList<AutopilotProfileSettings> availableProfiles);

    Task<string?> PickFolderPathAsync(string title, string? initialPath = null);

    void OpenFolder(string path);

    void ShowMessage(string title, string message, ApplicationMessageKind kind);

    bool ConfirmWarning(string title, string message);
}
