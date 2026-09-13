// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Core.Services.Profiles;

/// <summary>Resolves a configuration's dedicated shared folder without changing names or accessing storage.</summary>
public static class SharedProfileLocation
{
    /// <summary>Accepts human-readable Windows folder names while rejecting ambiguous or reserved device names.</summary>
    public static bool IsValidName(string? name)
    {
        if (name is null || name.Length > 120 || !IsValidSegment(name)) return false;
        string stem = name.Split('.')[0].TrimEnd(' ').ToUpperInvariant();
        if (stem is "CON" or "PRN" or "AUX" or "NUL") return false;
        if (stem.Length == 4 && (stem.StartsWith("COM", StringComparison.Ordinal) || stem.StartsWith("LPT", StringComparison.Ordinal)) &&
            stem[3] is >= '1' and <= '9' or '¹' or '²' or '³') return false;
        return true;
    }

    /// <summary>Appends Foundry and the validated name beneath a standard UNC parent; existing contents are not inspected.</summary>
    public static string Resolve(string parentPath, string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(parentPath);
        if (!IsValidName(name)) throw new ArgumentException("Enter a valid shared configuration folder name of at most 120 characters.", nameof(name));
        return Path.Combine(NormalizeParent(parentPath), "Foundry", name);
    }

    /// <summary>Uses the selected file's server alias only for the same share and relative folder; backups retain the embedded location.</summary>
    public static string ResolveConnectionFolder(string embeddedFolder, string? sourcePath)
    {
        string embedded = NormalizeParent(embeddedFolder);
        if (string.IsNullOrWhiteSpace(sourcePath)) return embeddedFolder;
        try
        {
            string normalizedSource = sourcePath.Replace('/', '\\');
            if (!IsValidSegment(Path.GetFileName(normalizedSource))) return embeddedFolder;
            string sourceFolder = NormalizeParent(Path.GetDirectoryName(normalizedSource)!);
            bool sameLocation = embedded[2..].Split('\\').Skip(1)
                .SequenceEqual(sourceFolder[2..].Split('\\').Skip(1), StringComparer.OrdinalIgnoreCase);
            return sameLocation ? sourceFolder : embeddedFolder;
        }
        catch (ArgumentException)
        {
            return embeddedFolder;
        }
    }

    private static string NormalizeParent(string parentPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(parentPath);
        string parent = parentPath.Replace('/', '\\').TrimEnd('\\');
        if (!parent.StartsWith(@"\\", StringComparison.Ordinal) || parent.StartsWith(@"\\?", StringComparison.Ordinal) ||
            parent.StartsWith(@"\\.", StringComparison.Ordinal) || !Path.IsPathFullyQualified(parent))
            throw new ArgumentException("Select a standard UNC shared folder with a server and share name.", nameof(parentPath));
        string[] segments = parent[2..].Split('\\');
        if (segments.Length < 2 || segments.Any(segment => !IsValidSegment(segment)))
            throw new ArgumentException("The shared parent folder contains an invalid or traversing path segment.", nameof(parentPath));
        return parent;
    }

    private static bool IsValidSegment(string value) =>
        !string.IsNullOrWhiteSpace(value) && value is not "." and not ".." && value[^1] is not '.' and not ' ' &&
        value.IndexOfAny(Path.GetInvalidFileNameChars()) < 0 && !value.Any(char.IsControl);
}
