// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Models.Configuration;

namespace Foundry.Services.DomainJoin;

/// <summary>Discovers the OUs of a named AD domain using the author's current Windows identity.</summary>
public interface IAuthoringDomainOuDiscoveryService
{
    /// <summary>
    /// Returns a bounded list without retaining an authenticated connection. It works for the authoring computer's
    /// own domain and for domains that trust the author's account and are reachable from this computer.
    /// </summary>
    Task<DomainOuDiscoveryResult> DiscoverAsync(string domainName, CancellationToken cancellationToken);
}

/// <summary>Distinguishes a complete catalog preview from partial results and unavailable discovery.</summary>
public enum DomainOuDiscoveryStatus { Complete, Incomplete, Unavailable, Canceled }

/// <summary>Contains only portable directory metadata and an allowlisted presentation code.</summary>
public sealed record DomainOuDiscoveryResult(string? DomainName,
    IReadOnlyList<DomainJoinOrganizationalUnitSettings> Candidates, DomainOuDiscoveryStatus Status, string? ErrorCode = null,
    int? NativeErrorCode = null, int? LdapErrorCode = null, int? DirectoryResultCode = null);
