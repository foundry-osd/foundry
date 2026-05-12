namespace Foundry.Services.Settings;

/// <summary>
/// Loads and persists application-scoped settings for the WinUI shell.
/// </summary>
public interface IAppSettingsService
{
    /// <summary>
    /// Gets the current mutable settings document.
    /// </summary>
    FoundryAppSettings Current { get; }

    /// <summary>
    /// Gets a value indicating whether the settings file was missing during service initialization.
    /// </summary>
    bool IsFirstRun { get; }

    /// <summary>
    /// Persists the current settings document to disk.
    /// </summary>
    void Save();
}
