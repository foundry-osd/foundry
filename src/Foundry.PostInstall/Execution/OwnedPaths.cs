// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.PostInstall.Execution;

public static class OwnedPaths
{
    public static string Resolve(string root, string relative)
    {
        if (string.IsNullOrWhiteSpace(relative) || Path.IsPathRooted(relative) || relative.Contains(':'))
            throw new InvalidDataException("An owned path must be relative.");
        string[] segments = relative.Replace('/', '\\').Split('\\');
        if (segments.Any(segment => segment.Length == 0 || segment is "." or ".." ||
            segment.EndsWith(' ') || segment.EndsWith('.') || segment.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
            System.Text.RegularExpressions.Regex.IsMatch(segment.Split('.')[0], "^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])$",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant)))
            throw new InvalidDataException("An owned path is invalid.");
        string basePath = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar);
        string path = Path.GetFullPath(Path.Combine(basePath, Path.Combine(segments)));
        if (!path.StartsWith(basePath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("An owned path escapes its root.");
        RejectReparsePoints(basePath, path);
        return path;
    }

    public static void RejectReparsePoints(string root, string path)
    {
        string? current = path;
        while (current is not null && current.Length >= root.Length)
        {
            try
            {
                if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidDataException("An owned path contains a reparse point.");
            }
            catch (FileNotFoundException) { }
            catch (DirectoryNotFoundException) { }
            current = Path.GetDirectoryName(current);
        }
    }

    public static void DeleteDirectory(string path) => DeleteDirectory(path, 0);

    private static void DeleteDirectory(string path, int depth)
    {
        if (depth > 256) throw new InvalidDataException("Owned cleanup exceeded its directory depth limit.");
        if (!Directory.Exists(path)) return;
        RejectReparsePoints(path, path);
        foreach (string entry in Directory.EnumerateFileSystemEntries(path))
        {
            FileAttributes attributes = File.GetAttributes(entry);
            if ((attributes & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("Owned cleanup encountered a reparse point.");
            if ((attributes & FileAttributes.Directory) != 0) DeleteDirectory(entry, depth + 1);
            else { File.SetAttributes(entry, attributes & ~FileAttributes.ReadOnly); File.Delete(entry); }
        }
        Directory.Delete(path);
    }
}
