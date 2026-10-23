// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Core.Services.Configuration.Legacy;

/// <summary>The Credential Manager key of a legacy local profile is unavailable for the current Windows user.</summary>
/// <param name="localId">The local identity of the locked profile.</param>
public sealed class LegacyProfileLockedException(Guid localId) : IOException("The legacy local profile key is unavailable for this Windows user.")
{
    /// <summary>Gets the local identity of the profile whose revision key is missing.</summary>
    public Guid LocalId { get; } = localId;
}
