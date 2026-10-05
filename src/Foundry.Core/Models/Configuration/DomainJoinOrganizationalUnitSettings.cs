// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Core.Models.Configuration;

/// <summary>
/// Describes a saved OU. The id is the directory object GUID for a discovered OU and a generated value for a
/// manually added one; the runtime resolves the live object from the distinguished name.
/// </summary>
public sealed record DomainJoinOrganizationalUnitSettings
{
    public string Id { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string DistinguishedName { get; init; } = string.Empty;
}
