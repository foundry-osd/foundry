// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Core.Models.Configuration.Deploy;

/// <summary>Carries active domain metadata; interactive projections omit the account and credential envelope.</summary>
public sealed record DeployDomainJoinSettings
{
    public bool IsEnabled { get; init; }
    public DomainJoinMode Mode { get; init; } = DomainJoinMode.Interactive;
    public string? DomainName { get; init; }
    public string? AccountName { get; init; }
    public string? OuCatalogDomain { get; init; }
    public IReadOnlyList<DomainJoinOrganizationalUnitSettings> OrganizationalUnits { get; init; } = [];
    public string? DefaultOuId { get; init; }
    public bool AllowOuSelectionDuringDeployment { get; init; }
    public SecretEnvelope? EncryptedCredentials { get; init; }
}
