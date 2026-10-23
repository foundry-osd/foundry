// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Security.AccessControl;
using System.Security.Principal;

namespace Foundry.Utilities.IO;

/// <summary>
/// Creates owner-only directories and manages leased private directories below a dedicated root.
/// </summary>
/// <remarks>
/// A leased directory is named with a <c>N</c>-formatted GUID and holds an exclusive <c>.lease</c> file while its owner is alive.
/// The root holds a <c>.cleanup-lock</c> file that serializes creation, removal and abandoned cleanup across processes.
/// Every path is refused when it, or any of its ancestors, is a reparse point, so cleanup never follows a redirection.
/// </remarks>
public static class PrivateDirectory
{
    internal const string LeaseFileName = ".lease";
    private const string CleanupLockFileName = ".cleanup-lock";
    private const int MaximumCleanupCandidates = 1024;

    /// <summary>
    /// Creates a directory, or replaces the access rules of an existing one, so only the current user, SYSTEM and Administrators can access it.
    /// </summary>
    /// <remarks>Inheritance from the parent is removed and the rules are inherited by new children. Missing parents are created.</remarks>
    /// <param name="path">The directory to create or protect.</param>
    public static void CreateOwnerOnly(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        DirectorySecurity security = CreateOwnerOnlySecurity();
        var directory = new DirectoryInfo(Path.GetFullPath(path));
        if (directory.Exists)
        {
            // CreateDirectory leaves the access rules of an existing directory unchanged.
            directory.SetAccessControl(security);
        }
        else
        {
            security.CreateDirectory(directory.FullName);
        }
    }

    /// <summary>
    /// Throws when <paramref name="path"/> is outside <paramref name="boundary"/>, or when the path or any ancestor is a reparse point.
    /// </summary>
    /// <remarks>Missing path segments are accepted, so a path can be validated before it is created. Ancestors above the boundary are checked too.</remarks>
    /// <param name="path">The path to validate.</param>
    /// <param name="boundary">The directory that must contain <paramref name="path"/>, or be equal to it.</param>
    /// <exception cref="IOException">The path is outside the boundary or uses a redirected segment.</exception>
    public static void ThrowIfRedirected(string path, string boundary)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentException.ThrowIfNullOrWhiteSpace(boundary);
        string fullPath = Path.GetFullPath(path);
        if (!IsSameOrInside(fullPath, NormalizeRoot(boundary)))
            throw new IOException("The private directory path is outside its boundary.");

        for (string? current = fullPath; current is not null; current = Path.GetDirectoryName(current))
        {
            try
            {
                if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                    throw new IOException("A private directory cannot use a redirected path.");
            }
            catch (FileNotFoundException) { }
            catch (DirectoryNotFoundException) { }
        }
    }

    /// <summary>
    /// Removes abandoned directories below <paramref name="root"/>, then creates a new owner-only directory and holds its lease.
    /// </summary>
    /// <param name="root">The dedicated root of the leased directories; it is created when missing.</param>
    /// <returns>The lease of the new directory. Disposing it removes the directory.</returns>
    /// <exception cref="IOException">The root is redirected, or another process holds its cleanup lock.</exception>
    public static PrivateDirectoryLease CreateLeased(string root)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        string fullRoot = NormalizeRoot(root);
        using FileStream cleanupLock = AcquireCleanupLock(fullRoot);
        CleanupAbandonedCore(fullRoot);
        string path = Path.Combine(fullRoot, Guid.NewGuid().ToString("N"));
        CreateOwnerOnly(path);
        return new PrivateDirectoryLease(fullRoot, path, OpenLease(path, fullRoot));
    }

    /// <summary>
    /// Removes every unleased directory below <paramref name="root"/> whose name is a leased directory name; other entries are kept.
    /// </summary>
    /// <remarks>
    /// Leased, redirected or locked directories are skipped and remain eligible for a later cleanup. At most 1,024 root entries are examined per call.
    /// </remarks>
    /// <param name="root">The dedicated root of the leased directories.</param>
    /// <returns>The number of directories removed.</returns>
    /// <exception cref="IOException">The root is redirected, or another process holds its cleanup lock.</exception>
    public static int CleanupAbandoned(string root)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        string fullRoot = NormalizeRoot(root);
        if (!Directory.Exists(fullRoot)) return 0;
        using FileStream cleanupLock = AcquireCleanupLock(fullRoot);
        return CleanupAbandonedCore(fullRoot);
    }

    internal static FileStream AcquireCleanupLock(string root)
    {
        ThrowIfRedirected(root, root);
        Directory.CreateDirectory(root);
        string path = Path.Combine(root, CleanupLockFileName);
        ThrowIfRedirected(path, root);
        return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
    }

    internal static FileStream OpenLease(string directory, string root)
    {
        ValidateLeasedDirectory(directory, root);
        string path = Path.Combine(directory, LeaseFileName);
        ThrowIfRedirected(path, directory);
        return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
    }

    /// <summary>Deletes every file and subdirectory of a leased directory except its lease file, refusing redirected entries.</summary>
    internal static void DeleteContents(string directory)
    {
        Stack<string> pending = new();
        List<string> directories = [];
        pending.Push(directory);
        while (pending.TryPop(out string? current))
        {
            ThrowIfRedirected(current, directory);
            directories.Add(current);
            foreach (string path in Directory.EnumerateFileSystemEntries(current))
            {
                if (current == directory && Path.GetFileName(path) == LeaseFileName) continue;
                ThrowIfRedirected(path, directory);
                if ((File.GetAttributes(path) & FileAttributes.Directory) != 0) pending.Push(path);
                else File.Delete(path);
            }
        }
        for (int index = directories.Count - 1; index > 0; index--)
            Directory.Delete(directories[index], recursive: false);
    }

    /// <summary>Deletes the lease file and the empty directory; the caller must have released the lease handle.</summary>
    internal static void RemoveEmpty(string directory)
    {
        File.Delete(Path.Combine(directory, LeaseFileName));
        Directory.Delete(directory, recursive: false);
    }

    private static int CleanupAbandonedCore(string root)
    {
        int removed = 0;
        foreach (string directory in Directory.EnumerateDirectories(root).Take(MaximumCleanupCandidates))
        {
            if (!Guid.TryParseExact(Path.GetFileName(directory), "N", out _)) continue;
            try
            {
                using (FileStream lease = OpenLease(directory, root))
                {
                    DeleteContents(directory);
                }
                RemoveEmpty(directory);
                removed++;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // A live owner holds the lease, or the directory cannot be removed now; a later cleanup retries it.
            }
        }
        return removed;
    }

    private static void ValidateLeasedDirectory(string directory, string root)
    {
        string fullPath = Path.GetFullPath(directory);
        if (!string.Equals(Path.GetDirectoryName(fullPath), root, StringComparison.OrdinalIgnoreCase) ||
            !Guid.TryParseExact(Path.GetFileName(fullPath), "N", out _))
            throw new IOException("The private directory is outside its owned root.");
        ThrowIfRedirected(fullPath, root);
    }

    private static DirectorySecurity CreateOwnerOnlySecurity()
    {
        using WindowsIdentity identity = WindowsIdentity.GetCurrent();
        var security = new DirectorySecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        foreach (SecurityIdentifier principal in new[]
        {
            identity.User ?? throw new InvalidOperationException("The current Windows user is unavailable."),
            new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
            new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null)
        })
        {
            security.AddAccessRule(new FileSystemAccessRule(principal, FileSystemRights.FullControl,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
        }
        return security;
    }

    private static string NormalizeRoot(string path) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));

    private static bool IsSameOrInside(string path, string boundary)
    {
        if (string.Equals(Path.TrimEndingDirectorySeparator(path), boundary, StringComparison.OrdinalIgnoreCase)) return true;
        string prefix = Path.EndsInDirectorySeparator(boundary) ? boundary : boundary + Path.DirectorySeparatorChar;
        return path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
    }
}
