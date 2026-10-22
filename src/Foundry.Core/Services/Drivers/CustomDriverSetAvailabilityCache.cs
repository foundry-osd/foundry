// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Collections.Concurrent;
using Foundry.Core.Models.Configuration;
using Foundry.Core.Services.Configuration;

namespace Foundry.Core.Services.Drivers;

/// <summary>
/// Remembers the last known library availability of driver set packages, so readiness can be evaluated on the UI thread
/// without walking package files. A package that was never inspected is not treated as missing; media preparation still
/// acquires and verifies every included package before it is published.
/// </summary>
public sealed class CustomDriverSetAvailabilityCache
{
    private readonly ConcurrentDictionary<string, bool> availability = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Gets the last inspected availability of a package, or <see langword="null"/> when it was never inspected.</summary>
    public bool? GetAvailability(string contentHash) =>
        availability.TryGetValue(contentHash, out bool isAvailable) ? isAvailable : null;

    /// <summary>Evaluates readiness from cached availability only; packages that were never inspected count as available.</summary>
    public bool IsReady(CustomDriverSetsSettings settings) =>
        CustomDriverSetsSettingsValidator.IsReady(settings, package => GetAvailability(package.ContentHash) != false);

    /// <summary>Checks each package in the library and records the result. This touches every package file; call it off the UI thread.</summary>
    public void Inspect(CustomDriverSetLibrary library, IEnumerable<ContentPackageReference> packages)
    {
        ArgumentNullException.ThrowIfNull(library);
        ArgumentNullException.ThrowIfNull(packages);
        foreach (ContentPackageReference package in packages)
            availability[package.ContentHash] = library.IsAvailable(package);
    }
}
