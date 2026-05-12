using System.IO;

namespace Foundry.Connect.Services.Runtime;

/// <summary>
/// Resolves Foundry.Connect configuration, log, and temporary paths for WinPE and desktop runtimes.
/// </summary>
internal static class ConnectWorkspacePaths
{
    private const string WinPeWorkspaceRoot = @"X:\Foundry";

    /// <summary>
    /// Gets a value indicating whether Foundry.Connect is running inside WinPE.
    /// </summary>
    public static bool IsWinPeRuntime()
    {
        return WinPeRuntimeDetector.IsWinPeRuntime();
    }

    /// <summary>
    /// Resolves a configuration file path from the WinPE workspace or application directory.
    /// </summary>
    /// <param name="fileName">Configuration file name.</param>
    /// <returns>The resolved configuration file path.</returns>
    public static string GetConfigFilePath(string fileName)
    {
        return IsWinPeRuntime()
            ? GetWinPeWorkspacePath("Config", fileName)
            : Path.Combine(AppContext.BaseDirectory, fileName);
    }

    /// <summary>
    /// Enumerates directories that may contain startup logs for the current runtime.
    /// </summary>
    /// <returns>Candidate log directories in preferred search order.</returns>
    public static IEnumerable<string> EnumerateStartupLogDirectories()
    {
        if (IsWinPeRuntime())
        {
            yield return GetWinPeWorkspacePath("Logs");
        }

        yield return Path.Combine(Path.GetTempPath(), "Foundry", "Logs");
        yield return AppContext.BaseDirectory;
    }

    /// <summary>
    /// Enumerates temporary directories suitable for application runtime files.
    /// </summary>
    /// <param name="applicationFolderName">Folder name used below the runtime temporary root.</param>
    /// <returns>Candidate temporary directories in preferred order.</returns>
    public static IEnumerable<string> EnumerateTemporaryDirectories(string applicationFolderName)
    {
        if (IsWinPeRuntime())
        {
            yield return GetWinPeWorkspacePath("Temp", applicationFolderName);
        }

        yield return Path.Combine(Path.GetTempPath(), applicationFolderName);
    }

    private static string GetWinPeWorkspacePath(params string[] relativeSegments)
    {
        string currentPath = WinPeWorkspaceRoot;
        foreach (string segment in relativeSegments)
        {
            currentPath = Path.Combine(currentPath, segment);
        }

        return currentPath;
    }
}
