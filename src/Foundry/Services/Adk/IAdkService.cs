namespace Foundry.Services.Adk;

/// <summary>
/// Service for managing Windows ADK installation and versioning.
/// </summary>
public interface IAdkService
{
    /// <summary>
    /// Gets a value indicating whether ADK is currently installed.
    /// </summary>
    bool IsAdkInstalled { get; }

    /// <summary>
    /// Gets a value indicating whether the installed ADK version is compatible with Windows 11 24H2.
    /// </summary>
    bool IsAdkCompatible { get; }

    /// <summary>
    /// Gets the currently installed ADK version, or null if not installed.
    /// </summary>
    string? InstalledVersion { get; }

    /// <summary>
    /// Gets a value indicating whether an ADK operation is currently in progress.
    /// </summary>
    bool IsOperationInProgress { get; }

    /// <summary>
    /// Gets the current operation progress (0-100).
    /// </summary>
    int OperationProgress { get; }

    /// <summary>
    /// Gets the current operation status message.
    /// </summary>
    string? OperationStatus { get; }

    /// <summary>
    /// Occurs when ADK installation status changes.
    /// </summary>
    event EventHandler? AdkStatusChanged;

    /// <summary>
    /// Occurs when operation progress changes.
    /// </summary>
    event EventHandler? OperationProgressChanged;

    /// <summary>
    /// Refreshes the ADK installation status.
    /// </summary>
    void RefreshStatus();

    /// <summary>
    /// Downloads the Windows 11 24H2-compatible ADK installer.
    /// </summary>
    /// <returns>A task representing the asynchronous download operation.</returns>
    Task DownloadAdkAsync();

    /// <summary>
    /// Installs the Windows ADK.
    /// </summary>
    /// <returns>A task representing the asynchronous install operation.</returns>
    Task InstallAdkAsync();

    /// <summary>
    /// Uninstalls the Windows ADK.
    /// </summary>
    /// <returns>A task representing the asynchronous uninstall operation.</returns>
    Task UninstallAdkAsync();

    /// <summary>
    /// Upgrades the Windows ADK (uninstalls current version and installs new version).
    /// </summary>
    /// <returns>A task representing the asynchronous upgrade operation.</returns>
    Task UpgradeAdkAsync();
}
