// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Core.Models.Configuration;

/// <summary>Describes one domain a deployment media can join, with the OUs that belong to it.</summary>
public sealed record DomainJoinDomainSettings
{
    /// <summary>Gets the stable identifier other settings refer to; it is never shown.</summary>
    public string Id { get; init; } = string.Empty;
    public string DomainName { get; init; } = string.Empty;
    /// <summary>Gets the dedicated join account; <see langword="null"/> means the shared account. Zero-touch only.</summary>
    public string? AccountName { get; init; }
    public IReadOnlyList<DomainJoinOrganizationalUnitSettings> OrganizationalUnits { get; init; } = [];
    /// <summary>Gets the default OU of this domain; <see langword="null"/> means the domain's default location.</summary>
    public string? DefaultOuId { get; init; }
}
