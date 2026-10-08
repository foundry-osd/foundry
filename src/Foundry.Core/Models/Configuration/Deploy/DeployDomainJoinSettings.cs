// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Core.Models.Configuration.Deploy;

/// <summary>Carries the joinable domains to Foundry.Deploy; interactive projections omit accounts and credential envelopes.</summary>
public sealed record DeployDomainJoinSettings
{
    public bool IsEnabled { get; init; }
    public DomainJoinMode Mode { get; init; } = DomainJoinMode.Interactive;
    public IReadOnlyList<DeployDomainJoinDomainSettings> Domains { get; init; } = [];
    /// <summary>Gets the domain preselected when several are listed; the technician then chooses among them.</summary>
    public string? DefaultDomainId { get; init; }
}
