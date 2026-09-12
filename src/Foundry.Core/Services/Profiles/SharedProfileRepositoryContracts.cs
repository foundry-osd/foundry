// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Core.Services.Profiles;

/// <summary>Classifies repository outcomes without exposing remote paths or confidential payloads.</summary>
public enum SharedProfileRepositoryStatus
{
    Success,
    Conflict,
    Busy,
    Unavailable,
    InvalidData,
    UnsupportedFormat,
    RollbackDetected,
    HistoryLimitExceeded,
    NotCommitted
}

/// <summary>Bounds untrusted payloads and authenticated ancestry; exhausted history requires explicit maintenance.</summary>
public sealed record SharedProfileRepositoryOptions
{
    public int MaximumPayloadBytes { get; init; } = 64 * 1024 * 1024;
    public int MaximumHistoryCount { get; init; } = 512;
}

/// <summary>Identifies one authenticated immutable revision, including deletion tombstones.</summary>
public sealed record SharedProfileRevision(
    Guid RepositoryId,
    Guid ProfileId,
    Guid RevisionId,
    Guid? ParentRevisionId,
    Guid OperationId,
    long Sequence,
    int KeyEpoch,
    bool IsTombstone);

/// <summary>Contains caller-owned encrypted bytes bound to the authenticated revision.</summary>
public sealed record SharedProfileSnapshot(SharedProfileRevision Head, byte[] EncryptedPayload);

/// <summary>A missing snapshot on success means a new profile has no committed head.</summary>
/// <param name="CommittedRevisionId">The original committed revision when an operation was reconciled or retried.</param>
public sealed record SharedProfileRepositoryResult(
    SharedProfileRepositoryStatus Status,
    SharedProfileSnapshot? Snapshot = null,
    Guid? CommittedRevisionId = null);
