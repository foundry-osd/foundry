// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Core.Services.Storage;

/// <summary>Keeps the entry exclusively leased across processes until consumption finishes.</summary>
public sealed class CachedArtifactLease : IAsyncDisposable
{
    private readonly FileStream entryLease;

    internal CachedArtifactLease(string path, bool cacheHit, FileStream entryLease)
    {
        Path = path;
        CacheHit = cacheHit;
        this.entryLease = entryLease;
    }

    /// <summary>Gets the verified original's path, valid for the lifetime of this lease.</summary>
    public string Path { get; }

    /// <summary>Gets whether acquisition reused verified previously published bytes.</summary>
    public bool CacheHit { get; }

    /// <summary>Releases coordination without deleting persistent bytes.</summary>
    public ValueTask DisposeAsync() => entryLease.DisposeAsync();
}
