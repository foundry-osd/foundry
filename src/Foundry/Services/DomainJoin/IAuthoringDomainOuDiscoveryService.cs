// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Models.Configuration;

namespace Foundry.Services.DomainJoin;

/// <summary>Discovers destinations from the authoring computer's AD domain using its current Windows identity.</summary>
public interface IAuthoringDomainOuDiscoveryService
{
    /// <summary>Returns a bounded preview without retaining an authenticated connection.</summary>
    Task<DomainOuDiscoveryResult> DiscoverAsync(CancellationToken cancellationToken);
}

/// <summary>Distinguishes a complete catalog preview from partial results and unavailable discovery.</summary>
public enum DomainOuDiscoveryStatus { Complete, Incomplete, Unavailable, Canceled }

/// <summary>Contains only portable directory metadata and an allowlisted presentation code.</summary>
public sealed record DomainOuDiscoveryResult(string? ComputerDomain, string? NamingContext,
    IReadOnlyList<DomainJoinOrganizationalUnitSettings> Candidates, DomainOuDiscoveryStatus Status, string? ErrorCode = null,
    int? NativeErrorCode = null, int? LdapErrorCode = null, int? DirectoryResultCode = null);
