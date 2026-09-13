// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Security.Cryptography;
using Serilog;
using Serilog.Events;

namespace Foundry.Core.Services.Profiles;

/// <summary>Publishes opaque encrypted revisions through a stable, exclusively opened commit journal.</summary>
public sealed partial class SharedProfileRepository : IDisposable
{
    private readonly string rootPath;
    private readonly string profilePath;
    private readonly string journalPath;
    private readonly string initializationPath;
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
            this.options.RetainedPayloadCount is < 1 or > 4096)
            throw new ArgumentOutOfRangeException(nameof(options));
        this.rootPath = Path.GetFullPath(rootPath);
        this.profilePath = Path.Combine(this.rootPath, "profiles", profileId.ToString("N"));
        this.journalPath = Path.Combine(profilePath, "head.journal");
        byte[] initializationIdentity = System.Text.Encoding.UTF8.GetBytes($"Foundry.SharedProfiles.v1/initialize/{repositoryId:N}/{profileId:N}/{keyEpoch}");
        initializationPath = Path.Combine(this.rootPath, ".initializing-" + Convert.ToHexString(HMACSHA256.HashData(sharedKey, initializationIdentity)));
        this.repositoryId = repositoryId;
        this.profileId = profileId;
        this.keyEpoch = keyEpoch;
        this.sharedKey = sharedKey.ToArray();
    }

    /// <summary>Explicitly creates a new repository or validates an existing enrollment; ordinary operations never initialize storage.</summary>
    public Task<SharedProfileRepositoryResult> InitializeAsync(CancellationToken cancellationToken = default) =>
        ExecuteAsync("Initialize", (files, token) =>
        {
            if (Volatile.Read(ref initialized) != 0)
            {
                using FileStream existingJournal = files.OpenJournal(journalPath);
                _ = ReadHistory(files, existingJournal, token, out _);
                CleanupInitialization();
                return new(SharedProfileRepositoryStatus.Success);
            }
            SharedProfileRepositoryFiles.ValidatePath(rootPath);
            Directory.CreateDirectory(rootPath);
            using FileStream repositoryLock = files.AcquireLock(Path.Combine(rootPath, "repository.lock"));
            InitializeStorage(files, token);
            using FileStream journal = files.OpenJournal(journalPath);
            _ = ReadHistory(files, journal, token, out _);
            CleanupInitialization();
            Volatile.Write(ref initialized, 1);
            return new(SharedProfileRepositoryStatus.Success);
        }, cancellationToken);

    /// <summary>Authenticates committed ancestry and rejects a head that no longer descends from a locally pinned revision.</summary>
    public Task<SharedProfileRepositoryResult> LoadAsync(Guid? knownRevisionId = null, CancellationToken cancellationToken = default) =>
        ExecuteAsync("Load", (files, token) =>
        {
            using FileStream journal = files.OpenJournal(journalPath);
            AuthenticatedHistory history = ReadHistory(files, journal, token, out _, knownRevisionId);
            EnsureKnownRevision(history);
            return new(SharedProfileRepositoryStatus.Success, ReadSnapshot(files, history.Head));
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

    /// <summary>Publishes a deletion revision conditionally, preserving committed ancestry and applying normal payload retention.</summary>
    public Task<SharedProfileRepositoryResult> TombstoneAsync(Guid expectedRevisionId, Guid operationId,
        CancellationToken cancellationToken = default) =>
        PublishCoreAsync([], expectedRevisionId, operationId, true, cancellationToken);

    /// <summary>Only authenticated committed ancestry proves success; orphan files never count as a committed operation.</summary>
    public Task<SharedProfileRepositoryResult> ReconcileAsync(Guid operationId, Guid? knownRevisionId = null,
        CancellationToken cancellationToken = default)
    {
        if (operationId == Guid.Empty) throw new ArgumentException("An operation identity is required.", nameof(operationId));
        return ExecuteAsync("Reconcile", (files, token) =>
        {
            using FileStream journal = files.OpenJournal(journalPath);
            AuthenticatedHistory history = ReadHistory(files, journal, token, out _, knownRevisionId, operationId);
            EnsureKnownRevision(history);
            RevisionMetadata? committed = history.CommittedOperation;
            return new(committed is null ? SharedProfileRepositoryStatus.NotCommitted : SharedProfileRepositoryStatus.Success,
                ReadSnapshot(files, history.Head), committed?.Revision.RevisionId);
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
            return await ExecuteAsync(tombstone ? "Tombstone" : "Publish", (files, token) =>
            {
                ValidateManifest(files);
                using FileStream journal = files.OpenJournal(journalPath);
                AuthenticatedHistory history = ReadHistory(files, journal, token, out long committedLength, expectedRevisionId, operationId);
                string hash = Convert.ToHexString(SHA256.HashData(payload));
                RevisionMetadata? previous = history.CommittedOperation;
                if (previous is not null)
                {
                    if (previous.Revision.ParentRevisionId != expectedRevisionId || previous.Revision.IsTombstone != tombstone ||
                        previous.PayloadLength != payload.Length || previous.PayloadHash != hash)
                        throw new SharedProfileRepositoryException(SharedProfileRepositoryStatus.InvalidData);
                    return new(SharedProfileRepositoryStatus.Success, ReadSnapshot(files, history.Head), previous.Revision.RevisionId);
                }

                EnsureKnownRevision(history);
                SharedProfileSnapshot? current = ReadSnapshot(files, history.Head);
                if (current?.Head.RevisionId != expectedRevisionId || current?.Head.IsTombstone == true)
                    return new(SharedProfileRepositoryStatus.Conflict, current);

                var revision = new SharedProfileRevision(repositoryId, profileId, Guid.NewGuid(), expectedRevisionId,
                    operationId, (current?.Head.Sequence ?? 0) + 1, keyEpoch, tombstone);
                var metadata = new RevisionMetadata(1, revision, hash, payload.Length);
                token.ThrowIfCancellationRequested();
                if (!tombstone) files.WriteBytes(RevisionPath(revision.RevisionId, ".payload"), payload);
                files.WriteSigned(RevisionPath(revision.RevisionId, ".json"), "revision", metadata);
                token.ThrowIfCancellationRequested();
                files.AppendJournal(journal, committedLength, CreateHead(revision));
                history.CommittedRevisionIds.Add(revision.RevisionId);
                RetainPayload(history.RetainedPayloads, revision);
                CleanupOldPayloads(history, token);
                return new(SharedProfileRepositoryStatus.Success, new(revision, payload.ToArray()), revision.RevisionId);
            }, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(payload);
        }
    }

    private AuthenticatedHistory ReadHistory(SharedProfileRepositoryFiles files, FileStream journal,
        CancellationToken token, out long committedLength, Guid? knownRevisionId = null, Guid? operationId = null)
    {
        ValidateManifest(files);
        SharedProfileRevision? head = null;
        SharedProfileRevision? committedOperation = null;
        bool knownFound = knownRevisionId is null;
        bool initialRecord = true;
        long sequence = 0;
        // Compact identity sets preserve duplicate detection without retaining historical metadata or payloads.
        var revisions = new HashSet<Guid>();
        var operations = new HashSet<Guid>();
        var retainedPayloads = new Queue<Guid>();
        committedLength = files.ReadJournal<HeadMetadata>(journal, commit =>
        {
            ValidateVersion(commit.FormatVersion);
            if (commit.RepositoryId != repositoryId || commit.ProfileId != profileId || commit.KeyEpoch != keyEpoch)
                throw new SharedProfileRepositoryException(SharedProfileRepositoryStatus.InvalidData);
            if (initialRecord)
            {
                initialRecord = false;
                if (commit.Revision is not null) throw new SharedProfileRepositoryException(SharedProfileRepositoryStatus.InvalidData);
                return;
            }
            ValidateRevision(commit.Revision);
            SharedProfileRevision revision = commit.Revision!;
            if (revision.Sequence != ++sequence || revision.ParentRevisionId != head?.RevisionId || head?.IsTombstone == true ||
                !revisions.Add(revision.RevisionId) || !operations.Add(revision.OperationId))
                throw new SharedProfileRepositoryException(SharedProfileRepositoryStatus.InvalidData);
            if (revision.RevisionId == knownRevisionId) knownFound = true;
            if (revision.OperationId == operationId) committedOperation = revision;
            head = revision;
            RetainPayload(retainedPayloads, revision);
        }, token);
        RevisionMetadata? headMetadata = head is null ? null : ReadRevisionMetadata(files, head);
        RevisionMetadata? operationMetadata = committedOperation is null ? null :
            committedOperation == head ? headMetadata : ReadRevisionMetadata(files, committedOperation);
        return new(headMetadata, operationMetadata, knownFound, revisions, retainedPayloads);
    }

    private RevisionMetadata ReadRevisionMetadata(SharedProfileRepositoryFiles files, SharedProfileRevision revision)
    {
        RevisionMetadata item = files.ReadSigned<RevisionMetadata>(RevisionPath(revision.RevisionId, ".json"), "revision");
        ValidateVersion(item.FormatVersion);
        if (item.Revision != revision || item.PayloadLength < 0 || item.PayloadLength > options.MaximumPayloadBytes ||
            item.PayloadHash is not { Length: 64 } || !item.PayloadHash.All(Uri.IsHexDigit) ||
            revision.IsTombstone != (item.PayloadLength == 0))
            throw new SharedProfileRepositoryException(SharedProfileRepositoryStatus.InvalidData);
        return item;
    }

    private SharedProfileSnapshot? ReadSnapshot(SharedProfileRepositoryFiles files, RevisionMetadata? head)
    {
        if (head is null) return null;
        byte[] payload = head.Revision.IsTombstone ? [] : files.ReadBytes(RevisionPath(head.Revision.RevisionId, ".payload"), options.MaximumPayloadBytes);
        if (payload.Length != head.PayloadLength || !string.Equals(Convert.ToHexString(SHA256.HashData(payload)), head.PayloadHash, StringComparison.Ordinal))
            throw new SharedProfileRepositoryException(SharedProfileRepositoryStatus.InvalidData);
        return new(head.Revision, payload);
    }

    private void RetainPayload(Queue<Guid> retained, SharedProfileRevision revision)
    {
        if (revision.IsTombstone) return;
        retained.Enqueue(revision.RevisionId);
        if (retained.Count > options.RetainedPayloadCount) retained.Dequeue();
    }

    private void CleanupOldPayloads(AuthenticatedHistory history, CancellationToken token)
    {
        if (token.IsCancellationRequested) return;
        var retained = new HashSet<Guid>(history.RetainedPayloads);
        string directory = Path.Combine(profilePath, "revisions");
        int removed = 0;
        int failed = 0;
        Exception? firstFailure = null;
        ILogger logger = Log.ForContext<SharedProfileRepository>()
            .ForContext("RepositoryId", repositoryId).ForContext("ProfileId", profileId);
        try
        {
            SharedProfileRepositoryFiles.ValidatePath(directory);
            foreach (string path in Directory.EnumerateFiles(directory, "*.payload", SearchOption.TopDirectoryOnly))
            {
                if (token.IsCancellationRequested) break;
                if (!Guid.TryParseExact(Path.GetFileNameWithoutExtension(path), "N", out Guid revisionId) ||
                    !history.CommittedRevisionIds.Contains(revisionId) || retained.Contains(revisionId)) continue;
                try
                {
                    SharedProfileRepositoryFiles.ValidatePath(path);
                    File.Delete(path);
                    removed++;
                }
                catch (Exception exception) when (IsCleanupFailure(exception))
                {
                    failed++;
                    firstFailure ??= exception;
                }
            }
        }
        catch (Exception exception) when (IsCleanupFailure(exception))
        {
            failed++;
            firstFailure ??= exception;
        }
        if (removed > 0 || failed > 0)
            logger.Write(failed == 0 ? LogEventLevel.Debug : LogEventLevel.Warning, firstFailure,
                "Shared profile payload retention finished. RemovedPayloadCount={RemovedPayloadCount}, FailedPayloadCount={FailedPayloadCount}, RetainedPayloadLimit={RetainedPayloadLimit}",
                removed, failed, options.RetainedPayloadCount);
    }

    private static bool IsCleanupFailure(Exception exception) =>
        exception is IOException or UnauthorizedAccessException or System.Security.SecurityException or SharedProfileRepositoryException;

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

    private static void EnsureKnownRevision(AuthenticatedHistory history)
    {
        if (!history.KnownRevisionFound)
            throw new SharedProfileRepositoryException(SharedProfileRepositoryStatus.RollbackDetected);
    }

    private string RevisionPath(Guid revisionId, string extension) =>
        Path.Combine(profilePath, "revisions", revisionId.ToString("N") + extension);

    private HeadMetadata CreateHead(SharedProfileRevision? revision) => new(1, repositoryId, profileId, keyEpoch, revision);

    private Task<SharedProfileRepositoryResult> ExecuteAsync(
        string operation, Func<SharedProfileRepositoryFiles, CancellationToken, SharedProfileRepositoryResult> action, CancellationToken cancellationToken)
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
            ILogger logger = Log.ForContext<SharedProfileRepository>()
                .ForContext("ProfileOperation", operation)
                .ForContext("RepositoryId", repositoryId)
                .ForContext("ProfileId", profileId);
            LogEventLevel successLevel = operation is "Load" or "Reconcile" ? LogEventLevel.Debug : LogEventLevel.Information;
            logger.Write(successLevel, "Shared profile repository operation started.");
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                SharedProfileRepositoryResult result = action(new SharedProfileRepositoryFiles(operationKey), cancellationToken);
                logger.Write(result.Status is SharedProfileRepositoryStatus.Success or SharedProfileRepositoryStatus.NotCommitted
                        ? successLevel : LogEventLevel.Warning,
                    "Shared profile repository operation completed. Status={Status}, RevisionId={RevisionId}",
                    result.Status, result.CommittedRevisionId ?? result.Snapshot?.Head.RevisionId);
                return result;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                logger.Write(successLevel, "Shared profile repository operation canceled.");
                throw;
            }
            catch (Exception exception) when (exception is SharedProfileRepositoryException or System.Text.Json.JsonException
                or CryptographicException or FormatException or IOException or UnauthorizedAccessException)
            {
                SharedProfileRepositoryStatus status = exception switch
                {
                    SharedProfileRepositoryException repositoryException => repositoryException.Status,
                    IOException or UnauthorizedAccessException => SharedProfileRepositoryStatus.Unavailable,
                    _ => SharedProfileRepositoryStatus.InvalidData
                };
                logger.Warning(exception, "Shared profile repository operation failed. Status={Status}", status);
                return new(status);
            }
            catch (Exception exception)
            {
                logger.Error(exception, "Shared profile repository operation failed unexpectedly.");
                throw;
            }
            finally { CryptographicOperations.ZeroMemory(operationKey); }
        });
    }

    private sealed record AuthenticatedHistory(RevisionMetadata? Head, RevisionMetadata? CommittedOperation,
        bool KnownRevisionFound, HashSet<Guid> CommittedRevisionIds, Queue<Guid> RetainedPayloads);

    private sealed record RepositoryManifest(int FormatVersion, Guid RepositoryId, int KeyEpoch);
    private sealed record HeadMetadata(int FormatVersion, Guid RepositoryId, Guid ProfileId, int KeyEpoch, SharedProfileRevision? Revision);
    private sealed record RevisionMetadata(int FormatVersion, SharedProfileRevision Revision, string PayloadHash, int PayloadLength);
}
