// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Models.Configuration;

namespace Foundry.Core.Services.Configuration;

/// <summary>Maintains the bounded OU list of one domain without changing the identity of OUs already listed.</summary>
public static class DomainJoinOrganizationalUnitCatalog
{
    /// <summary>Adds manually entered or discovered OUs to one domain, skipping OUs it already lists.</summary>
    public static DomainJoinSettings Merge(DomainJoinSettings current, string domainId,
        IReadOnlyList<DomainJoinOrganizationalUnitSettings> selected)
    {
        ArgumentNullException.ThrowIfNull(selected);
        DomainJoinDomainSettings domain = DomainJoinDomainCatalog.Require(current, domainId);
        var units = domain.OrganizationalUnits.ToList();
        var names = new HashSet<string>(units.Select(unit => Key(unit.DistinguishedName)), StringComparer.OrdinalIgnoreCase);
        foreach (DomainJoinOrganizationalUnitSettings unit in selected)
        {
            if (unit is null) throw new ArgumentException("An OU is required.", nameof(selected));
            if (names.Add(Key(unit.DistinguishedName))) units.Add(unit);
        }

        return DomainJoinDomainCatalog.Validated(
            DomainJoinDomainCatalog.Replace(current, domain with { OrganizationalUnits = units.ToArray() }), nameof(selected));
    }

    /// <summary>Removes an OU from one domain and clears it as that domain's default; it never validates, so a damaged list can be repaired step by step.</summary>
    public static DomainJoinSettings Remove(DomainJoinSettings current, string domainId, string ouId)
    {
        DomainJoinDomainSettings domain = DomainJoinDomainCatalog.Require(current, domainId);
        return DomainJoinDomainCatalog.Replace(current, domain with
        {
            OrganizationalUnits = domain.OrganizationalUnits
                .Where(unit => !string.Equals(unit.Id, ouId, StringComparison.OrdinalIgnoreCase)).ToArray(),
            DefaultOuId = string.Equals(domain.DefaultOuId, ouId, StringComparison.OrdinalIgnoreCase) ? null : domain.DefaultOuId
        });
    }

    /// <summary>Sets or clears the default OU of one domain; <see langword="null"/> means the domain's default location.</summary>
    public static DomainJoinSettings SetDefault(DomainJoinSettings current, string domainId, string? ouId) =>
        DomainJoinDomainCatalog.Validated(
            DomainJoinDomainCatalog.Replace(current, DomainJoinDomainCatalog.Require(current, domainId) with { DefaultOuId = ouId }), nameof(ouId));

    /// <summary>
    /// Returns the candidates a domain does not list yet, compared as <see cref="Merge"/> compares them, so an
    /// import offers only what it would add. A candidate with an unreadable name is kept for the merge to reject.
    /// </summary>
    public static IReadOnlyList<DomainJoinOrganizationalUnitSettings> ExcludeListed(DomainJoinDomainSettings domain,
        IReadOnlyList<DomainJoinOrganizationalUnitSettings> candidates)
    {
        ArgumentNullException.ThrowIfNull(domain);
        ArgumentNullException.ThrowIfNull(candidates);
        var listed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (DomainJoinOrganizationalUnitSettings unit in domain.OrganizationalUnits)
        {
            if (DistinguishedNameRules.TryParse(unit.DistinguishedName, out ParsedDistinguishedName parsed))
                listed.Add(DistinguishedNameRules.GetComparisonKey(parsed));
        }

        return candidates.Where(candidate => !DistinguishedNameRules.TryParse(candidate.DistinguishedName, out ParsedDistinguishedName parsed) ||
            !listed.Contains(DistinguishedNameRules.GetComparisonKey(parsed))).ToArray();
    }

    private static string Key(string dn) => DistinguishedNameRules.TryParse(dn, out ParsedDistinguishedName parsed)
        ? DistinguishedNameRules.GetComparisonKey(parsed)
        : throw new ArgumentException("The OU distinguished name is invalid.", nameof(dn));
}