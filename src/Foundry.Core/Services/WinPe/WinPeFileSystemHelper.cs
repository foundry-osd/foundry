namespace Foundry.Core.Services.WinPe;

internal static class WinPeFileSystemHelper
{
    public static void EnsureDirectoryClean(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }

        Directory.CreateDirectory(path);
    }

    public static string SanitizePathSegment(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "item";
        }

        char[] invalidChars = Path.GetInvalidFileNameChars();
        var result = value.Trim();

        foreach (char invalid in invalidChars)
        {
            result = result.Replace(invalid, '_');
        }

        return result.Replace(' ', '_');
    }

    public static bool ContainsFileRecursive(string rootPath, string searchPattern)
    {
        return Directory.EnumerateFiles(rootPath, searchPattern, SearchOption.AllDirectories).Any();
    }
}
