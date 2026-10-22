// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Core.Services.Packages;

/// <summary>Limits imported and staged paths to ordinary Windows files, using the length limits of one <see cref="ContentPackageKind"/>.</summary>
public sealed class ContentPackagePathPolicy
{
    private readonly ContentPackageKind kind;

    /// <summary>Creates a policy that applies the path length limits of <paramref name="kind"/>.</summary>
    public ContentPackagePathPolicy(ContentPackageKind kind)
    {
        ArgumentNullException.ThrowIfNull(kind);
        this.kind = kind;
    }

    /// <summary>Policy for post-installation packages, whose short limits suit legacy installer tools.</summary>
    public static ContentPackagePathPolicy PostInstallation { get; } = new(ContentPackageKind.PostInstallation);

    public static bool IsValidHash(string? hash) => hash is { Length: 64 } && hash.All(Uri.IsHexDigit);

    /// <summary>Validates path syntax and the kind's relative path length.</summary>
    public void ValidateRelativePath(string? path)
    {
        if (path?.Length > kind.MaximumRelativePathLength)
            throw new InvalidDataException("ContentPackage.InvalidPackagePath");
        ValidatePathSyntax(path);
    }

    /// <summary>Validates path syntax independently of any kind limit, so canonical serialization stays kind-neutral.</summary>
    internal static void ValidatePathSyntax(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || Path.IsPathRooted(path) || path.Contains('\\'))
            throw new InvalidDataException("ContentPackage.InvalidPackagePath");
        foreach (string component in path.Split('/'))
        {
            if (component.Length is 0 or > 255 || component is "." or ".." || component.EndsWith('.') || component.EndsWith(' ') ||
                component.Any(character => character < 32 || "<>:\"|?*".Contains(character)))
                throw new InvalidDataException("ContentPackage.InvalidPackagePath");
            string basename = component.Split('.')[0];
            if (basename.Equals("CON", StringComparison.OrdinalIgnoreCase) || basename.Equals("PRN", StringComparison.OrdinalIgnoreCase) ||
                basename.Equals("AUX", StringComparison.OrdinalIgnoreCase) || basename.Equals("NUL", StringComparison.OrdinalIgnoreCase) ||
                basename.Equals("CONIN$", StringComparison.OrdinalIgnoreCase) || basename.Equals("CONOUT$", StringComparison.OrdinalIgnoreCase) ||
                (basename.Length == 4 && (basename.StartsWith("COM", StringComparison.OrdinalIgnoreCase) || basename.StartsWith("LPT", StringComparison.OrdinalIgnoreCase)) &&
                    "123456789¹²³".Contains(basename[3])))
                throw new InvalidDataException("ContentPackage.InvalidPackagePath");
        }
    }

    /// <summary>Checks the actual destination again, because a valid relative path may exceed a longer host prefix.</summary>
    public string Resolve(string root, string relativePath)
    {
        string resolved = ResolveLexically(root, relativePath);
        ValidateNoReparsePoints(resolved);
        return resolved;
    }

    /// <summary>Validates future path layout and length without inspecting the filesystem.</summary>
    /// <remarks>Use <see cref="Resolve"/> for actual source and staging paths so reparse points are still rejected.</remarks>
    public string ResolveLexically(string root, string relativePath)
    {
        ValidateRelativePath(relativePath);
        string fullRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        string resolved = Path.GetFullPath(Path.Combine(fullRoot, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        if (!resolved.StartsWith(fullRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || resolved.Length > kind.MaximumFullPathLength)
            throw new InvalidDataException("ContentPackage.InvalidPackagePath");
        return resolved;
    }

    public static void ValidateNoReparsePoints(string path)
    {
        for (string? current = Path.GetFullPath(path); current is not null; current = Path.GetDirectoryName(current))
        {
            try
            {
                if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidDataException("ContentPackage.RedirectedPackagePath");
            }
            catch (FileNotFoundException) { }
            catch (DirectoryNotFoundException) { }
        }
    }
}
