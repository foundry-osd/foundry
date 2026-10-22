// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Core.Services.WinPe;

/// <summary>Outcome of removing content package generations that the current media manifest no longer references.</summary>
/// <param name="RemovedPackages">Package folders that were removed completely.</param>
/// <param name="RemovedManifests">Older media manifests that were removed after their package folders.</param>
/// <param name="RetainedUnknownEntries">
/// Media-relative paths left in place because no previous manifest proves Foundry wrote them, their recorded length
/// changed, or they are redirected through a reparse point.
/// </param>
public sealed record ContentPackagePruneResult(int RemovedPackages, int RemovedManifests, IReadOnlyList<string> RetainedUnknownEntries);
