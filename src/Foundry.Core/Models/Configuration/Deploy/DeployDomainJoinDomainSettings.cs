// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Core.Models.Configuration.Deploy;

/// <summary>Carries one joinable domain with its resolved account and the credentials bound to that pair.</summary>
public sealed record DeployDomainJoinDomainSettings
{
    public string Id { get; init; } = string.Empty;
    public string DomainName { get; init; } = string.Empty;
    /// <summary>Gets the account this domain joins with, dedicated or shared. Zero-touch only.</summary>
    public string? AccountName { get; init; }
    public IReadOnlyList<DomainJoinOrganizationalUnitSettings> OrganizationalUnits { get; init; } = [];
    public string? DefaultOuId { get; init; }
    /// <summary>Gets the payload that only decodes for this domain and this account. Zero-touch only.</summary>
    public SecretEnvelope? EncryptedCredentials { get; init; }
}