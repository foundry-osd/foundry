// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Utilities.IO;

/// <summary>
/// Holds the exclusive lease of one private directory created by <see cref="PrivateDirectory.CreateLeased(string)"/>.
/// </summary>
/// <remarks>
/// While the lease is held, <see cref="PrivateDirectory.CleanupAbandoned(string)"/> skips the directory in every process.
/// Disposing removes the directory under the root cleanup lock; removal errors are surfaced after the lease handle is released,
/// so the directory stays eligible for abandoned cleanup, and a later <see cref="Dispose"/> call retries the removal.
/// </remarks>
public sealed class PrivateDirectoryLease : IDisposable
{
    private readonly string root;
    private readonly int maximumEntries;
    private FileStream? lease;

    internal PrivateDirectoryLease(string root, string path, FileStream lease, int maximumEntries, PrivateDirectoryCleanupResult abandonedCleanup)
    {
        this.root = root;
        Path = path;
        this.lease = lease;
        this.maximumEntries = maximumEntries;
        AbandonedCleanup = abandonedCleanup;
    }

    /// <summary>Gets the full path of the leased directory.</summary>
    public string Path { get; }

    /// <summary>Gets the result of the abandoned cleanup that ran under the root lock before this directory was created.</summary>
    public PrivateDirectoryCleanupResult AbandonedCleanup { get; }

    /// <summary>Deletes every file and nested directory while keeping the directory and its lease.</summary>
    /// <exception cref="ObjectDisposedException">The lease was released.</exception>
    /// <exception cref="IOException">An entry is redirected, the entry bound given at creation is exceeded, or an entry cannot be deleted.</exception>
    public void DeleteContents()
    {
        ObjectDisposedException.ThrowIf(lease is null, this);
        PrivateDirectory.DeleteContents(Path, maximumEntries);
    }

    /// <summary>Removes the directory and its contents, then releases the lease.</summary>
    /// <exception cref="IOException">The directory or an entry could not be removed; the lease is released anyway.</exception>
    /// <exception cref="UnauthorizedAccessException">An entry could not be removed; the lease is released anyway.</exception>
    public void Dispose()
    {
        try
        {
            if (!Directory.Exists(Path)) return;
            using FileStream cleanupLock = PrivateDirectory.AcquireCleanupLock(root);
            lease ??= PrivateDirectory.OpenLease(Path, root);
            PrivateDirectory.DeleteContents(Path, maximumEntries);
            lease.Dispose();
            lease = null;
            PrivateDirectory.RemoveEmpty(Path);
        }
        finally
        {
            lease?.Dispose();
            lease = null;
        }
    }
}
