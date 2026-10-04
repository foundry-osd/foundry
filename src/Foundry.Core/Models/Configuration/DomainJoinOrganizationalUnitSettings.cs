// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Core.Models.Configuration;

/// <summary>References an authored OU destination; live directory identity must be resolved by GUID.</summary>
public sealed record DomainJoinOrganizationalUnitSettings
{
    public string Id { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string DistinguishedName { get; init; } = string.Empty;
}
