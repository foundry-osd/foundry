// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Utilities.Diagnostics;

/// <summary>Creates a source from an application-owned root and explicit relative path.</summary>
public static class SupportBundleSourcePolicy
{
    /// <summary>Validates ownership without enumerating sources or requiring optional files to exist.</summary>
    public static SupportBundleSource CreateOwned(string root, string relativePath, string archiveName, SupportBundleSourceFormat format, bool required = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);
        if (!Path.IsPathFullyQualified(root) || Path.IsPathRooted(relativePath) || relativePath.Split('/', '\\').Any(IsUnsafeSegment))
        { throw new ArgumentException("Diagnostic source must be contained in its owned root."); }
        string fullRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        string path = Path.GetFullPath(Path.Combine(fullRoot, relativePath));
        if (!path.StartsWith(fullRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        { throw new ArgumentException("Diagnostic source must be contained in its owned root."); }
        RejectReparseChain(path);
        return new(path, archiveName, format, required);
    }

    private static bool IsUnsafeSegment(string segment) => segment is ".." or "." or "" ||
        segment.EndsWith('.') || segment.EndsWith(' ') || segment.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0;

    /// <summary>Selects a bounded top-level inventory of one canonical log and its numeric rolls.</summary>
    public static IReadOnlyList<SupportBundleSource> CreateRollingLogs(string root, string fileName, string archivePrefix,
        SupportBundleSourceFormat format = SupportBundleSourceFormat.ApplicationText, int maximumFiles = 5)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumFiles, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(maximumFiles, 5);
        if (fileName != Path.GetFileName(fileName) || IsUnsafeSegment(fileName) ||
            string.IsNullOrEmpty(archivePrefix) || archivePrefix.Length > 64 || archivePrefix.Any(static character => !char.IsAsciiLetterOrDigit(character) && character is not ('-' or '_')))
        { throw new ArgumentException("Diagnostic log names must be fixed safe leaf names."); }
        string extension = Path.GetExtension(fileName);
        string stem = Path.GetFileNameWithoutExtension(fileName);
        var canonical = CreateOwned(root, fileName, archivePrefix + extension, format);
        canonical = canonical with { Required = File.Exists(canonical.Path) };
        var result = new List<SupportBundleSource> { canonical };
        if (maximumFiles == 1 || !Directory.Exists(root)) { return result; }
        var rolls = new List<(SupportBundleSource Source, DateTime Modified)>();
        foreach (string path in Directory.EnumerateFiles(root, "*", SearchOption.TopDirectoryOnly))
        {
            string name = Path.GetFileName(path);
            if (!name.EndsWith(extension, StringComparison.OrdinalIgnoreCase)) { continue; }
            string candidateStem = Path.GetFileNameWithoutExtension(name);
            if (!candidateStem.StartsWith(stem, StringComparison.OrdinalIgnoreCase)) { continue; }
            string suffix = candidateStem[stem.Length..];
            if (suffix.Length < 2 || suffix[0] is not ('_' or '.') || !suffix.Skip(1).All(char.IsAsciiDigit)) { continue; }
            var source = CreateOwned(root, name, archivePrefix + "-roll" + extension, format);
            rolls.Add((source, File.GetLastWriteTimeUtc(source.Path)));
            rolls.Sort(static (left, right) =>
            {
                int modified = right.Modified.CompareTo(left.Modified);
                return modified != 0 ? modified : StringComparer.OrdinalIgnoreCase.Compare(left.Source.Path, right.Source.Path);
            });
            if (rolls.Count >= maximumFiles) { rolls.RemoveAt(rolls.Count - 1); }
        }
        for (int index = 0; index < rolls.Count; index++)
        { result.Add(rolls[index].Source with { ArchiveName = $"{archivePrefix}-roll-{index + 1}{extension}" }); }
        return result;
    }

    /// <summary>Rejects existing reparse ancestors without following directory trees.</summary>
    internal static void RejectReparseChain(string path, Func<string, FileAttributes>? readAttributes = null)
    {
        if (!Path.IsPathFullyQualified(path)) { throw new IOException("Diagnostic source path must be absolute."); }
        string relative = path[(Path.GetPathRoot(path)?.Length ?? 0)..];
        if (relative.Split('/', '\\').Any(IsUnsafeSegment)) { throw new IOException("Diagnostic source path is unsupported."); }
        string? current = Path.GetFullPath(path);
        while (current is not null)
        {
            try
            {
                if (((readAttributes ?? File.GetAttributes)(current) & FileAttributes.ReparsePoint) != 0) { throw new IOException("Diagnostic source crosses a reparse point."); }
            }
            catch (FileNotFoundException) { }
            catch (DirectoryNotFoundException) { }
            current = Path.GetDirectoryName(current);
        }
    }
}
