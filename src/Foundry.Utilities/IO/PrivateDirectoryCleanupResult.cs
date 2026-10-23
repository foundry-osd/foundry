// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Utilities.IO;

/// <summary>
/// Reports one abandoned private directory cleanup pass.
/// </summary>
/// <remarks>
/// Directories still leased by a live owner are expected contention and are counted in neither value.
/// Failed directories may still hold private inputs; they stay eligible for a later cleanup, so callers should surface the count.
/// </remarks>
/// <param name="Removed">The number of abandoned directories removed.</param>
/// <param name="Failed">The number of abandoned directories that were redirected, exceeded the entry bound, or could not be deleted.</param>
public sealed record PrivateDirectoryCleanupResult(int Removed, int Failed);
