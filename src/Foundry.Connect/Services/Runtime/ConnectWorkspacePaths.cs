using System.IO;

namespace Foundry.Connect.Services.Runtime;

internal static class ConnectWorkspacePaths
{
    private const string WinPeWorkspaceRoot = @"X:\Foundry";

    public static bool IsWinPeRuntime()
    {
        return WinPeRuntimeDetector.IsWinPeRuntime();
    }

    public static string GetConfigFilePath(string fileName)
    {
        return IsWinPeRuntime()
            ? GetWinPeWorkspacePath("Config", fileName)
            : Path.Combine(AppContext.BaseDirectory, fileName);
    }

    public static IEnumerable<string> EnumerateStartupLogDirectories()
    {
        if (IsWinPeRuntime())
        {
            yield return GetWinPeWorkspacePath("Logs");
        }

        yield return Path.Combine(Path.GetTempPath(), "Foundry", "Logs");
        yield return AppContext.BaseDirectory;
    }

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
