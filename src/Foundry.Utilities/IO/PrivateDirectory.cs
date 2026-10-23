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
    /// <summary>The default bound on entries one directory may hold before cleanup refuses to delete it.</summary>
    public const int DefaultMaximumEntries = 10_000;
    private const string LeaseFileName = ".lease";
    private const string CleanupLockFileName = ".cleanup-lock";
    private const int MaximumCleanupCandidates = 1024;

    /// <summary>
    /// Creates a directory, or replaces the access rules of an existing one, so only the current user has access.
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
    /// <remarks>The result of the abandoned cleanup is exposed by <see cref="PrivateDirectoryLease.AbandonedCleanup"/>.</remarks>
    /// <param name="root">The dedicated root of the leased directories; it is created when missing.</param>
    /// <param name="maximumEntries">The most entries one directory may hold before cleanup refuses to delete it, for this call and for the lease.</param>
    /// <returns>The lease of the new directory. Disposing it removes the directory.</returns>
    /// <exception cref="IOException">The root is redirected, or another process holds its cleanup lock.</exception>
    public static PrivateDirectoryLease CreateLeased(string root, int maximumEntries = DefaultMaximumEntries)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumEntries);
        string fullRoot = NormalizeRoot(root);
        using FileStream cleanupLock = AcquireCleanupLock(fullRoot);
        PrivateDirectoryCleanupResult cleanup = CleanupAbandonedCore(fullRoot, maximumEntries);
        string path = Path.Combine(fullRoot, Guid.NewGuid().ToString("N"));
        CreateOwnerOnly(path);
        FileStream lease;
        try
        {
            lease = OpenLease(path, fullRoot);
        }
        catch
        {
            TryDeleteEmpty(path);
            throw;
        }
        return new PrivateDirectoryLease(fullRoot, path, lease, maximumEntries, cleanup);
    }

    /// <summary>
    /// Removes every unleased directory below <paramref name="root"/> whose name is a leased directory name; other entries are kept.
    /// </summary>
    /// <remarks>
    /// Leased directories are skipped without being counted. Redirected, oversized or undeletable directories are counted as failed
    /// and remain eligible for a later cleanup. At most 1,024 root entries are examined per call.
    /// </remarks>
    /// <param name="root">The dedicated root of the leased directories.</param>
    /// <param name="maximumEntries">The most entries one directory may hold before cleanup refuses to delete it.</param>
    /// <returns>The number of directories removed and the number that could not be removed.</returns>
    /// <exception cref="IOException">The root is redirected, or another process holds its cleanup lock.</exception>
    public static PrivateDirectoryCleanupResult CleanupAbandoned(string root, int maximumEntries = DefaultMaximumEntries)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumEntries);
        string fullRoot = NormalizeRoot(root);
        if (!Directory.Exists(fullRoot)) return new(0, 0);
        using FileStream cleanupLock = AcquireCleanupLock(fullRoot);
        return CleanupAbandonedCore(fullRoot, maximumEntries);
    }

    /// <summary>Validates the root, creates it when missing, and opens its exclusive <c>.cleanup-lock</c> file.</summary>
    /// <exception cref="IOException">The root is redirected, or another process holds the lock.</exception>
    internal static FileStream AcquireCleanupLock(string root)
    {
        ThrowIfRedirected(root, root);
        Directory.CreateDirectory(root);
        string path = Path.Combine(root, CleanupLockFileName);
        ThrowIfRedirected(path, root);
        return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
    }

    /// <summary>
    /// Validates a leased directory directly below <paramref name="root"/> and opens its exclusive <c>.lease</c> file, creating it when missing.
    /// </summary>
    /// <exception cref="IOException">The directory is not an owned leased directory, is redirected, or its lease is held.</exception>
    internal static FileStream OpenLease(string directory, string root)
    {
        ValidateLeasedDirectory(directory, root);
        return OpenLeaseFile(directory);
    }

    /// <summary>Deletes every file and subdirectory of a leased directory except its lease file, refusing redirected entries.</summary>
    /// <exception cref="IOException">
    /// An entry is redirected, the directory holds more than <paramref name="maximumEntries"/> entries, or an entry cannot be deleted.
    /// </exception>
    internal static void DeleteContents(string directory, int maximumEntries)
    {
        Stack<string> pending = new();
        List<string> directories = [];
        pending.Push(directory);
        int entries = 0;
        while (pending.TryPop(out string? current))
        {
            ThrowIfRedirected(current, directory);
            directories.Add(current);
            foreach (string path in Directory.EnumerateFileSystemEntries(current))
            {
                if (current == directory && Path.GetFileName(path) == LeaseFileName) continue;
                if (++entries > maximumEntries)
                    throw new IOException("The private directory exceeds the cleanup entry limit.");
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

    private static PrivateDirectoryCleanupResult CleanupAbandonedCore(string root, int maximumEntries)
    {
        int removed = 0;
        int failed = 0;
        foreach (string directory in Directory.EnumerateDirectories(root).Take(MaximumCleanupCandidates))
        {
            if (!Guid.TryParseExact(Path.GetFileName(directory), "N", out _)) continue;
            try
            {
                ValidateLeasedDirectory(directory, root);
                FileStream lease;
                try
                {
                    lease = OpenLeaseFile(directory);
                }
                catch (IOException exception) when (IsSharingViolation(exception))
                {
                    // A live owner holds the lease; this is expected contention, not a failure.
                    continue;
                }
                using (lease)
                {
                    DeleteContents(directory, maximumEntries);
                }
                RemoveEmpty(directory);
                removed++;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // The directory stays eligible for a later cleanup; callers report the failed count.
                failed++;
            }
        }
        return new(removed, failed);
    }

    private static FileStream OpenLeaseFile(string directory)
    {
        string path = Path.Combine(directory, LeaseFileName);
        ThrowIfRedirected(path, directory);
        return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
    }

    private static void TryDeleteEmpty(string directory)
    {
        try
        {
            Directory.Delete(directory, recursive: false);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // An empty unleased directory is removed by the next abandoned cleanup.
        }
    }

    private static bool IsSharingViolation(IOException exception) => (exception.HResult & 0xffff) is 32 or 33;

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
        SecurityIdentifier user = identity.User ?? throw new InvalidOperationException("The current Windows user is unavailable.");
        security.AddAccessRule(new FileSystemAccessRule(user, FileSystemRights.FullControl,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
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
