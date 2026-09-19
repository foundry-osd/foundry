// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Core.Services.WinPe;

/// <summary>Preserves workspaces until mounted-image state permits safe deletion.</summary>
public sealed class WinPeWorkspaceCleanupService
{
    private readonly Func<IReadOnlyList<WinPeMountedImage>> _getMountedImages;

    public WinPeWorkspaceCleanupService() : this(NativeWinPeMountedImageInventory.GetMountedImages)
    {
    }

    internal WinPeWorkspaceCleanupService(Func<IReadOnlyList<WinPeMountedImage>> getMountedImages)
    {
        _getMountedImages = getMountedImages;
    }

    public WinPeResult Delete(string workspacePath)
    {
        try
        {
            string path = NormalizePath(workspacePath);
            if (Path.GetDirectoryName(path) is null)
                throw new IOException("A filesystem root cannot be deleted as a WinPE workspace.");
            bool directory = Directory.Exists(path);
            if (!directory && !File.Exists(path)) return WinPeResult.Success();

            for (string? ancestor = directory ? path : Path.GetDirectoryName(path); ancestor is not null;
                ancestor = Path.GetDirectoryName(ancestor))
            {
                if (Directory.EnumerateFiles(ancestor, WinPeMountSession.CleanupMarkerPattern).Any())
                    throw new IOException($"An earlier image cleanup has no confirmed completion. Preserve workspace '{path}' and inspect its cleanup marker in '{ancestor}'.");
            }

            foreach (WinPeMountedImage image in _getMountedImages())
            {
                string mountPath = NormalizePath(image.MountPath);
                string imagePath = NormalizePath(image.ImagePath);
                if (IsWithin(mountPath, path) || IsWithin(path, mountPath) || IsWithin(imagePath, path))
                {
                    return WinPeResult.Failure(WinPeErrorCodes.WimUnmountFailed,
                        "WinPE workspace retained because an image is still registered as mounted.",
                        $"Workspace: '{path}'. Mount: '{mountPath}'. Image: '{imagePath}'.");
                }
            }

            // Inspect without following links before changing any attributes in the workspace.
            var entries = new List<string> { path };
            for (int index = 0; index < entries.Count; index++)
            {
                string entry = entries[index];
                if (Path.GetFileName(entry).StartsWith(".foundry-mount-cleanup-", StringComparison.OrdinalIgnoreCase) &&
                    entry.EndsWith(".pending", StringComparison.OrdinalIgnoreCase))
                    throw new IOException($"An earlier image cleanup has no confirmed completion. Preserve its marker: '{entry}'.");
                FileAttributes attributes = File.GetAttributes(entry);
                if (attributes.HasFlag(FileAttributes.ReparsePoint))
                    throw new IOException($"Workspace cleanup cannot follow a reparse point: '{entry}'.");
                if (attributes.HasFlag(FileAttributes.Directory))
                    entries.AddRange(Directory.EnumerateFileSystemEntries(entry));
            }

            foreach (string entry in entries)
            {
                FileAttributes attributes = File.GetAttributes(entry);
                if (attributes.HasFlag(FileAttributes.ReadOnly))
                    File.SetAttributes(entry, attributes & ~FileAttributes.ReadOnly);
            }
            if (directory) Directory.Delete(path, recursive: true);
            else File.Delete(path);
            return WinPeResult.Success();
        }
        catch (Exception exception)
        {
            return WinPeResult.Failure(new WinPeDiagnostic(WinPeErrorCodes.WimUnmountFailed,
                "WinPE workspace cleanup could not establish or complete safe deletion.",
                $"Workspace: '{workspacePath}'. {exception.Message}", exception: exception));
        }
    }

    private static string NormalizePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path))
            throw new IOException("Mounted-image and workspace paths must use fully qualified standard filesystem paths.");
        string fullPath = Path.GetFullPath(path);
        if (fullPath.StartsWith(@"\\?\", StringComparison.Ordinal) || fullPath.StartsWith(@"\\.\", StringComparison.Ordinal))
            throw new IOException("Mounted-image and workspace paths must use fully qualified standard filesystem paths.");
        return Path.TrimEndingDirectorySeparator(fullPath);
    }

    private static bool IsWithin(string path, string parent) =>
        string.Equals(path, parent, StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith(Path.EndsInDirectorySeparator(parent) ? parent : parent + Path.DirectorySeparatorChar,
            StringComparison.OrdinalIgnoreCase);
}
