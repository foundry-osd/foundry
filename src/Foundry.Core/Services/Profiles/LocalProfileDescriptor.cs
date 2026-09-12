// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Security.Cryptography;
using System.Text.Json.Serialization;
using Foundry.Core.Models.Profiles;

namespace Foundry.Core.Services.Profiles;

/// <summary>Identifies one committed local revision; the local identity is independent from its shared profile identity.</summary>
public sealed record LocalProfileDescriptor
{
    public int Version { get; init; } = 1;
    public Guid LocalId { get; init; }
    public Guid ProfileId { get; init; }
    public Guid Revision { get; init; }
    public string DisplayName { get; init; } = string.Empty;
    public bool RememberSecrets { get; init; }
    public LocalProfileEnrollment? Enrollment { get; init; }
    /// <summary>Repository-controlled local credential identity, never a native target supplied by a profile package.</summary>
    public Guid? SharedKeyRevision { get; init; }
    /// <summary>The committed revision is usable, but an earlier key or file still requires cleanup.</summary>
    [JsonIgnore]
    public bool CleanupPending { get; init; }
}

/// <summary>Local synchronization enrollment. Keys and encrypted outbox bodies are managed separately.</summary>
public sealed record LocalProfileEnrollment
{
    public string RootPath { get; init; } = string.Empty;
    public Guid RepositoryId { get; init; }
    public Guid ProfileId { get; init; }
    public int KeyEpoch { get; init; }
    public Guid? KnownRevisionId { get; init; }
    public Guid? PendingOperationId { get; init; }
    public bool IsDirty { get; init; }
    public bool IsEnabled { get; init; }
    public bool RememberSharedKey { get; init; }
    public bool IncludeSecrets { get; init; }
}

/// <summary>Owns decrypted profile secret and asset buffers for one committed revision.</summary>
public sealed record LocalProfileSnapshot(LocalProfileDescriptor Descriptor, DeploymentProfileDocument Profile) : IDisposable
{
    public void Dispose()
    {
        foreach (DeploymentProfileSecret secret in Profile.Secrets.Entries)
        {
            if (secret.Value is { } value)
            {
                CryptographicOperations.ZeroMemory(value);
            }
        }
        foreach (DeploymentProfileAsset asset in Profile.Assets)
        {
            if (asset.Content is { } content)
            {
                CryptographicOperations.ZeroMemory(content);
            }
        }
    }
}

/// <summary>A committed profile or enrollment key is unavailable for the current Windows identity.</summary>
public sealed class LocalProfileLockedException(Guid localId) : IOException("The local profile key is unavailable for this Windows user.")
{
    public Guid LocalId { get; } = localId;
}

/// <summary>A local writer attempted to replace a revision that has changed since it was read.</summary>
public sealed class LocalProfileConflictException(Guid? currentRevision) : IOException("The local profile changed. Reload it before saving.")
{
    public Guid? CurrentRevision { get; } = currentRevision;
}
