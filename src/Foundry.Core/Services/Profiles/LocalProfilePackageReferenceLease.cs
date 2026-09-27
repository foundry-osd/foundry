// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Core.Services.Profiles;

/// <summary>Keeps saved profile references stable while a caller decides whether cached packages can be deleted.</summary>
public sealed class LocalProfilePackageReferenceLease : IDisposable
{
    private readonly FileStream lease;

    internal LocalProfilePackageReferenceLease(FileStream lease, IReadOnlySet<string> contentHashes)
    {
        this.lease = lease;
        ContentHashes = contentHashes;
    }

    /// <summary>Contains references from every saved profile, including disabled actions and customization.</summary>
    public IReadOnlySet<string> ContentHashes { get; }

    /// <summary>Releases the repository lock; references must not authorize deletion after this call.</summary>
    public void Dispose() => lease.Dispose();
}
