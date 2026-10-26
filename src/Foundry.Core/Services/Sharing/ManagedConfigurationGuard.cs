// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Text.Json;
using Foundry.Core.Models.Configuration;
using Foundry.Core.Services.Configuration;

namespace Foundry.Core.Services.Sharing;

/// <summary>
/// The read-only rule of a follower: while a published configuration manages this PC, only its per-PC values may change.
/// </summary>
/// <remarks>
/// The rule is the per-PC field list of <see cref="PortableConfiguration"/>, the single source: every other value, including the
/// <c>CustomDrivers</c> section and any section added later, is published and therefore rejected, without a change here. Passwords
/// are not part of the document, so editing a password the publication does not include is never a change of the document.
/// </remarks>
public static class ManagedConfigurationGuard
{
    /// <summary>
    /// Returns whether <paramref name="after"/> differs from <paramref name="before"/> only in per-PC values (or not at all).
    /// </summary>
    /// <param name="before">The current managed configuration.</param>
    /// <param name="after">The configuration an edit would save.</param>
    public static bool IsAllowed(FoundryConfigurationDocument before, FoundryConfigurationDocument after)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);

        // Records compare their lists by reference, so both documents are compared through their persisted JSON form.
        FoundryConfigurationDocument published = PortableConfiguration.ApplyLocal(after, before);
        return string.Equals(Serialize(published), Serialize(before), StringComparison.Ordinal);
    }

    private static string Serialize(FoundryConfigurationDocument document) =>
        JsonSerializer.Serialize(document, ConfigurationJsonDefaults.SerializerOptions);
}
