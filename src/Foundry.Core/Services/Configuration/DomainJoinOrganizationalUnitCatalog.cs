// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Models.Configuration;

namespace Foundry.Core.Services.Configuration;

/// <summary>Maintains a bounded portable OU catalog without changing existing destination identities.</summary>
public static class DomainJoinOrganizationalUnitCatalog
{
    /// <summary>Merges selected discovered destinations into a validated copy.</summary>
    public static DomainJoinSettings Merge(DomainJoinSettings current, string discoveredDomain,
        IReadOnlyList<DomainJoinOrganizationalUnitSettings> selected)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(selected);
        Validate(current);
        if (!DomainJoinCredentialContext.IsValidDomainName(discoveredDomain))
            throw new ArgumentException("The discovered domain must be a DNS domain.", nameof(discoveredDomain));
        string domain = DomainJoinCredentialContext.CanonicalizeDomainName(discoveredDomain);
        if (!Matches(current.DomainName) || !Matches(current.OuCatalogDomain))
            throw new ArgumentException("The target and catalog must match the discovered computer domain.", nameof(discoveredDomain));
        var units = current.OrganizationalUnits.ToList();
        var names = new HashSet<string>(units.Select(unit => Key(unit.DistinguishedName)), StringComparer.OrdinalIgnoreCase);
        foreach (DomainJoinOrganizationalUnitSettings unit in selected)
        {
            if (unit is null) throw new ArgumentException("A destination is required.", nameof(selected));
            if (names.Add(Key(unit.DistinguishedName))) units.Add(unit);
        }
        DomainJoinSettings result = current with
        {
            DomainName = string.IsNullOrWhiteSpace(current.DomainName) && selected.Count > 0 ? domain : current.DomainName,
            OuCatalogDomain = units.Count > 0 ? domain : current.OuCatalogDomain,
            OrganizationalUnits = units.ToArray()
        };
        Validate(result);
        return result;

        bool Matches(string? value) => string.IsNullOrWhiteSpace(value) ||
            DomainJoinCredentialContext.IsValidDomainName(value) &&
            DomainJoinCredentialContext.CanonicalizeDomainName(value) == domain;
    }

    /// <summary>Removes a destination and clears its default selection, allowing inconsistent drafts to be repaired incrementally.</summary>
    public static DomainJoinSettings Remove(DomainJoinSettings current, string id)
    {
        ArgumentNullException.ThrowIfNull(current);
        DomainJoinOrganizationalUnitSettings[] units = current.OrganizationalUnits
            .Where(unit => !string.Equals(unit.Id, id, StringComparison.OrdinalIgnoreCase)).ToArray();
        DomainJoinSettings result = current with
        {
            OrganizationalUnits = units,
            OuCatalogDomain = units.Length == 0 ? null : current.OuCatalogDomain,
            DefaultOuId = string.Equals(current.DefaultOuId, id, StringComparison.OrdinalIgnoreCase) ? null : current.DefaultOuId,
            AllowOuSelectionDuringDeployment = units.Length > 0 && current.AllowOuSelectionDuringDeployment
        };
        return result;
    }

    private static string Key(string dn) => DistinguishedNameRules.TryParse(dn, out ParsedDistinguishedName parsed)
        ? DistinguishedNameRules.GetComparisonKey(parsed)
        : throw new ArgumentException("The destination distinguished name is invalid.", nameof(dn));

    private static void Validate(DomainJoinSettings settings)
    {
        if (!DomainJoinConfigurationValidator.ValidateMetadata(settings).IsValid)
            throw new ArgumentException("The destination catalog is invalid.", nameof(settings));
    }
}
