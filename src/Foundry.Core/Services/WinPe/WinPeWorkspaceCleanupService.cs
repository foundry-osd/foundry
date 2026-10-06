// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Core.Services.WinPe;

/// <summary>Preserves workspaces until mounted-image state permits safe deletion.</summary>
public sealed class WinPeWorkspaceCleanupService
{
    /// <summary>
    /// Blocks new servicing when an owned operation retains pending cleanup.
    /// Inspects only operation roots and their WinPe directories, without acquiring leases or walking mounts.
    /// </summary>
    public WinPeResult EnsureServicingCanStart(string workspaceRoot)
    {
        try
        {
            string root = NormalizePath(workspaceRoot);
            for (string? ancestor = root; ancestor is not null; ancestor = Path.GetDirectoryName(ancestor))
            {
                FileAttributes? attributes = GetExistingAttributes(ancestor);
                if (attributes?.HasFlag(FileAttributes.ReparsePoint) == true)
                    throw new IOException($"Workspace inspection cannot follow a reparse point: '{ancestor}'.");
            }
            if (GetExistingAttributes(root) is null) return WinPeResult.Success();

            foreach (string candidate in Directory.EnumerateDirectories(root))
            {
                string path = NormalizePath(candidate);
                if (!string.Equals(Path.GetDirectoryName(path), root, StringComparison.OrdinalIgnoreCase))
                    throw new IOException("The operation is not an immediate child of the workspace root.");
                if (!Guid.TryParseExact(Path.GetFileName(path), "N", out _)) continue;
                if (File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint))
                    throw new IOException($"Operation inspection cannot follow a reparse point: '{path}'.");

                string ownershipPath = Path.Combine(path, WinPeWorkspaceLease.OwnershipFileName);
                FileAttributes? ownershipAttributes = GetExistingAttributes(ownershipPath);
                if (ownershipAttributes is null) continue;
                if (ownershipAttributes.Value.HasFlag(FileAttributes.ReparsePoint))
                    throw new IOException($"Operation ownership cannot follow a reparse point: '{ownershipPath}'.");
                if (!WinPeWorkspaceLease.IsOwned(path)) continue;

                foreach (string directory in new[] { path, Path.Combine(path, "WinPe") })
                {
                    FileAttributes? attributes = GetExistingAttributes(directory);
                    if (attributes is null) continue;
                    if (attributes.Value.HasFlag(FileAttributes.ReparsePoint) || !attributes.Value.HasFlag(FileAttributes.Directory))
                        throw new IOException($"Owned workspace state cannot be safely inspected: '{directory}'.");
                    string? marker = Directory.EnumerateFileSystemEntries(directory, WinPeMountSession.CleanupMarkerPattern,
                        SearchOption.TopDirectoryOnly).FirstOrDefault();
                    if (marker is not null)
                    {
                        return WinPeResult.Failure(new WinPeDiagnostic(WinPeErrorCodes.WimUnmountFailed,
                            "WinPE servicing is blocked because an earlier image cleanup has no confirmed completion.",
                            $"Retained operation: '{path}'. Cleanup marker: '{marker}'. Preserve this workspace until cleanup can be verified.",
                            stage: "Check retained WinPE cleanup") with
                        { MountCleanupStatus = WinPeMountCleanupStatus.ExitUnconfirmed });
                    }
                }
            }
            return WinPeResult.Success();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Text.Json.JsonException or ArgumentException)
        {
            return WinPeResult.Failure(new WinPeDiagnostic(WinPeErrorCodes.WimUnmountFailed,
                "WinPE servicing is blocked because retained operation cleanup could not be safely inspected.",
                $"Workspace root: '{workspaceRoot}'. {exception.Message}",
                stage: "Check retained WinPE cleanup", exception: exception));
        }
    }

    /// <summary>
    /// Identifies directories created by <see cref="WinPeWorkspaceLease"/> so stale-operation recovery can skip
    /// unrelated or legacy folders under the workspace root without treating them as failed cleanups.
    /// </summary>
    /// <remarks>
    /// Only the GUID name and the presence of a lease or ownership entry are inspected; ownership itself is still
    /// verified by <see cref="DeleteOwnedOperation"/>. When the probe cannot complete, the directory is reported as an
    /// operation so recovery surfaces the failure instead of silently ignoring possibly retained state.
    /// </remarks>
    public static bool IsOperationWorkspace(string workspacePath)
    {
        string name = Path.GetFileName(Path.TrimEndingDirectorySeparator(workspacePath));
        if (!Guid.TryParseExact(name, "N", out _)) return false;
        try
        {
            return GetExistingAttributes(Path.Combine(workspacePath, WinPeWorkspaceLease.LeaseFileName)) is not null ||
                GetExistingAttributes(Path.Combine(workspacePath, WinPeWorkspaceLease.OwnershipFileName)) is not null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return true;
        }
    }

    /// <summary>Recovers only an identified inactive operation directly under the designated root.</summary>
    public WinPeResult DeleteOwnedOperation(string workspaceRoot, string operationPath)
    {
        try
        {
            string root = NormalizePath(workspaceRoot);
            string path = NormalizePath(operationPath);
            if (!string.Equals(Path.GetDirectoryName(path), root, StringComparison.OrdinalIgnoreCase))
                throw new IOException("The operation is not an immediate child of the workspace root.");
            if (!Directory.Exists(path)) return WinPeResult.Success();
            string leasePath = Path.Combine(path, WinPeWorkspaceLease.LeaseFileName);
            string ownershipPath = Path.Combine(path, WinPeWorkspaceLease.OwnershipFileName);
            foreach (string candidate in new[] { root, path, leasePath, ownershipPath })
                if (File.GetAttributes(candidate).HasFlag(FileAttributes.ReparsePoint))
                    throw new IOException("Operation recovery cannot follow reparse points.");

            // Keep exclusive read/write ownership during deletion. Delete sharing allows our
            // own recursive cleanup while a concurrent recovery attempt cannot acquire the lease.
            using var recoveryLease = new FileStream(leasePath, FileMode.Open, FileAccess.ReadWrite, FileShare.Delete);
            if (!WinPeWorkspaceLease.IsOwned(path))
                throw new IOException("The directory has no recognized Foundry operation ownership.");
            return Delete(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            return WinPeResult.Failure(WinPeErrorCodes.WimUnmountFailed,
                "Workspace retained because ownership or inactivity could not be established.",
                $"Workspace: '{operationPath}'. {exception.Message}");
        }
    }

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

            // New source candidates must honor unresolved cleanup in their existing ancestors.
            for (string? ancestor = directory ? path : Path.GetDirectoryName(path); ancestor is not null;
                ancestor = Path.GetDirectoryName(ancestor))
            {
                if (Directory.Exists(ancestor) && Directory.EnumerateFiles(ancestor, WinPeMountSession.CleanupMarkerPattern).Any())
                    throw new IOException($"An earlier image cleanup has no confirmed completion. Preserve workspace '{path}' and inspect its cleanup marker in '{ancestor}'.");
            }
            if (!directory && !File.Exists(path)) return WinPeResult.Success();

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

    private static FileAttributes? GetExistingAttributes(string path)
    {
        try
        {
            return File.GetAttributes(path);
        }
        catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException)
        {
            return null;
        }
    }

    private static bool IsWithin(string path, string parent) =>
        string.Equals(path, parent, StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith(Path.EndsInDirectorySeparator(parent) ? parent : parent + Path.DirectorySeparatorChar,
            StringComparison.OrdinalIgnoreCase);
}
