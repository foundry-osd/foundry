// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Deploy.Services.Download;

/// <summary>Checks source availability without verifying or staging an artifact.</summary>
public interface IArtifactAvailabilityProbe
{
    /// <summary>Requires a reachable permitted source with at least one response byte.</summary>
    Task EnsureAvailableAsync(ArtifactIdentity artifact, CancellationToken cancellationToken = default);
}
