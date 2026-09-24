// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Core.Services.Storage;

/// <summary>Identifies persistent original-download owners; runtime archives are deliberately excluded.</summary>
public enum AuthoringArtifactKind
{
    WindowsSource,
    WinPeDriver,
    Installer
}

/// <summary>Describes an original download. Hashless reuse requires an owner-established pinned source policy and proves local consistency only.</summary>
public sealed record CachedArtifactRequest(
    AuthoringArtifactKind Kind,
    string SourceIdentity,
    string FileName,
    string? ExpectedSha256,
    long? ExpectedLength,
    bool AllowCompletedTransferReuse);

/// <summary>Reports actual bytes hashed; completion is emitted only after successful comparison.</summary>
public sealed record CachedArtifactVerificationProgress(long BytesVerified, long TotalBytes, bool IsComplete);
