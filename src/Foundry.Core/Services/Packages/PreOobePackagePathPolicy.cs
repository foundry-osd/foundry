// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Core.Services.Packages;

/// <summary>Limits imported and staged paths to ordinary Windows files usable by legacy installer tools.</summary>
public static class PreOobePackagePathPolicy
{
    public const int MaximumFullPathLength = 259;
    // Covers the longest documented target prefix, including an operation ID and SHA-256 directory.
    public const int MaximumRelativePathLength = 110;

    public static bool IsValidHash(string? hash) => hash is { Length: 64 } && hash.All(Uri.IsHexDigit);

    public static void ValidateRelativePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Length > MaximumRelativePathLength || Path.IsPathRooted(path) || path.Contains('\\'))
            throw new InvalidDataException("PreOobe.InvalidPackagePath");
        foreach (string component in path.Split('/'))
        {
            if (component.Length is 0 or > 255 || component is "." or ".." || component.EndsWith('.') || component.EndsWith(' ') ||
                component.Any(character => character < 32 || "<>:\"|?*".Contains(character)))
                throw new InvalidDataException("PreOobe.InvalidPackagePath");
            string basename = component.Split('.')[0];
            if (basename.Equals("CON", StringComparison.OrdinalIgnoreCase) || basename.Equals("PRN", StringComparison.OrdinalIgnoreCase) ||
                basename.Equals("AUX", StringComparison.OrdinalIgnoreCase) || basename.Equals("NUL", StringComparison.OrdinalIgnoreCase) ||
                basename.Equals("CONIN$", StringComparison.OrdinalIgnoreCase) || basename.Equals("CONOUT$", StringComparison.OrdinalIgnoreCase) ||
                (basename.Length == 4 && (basename.StartsWith("COM", StringComparison.OrdinalIgnoreCase) || basename.StartsWith("LPT", StringComparison.OrdinalIgnoreCase)) &&
                    "123456789¹²³".Contains(basename[3])))
                throw new InvalidDataException("PreOobe.InvalidPackagePath");
        }
    }

    /// <summary>Checks the actual destination again, because a valid relative path may exceed a longer host prefix.</summary>
    public static string Resolve(string root, string relativePath)
    {
        string resolved = ResolveLexically(root, relativePath);
        ValidateNoReparsePoints(resolved);
        return resolved;
    }

    /// <summary>Validates future path layout and length without inspecting the filesystem.</summary>
    /// <remarks>Use <see cref="Resolve"/> for actual source and staging paths so reparse points are still rejected.</remarks>
    public static string ResolveLexically(string root, string relativePath)
    {
        ValidateRelativePath(relativePath);
        string fullRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        string resolved = Path.GetFullPath(Path.Combine(fullRoot, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        if (!resolved.StartsWith(fullRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || resolved.Length > MaximumFullPathLength)
            throw new InvalidDataException("PreOobe.InvalidPackagePath");
        return resolved;
    }

    public static void ValidateNoReparsePoints(string path)
    {
        for (string? current = Path.GetFullPath(path); current is not null; current = Path.GetDirectoryName(current))
        {
            try
            {
                if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidDataException("PreOobe.RedirectedPackagePath");
            }
            catch (FileNotFoundException) { }
            catch (DirectoryNotFoundException) { }
        }
    }
}
