// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Security.Cryptography;

namespace Foundry.Core.Services.Profiles;

/// <summary>Publishes opaque encrypted revisions through a stable, exclusively opened commit journal.</summary>
public sealed class SharedProfileRepository : IDisposable
{
    private readonly string rootPath;
    private readonly string profilePath;
    private readonly string journalPath;
    private readonly Guid repositoryId;
    private readonly Guid profileId;
    private readonly int keyEpoch;
    private readonly byte[] sharedKey;
    private readonly SharedProfileRepositoryOptions options;
    private readonly object keyLock = new();
    private bool disposed;
    private int initialized;

    /// <summary>Copies the enrollment key; the caller must pin these identities outside the shared directory.</summary>
    public SharedProfileRepository(string rootPath, Guid repositoryId, Guid profileId, int keyEpoch,
        ReadOnlySpan<byte> sharedKey, SharedProfileRepositoryOptions? options = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);
        if (!Path.IsPathFullyQualified(rootPath)) throw new ArgumentException("An absolute repository path is required.", nameof(rootPath));
        if (repositoryId == Guid.Empty || profileId == Guid.Empty) throw new ArgumentException("Repository identities must be nonempty.");
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(keyEpoch);
        if (sharedKey.Length != 32) throw new ArgumentException("A 32-byte enrollment key is required.", nameof(sharedKey));
        this.options = options ?? new();
        if (this.options.MaximumPayloadBytes is < 1 or > 256 * 1024 * 1024 ||
            this.options.MaximumHistoryCount is < 1 or > 4096)
            throw new ArgumentOutOfRangeException(nameof(options));
        this.rootPath = Path.GetFullPath(rootPath);
        this.profilePath = Path.Combine(this.rootPath, "profiles", profileId.ToString("N"));
        this.journalPath = Path.Combine(profilePath, "head.journal");
        this.repositoryId = repositoryId;
        this.profileId = profileId;
        this.keyEpoch = keyEpoch;
        this.sharedKey = sharedKey.ToArray();
    }

    /// <summary>Explicitly creates a new repository or validates an existing enrollment; ordinary operations never initialize storage.</summary>
    public Task<SharedProfileRepositoryResult> InitializeAsync(CancellationToken cancellationToken = default) =>
        ExecuteAsync((files, token) =>
        {
            if (Volatile.Read(ref initialized) != 0)
            {
                using FileStream existingJournal = files.OpenJournal(journalPath);
                _ = ReadHistory(files, existingJournal, token, out _);
                return new(SharedProfileRepositoryStatus.Success);
            }
            SharedProfileRepositoryFiles.ValidatePath(rootPath);
            Directory.CreateDirectory(rootPath);
            using FileStream repositoryLock = files.AcquireLock(Path.Combine(rootPath, "repository.lock"));
            string manifestPath = Path.Combine(rootPath, "repository.json");
            if (files.Exists(manifestPath))
            {
                ValidateManifest(files);
            }
            else
            {
                // Missing metadata in nonempty storage must not turn an old repository into a fresh one.
                if (Directory.EnumerateFileSystemEntries(rootPath).Any(path =>
                    !string.Equals(Path.GetFileName(path), "repository.lock", StringComparison.Ordinal)))
                    throw new SharedProfileRepositoryException(SharedProfileRepositoryStatus.InvalidData);
                files.WriteSigned(manifestPath, "repository", new RepositoryManifest(1, repositoryId, keyEpoch));
            }
            token.ThrowIfCancellationRequested();
            bool profileExisted = Directory.Exists(profilePath);
            Directory.CreateDirectory(profilePath);
            Directory.CreateDirectory(Path.Combine(profilePath, "revisions"));
            using FileStream journal = files.OpenJournal(journalPath, create: !profileExisted);
            if (!profileExisted) files.AppendJournal(journal, 0, CreateHead(null));
            _ = ReadHistory(files, journal, token, out _);
            Volatile.Write(ref initialized, 1);
            return new(SharedProfileRepositoryStatus.Success);
        }, cancellationToken);

    /// <summary>Authenticates committed ancestry and rejects a head that no longer descends from a locally pinned revision.</summary>
    public Task<SharedProfileRepositoryResult> LoadAsync(Guid? knownRevisionId = null, CancellationToken cancellationToken = default) =>
        ExecuteAsync((files, token) =>
        {
            using FileStream journal = files.OpenJournal(journalPath);
            List<RevisionMetadata> history = ReadHistory(files, journal, token, out _);
            EnsureKnownRevision(history, knownRevisionId);
            return new(SharedProfileRepositoryStatus.Success, ReadSnapshot(files, history));
        }, cancellationToken);

    /// <summary>Copies encrypted input immediately, then publishes only if the locked head still equals the expected base.</summary>
    public Task<SharedProfileRepositoryResult> PublishAsync(byte[] encryptedPayload, Guid? expectedRevisionId,
        Guid operationId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(encryptedPayload);
        if (encryptedPayload.Length == 0 || encryptedPayload.Length > options.MaximumPayloadBytes)
            throw new ArgumentOutOfRangeException(nameof(encryptedPayload));
        return PublishCoreAsync(encryptedPayload.ToArray(), expectedRevisionId, operationId, false, cancellationToken);
    }

    /// <summary>Publishes a deletion revision conditionally; no files or committed ancestry are removed.</summary>
    public Task<SharedProfileRepositoryResult> TombstoneAsync(Guid expectedRevisionId, Guid operationId,
        CancellationToken cancellationToken = default) =>
        PublishCoreAsync([], expectedRevisionId, operationId, true, cancellationToken);

    /// <summary>Only authenticated committed ancestry proves success; orphan files never count as a committed operation.</summary>
    public Task<SharedProfileRepositoryResult> ReconcileAsync(Guid operationId, Guid? knownRevisionId = null,
        CancellationToken cancellationToken = default)
    {
        if (operationId == Guid.Empty) throw new ArgumentException("An operation identity is required.", nameof(operationId));
        return ExecuteAsync((files, token) =>
        {
            using FileStream journal = files.OpenJournal(journalPath);
            List<RevisionMetadata> history = ReadHistory(files, journal, token, out _);
            EnsureKnownRevision(history, knownRevisionId);
            RevisionMetadata? committed = history.Find(revision => revision.Revision.OperationId == operationId);
            return new(committed is null ? SharedProfileRepositoryStatus.NotCommitted : SharedProfileRepositoryStatus.Success,
                ReadSnapshot(files, history), committed?.Revision.RevisionId);
        }, cancellationToken);
    }

    /// <summary>Clears the owned enrollment key; already started operations own separate bounded-lifetime copies.</summary>
    public void Dispose()
    {
        lock (keyLock)
        {
            disposed = true;
            CryptographicOperations.ZeroMemory(sharedKey);
        }
    }

    private async Task<SharedProfileRepositoryResult> PublishCoreAsync(byte[] payload, Guid? expectedRevisionId,
        Guid operationId, bool tombstone, CancellationToken cancellationToken)
    {
        if (operationId == Guid.Empty || expectedRevisionId == Guid.Empty)
            throw new ArgumentException("Operation and revision identities must be nonempty.");
        try
        {
            return await ExecuteAsync((files, token) =>
            {
                ValidateManifest(files);
                using FileStream journal = files.OpenJournal(journalPath);
                List<RevisionMetadata> history = ReadHistory(files, journal, token, out long committedLength);
                string hash = Convert.ToHexString(SHA256.HashData(payload));
                RevisionMetadata? previous = history.Find(revision => revision.Revision.OperationId == operationId);
                if (previous is not null)
                {
                    if (previous.Revision.ParentRevisionId != expectedRevisionId || previous.Revision.IsTombstone != tombstone ||
                        previous.PayloadLength != payload.Length || previous.PayloadHash != hash)
                        throw new SharedProfileRepositoryException(SharedProfileRepositoryStatus.InvalidData);
                    return new(SharedProfileRepositoryStatus.Success, ReadSnapshot(files, history), previous.Revision.RevisionId);
                }

                EnsureKnownRevision(history, expectedRevisionId);
                SharedProfileSnapshot? current = ReadSnapshot(files, history);
                if (current?.Head.RevisionId != expectedRevisionId || current?.Head.IsTombstone == true)
                    return new(SharedProfileRepositoryStatus.Conflict, current);
                if (history.Count >= options.MaximumHistoryCount)
                    throw new SharedProfileRepositoryException(SharedProfileRepositoryStatus.HistoryLimitExceeded);

                var revision = new SharedProfileRevision(repositoryId, profileId, Guid.NewGuid(), expectedRevisionId,
                    operationId, (current?.Head.Sequence ?? 0) + 1, keyEpoch, tombstone);
                var metadata = new RevisionMetadata(1, revision, hash, payload.Length);
                token.ThrowIfCancellationRequested();
                if (!tombstone) files.WriteBytes(RevisionPath(revision.RevisionId, ".payload"), payload);
                files.WriteSigned(RevisionPath(revision.RevisionId, ".json"), "revision", metadata);
                token.ThrowIfCancellationRequested();
                files.AppendJournal(journal, committedLength, CreateHead(revision));
                return new(SharedProfileRepositoryStatus.Success, new(revision, payload.ToArray()), revision.RevisionId);
            }, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(payload);
        }
    }

    private List<RevisionMetadata> ReadHistory(SharedProfileRepositoryFiles files, FileStream journal,
        CancellationToken token, out long committedLength)
    {
        ValidateManifest(files);
        SharedProfileJournal<HeadMetadata> commits = files.ReadJournal<HeadMetadata>(journal, options.MaximumHistoryCount + 1, token);
        committedLength = commits.CommittedLength;
        ValidateCommits(commits.Records);
        HeadMetadata head = commits.Records[^1];
        if (head.Revision is null) return [];
        var history = new List<RevisionMetadata>();
        var revisions = new HashSet<Guid>();
        var operations = new HashSet<Guid>();
        Guid? next = head.Revision.RevisionId;
        long sequence = head.Revision.Sequence;
        while (next is Guid id)
        {
            token.ThrowIfCancellationRequested();
            if (history.Count >= options.MaximumHistoryCount)
                throw new SharedProfileRepositoryException(SharedProfileRepositoryStatus.HistoryLimitExceeded);
            RevisionMetadata item = files.ReadSigned<RevisionMetadata>(RevisionPath(id, ".json"), "revision");
            ValidateVersion(item.FormatVersion);
            ValidateRevision(item.Revision);
            if (item.Revision.RevisionId != id || item.Revision.Sequence != sequence || !revisions.Add(id) ||
                !operations.Add(item.Revision.OperationId) || item.PayloadLength < 0 || item.PayloadLength > options.MaximumPayloadBytes ||
                item.PayloadHash is not { Length: 64 } || !item.PayloadHash.All(Uri.IsHexDigit) ||
                item.Revision.IsTombstone != (item.PayloadLength == 0) ||
                item.Revision != commits.Records[commits.Records.Count - 1 - history.Count].Revision ||
                history.Count > 0 && item.Revision.IsTombstone)
                throw new SharedProfileRepositoryException(SharedProfileRepositoryStatus.InvalidData);
            history.Add(item);
            next = item.Revision.ParentRevisionId;
            sequence--;
        }
        if (sequence != 0 || history.Count != commits.Records.Count - 1)
            throw new SharedProfileRepositoryException(SharedProfileRepositoryStatus.InvalidData);
        return history;
    }

    private void ValidateCommits(IReadOnlyList<HeadMetadata> commits)
    {
        SharedProfileRevision? previous = null;
        for (int index = 0; index < commits.Count; index++)
        {
            HeadMetadata commit = commits[index];
            ValidateVersion(commit.FormatVersion);
            if (commit.RepositoryId != repositoryId || commit.ProfileId != profileId || commit.KeyEpoch != keyEpoch)
                throw new SharedProfileRepositoryException(SharedProfileRepositoryStatus.InvalidData);
            if (index == 0)
            {
                if (commit.Revision is not null) throw new SharedProfileRepositoryException(SharedProfileRepositoryStatus.InvalidData);
                continue;
            }
            ValidateRevision(commit.Revision);
            if (commit.Revision!.Sequence != index || commit.Revision.ParentRevisionId != previous?.RevisionId || previous?.IsTombstone == true)
                throw new SharedProfileRepositoryException(SharedProfileRepositoryStatus.InvalidData);
            previous = commit.Revision;
        }
    }

    private SharedProfileSnapshot? ReadSnapshot(SharedProfileRepositoryFiles files, List<RevisionMetadata> history)
    {
        if (history.Count == 0) return null;
        RevisionMetadata head = history[0];
        byte[] payload = head.Revision.IsTombstone ? [] : files.ReadBytes(RevisionPath(head.Revision.RevisionId, ".payload"), options.MaximumPayloadBytes);
        if (payload.Length != head.PayloadLength || !string.Equals(Convert.ToHexString(SHA256.HashData(payload)), head.PayloadHash, StringComparison.Ordinal))
            throw new SharedProfileRepositoryException(SharedProfileRepositoryStatus.InvalidData);
        return new(head.Revision, payload);
    }

    private void ValidateManifest(SharedProfileRepositoryFiles files)
    {
        RepositoryManifest manifest = files.ReadSigned<RepositoryManifest>(Path.Combine(rootPath, "repository.json"), "repository");
        ValidateVersion(manifest.FormatVersion);
        if (manifest.RepositoryId != repositoryId || manifest.KeyEpoch != keyEpoch)
            throw new SharedProfileRepositoryException(SharedProfileRepositoryStatus.InvalidData);
        SharedProfileRepositoryFiles.ValidatePath(profilePath);
    }

    private void ValidateRevision(SharedProfileRevision? revision)
    {
        if (revision is null || revision.RepositoryId != repositoryId || revision.ProfileId != profileId ||
            revision.KeyEpoch != keyEpoch || revision.RevisionId == Guid.Empty || revision.OperationId == Guid.Empty ||
            revision.ParentRevisionId == Guid.Empty || revision.Sequence <= 0 ||
            (revision.ParentRevisionId is null) != (revision.Sequence == 1))
            throw new SharedProfileRepositoryException(SharedProfileRepositoryStatus.InvalidData);
    }

    private static void ValidateVersion(int version)
    {
        if (version != 1) throw new SharedProfileRepositoryException(
            version > 1 ? SharedProfileRepositoryStatus.UnsupportedFormat : SharedProfileRepositoryStatus.InvalidData);
    }

    private static void EnsureKnownRevision(List<RevisionMetadata> history, Guid? knownRevisionId)
    {
        if (knownRevisionId is not null && !history.Any(item => item.Revision.RevisionId == knownRevisionId))
            throw new SharedProfileRepositoryException(SharedProfileRepositoryStatus.RollbackDetected);
    }

    private string RevisionPath(Guid revisionId, string extension) =>
        Path.Combine(profilePath, "revisions", revisionId.ToString("N") + extension);

    private HeadMetadata CreateHead(SharedProfileRevision? revision) => new(1, repositoryId, profileId, keyEpoch, revision);

    private Task<SharedProfileRepositoryResult> ExecuteAsync(
        Func<SharedProfileRepositoryFiles, CancellationToken, SharedProfileRepositoryResult> action, CancellationToken cancellationToken)
    {
        byte[] operationKey;
        lock (keyLock)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            operationKey = sharedKey.ToArray();
        }
        // Native UNC opens can block despite cancellation. They never execute on the caller's UI thread.
        return Task.Run(() =>
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                return action(new SharedProfileRepositoryFiles(operationKey), cancellationToken);
            }
            catch (SharedProfileRepositoryException exception) { return new(exception.Status); }
            catch (System.Text.Json.JsonException) { return new(SharedProfileRepositoryStatus.InvalidData); }
            catch (CryptographicException) { return new(SharedProfileRepositoryStatus.InvalidData); }
            catch (FormatException) { return new(SharedProfileRepositoryStatus.InvalidData); }
            catch (IOException) { return new(SharedProfileRepositoryStatus.Unavailable); }
            catch (UnauthorizedAccessException) { return new(SharedProfileRepositoryStatus.Unavailable); }
            finally { CryptographicOperations.ZeroMemory(operationKey); }
        });
    }

    private sealed record RepositoryManifest(int FormatVersion, Guid RepositoryId, int KeyEpoch);
    private sealed record HeadMetadata(int FormatVersion, Guid RepositoryId, Guid ProfileId, int KeyEpoch, SharedProfileRevision? Revision);
    private sealed record RevisionMetadata(int FormatVersion, SharedProfileRevision Revision, string PayloadHash, int PayloadLength);
}
