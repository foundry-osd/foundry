// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Models.Configuration;

namespace Foundry.Core.Services.Configuration;

/// <summary>Maintains the list of joinable domains and its default, returning validated copies.</summary>
public static class DomainJoinDomainCatalog
{
    /// <summary>Adds a domain. The first domain listed becomes the default.</summary>
    public static DomainJoinSettings Add(DomainJoinSettings current, string domainName, string? accountName)
    {
        ArgumentNullException.ThrowIfNull(current);
        var domain = new DomainJoinDomainSettings
        {
            Id = Guid.NewGuid().ToString("D"),
            DomainName = (domainName ?? string.Empty).Trim(),
            AccountName = NormalizeAccount(accountName)
        };
        return Validated(current with
        {
            Domains = [.. current.Domains, domain],
            DefaultDomainId = current.Domains.Count == 0 ? domain.Id : current.DefaultDomainId
        }, nameof(domainName));
    }

    /// <summary>
    /// Changes a domain's name or dedicated account. The name cannot change while the domain lists OUs, because
    /// they would silently fall outside their domain.
    /// </summary>
    public static DomainJoinSettings Update(DomainJoinSettings current, string domainId, string domainName, string? accountName)
    {
        DomainJoinDomainSettings domain = Require(current, domainId);
        string name = (domainName ?? string.Empty).Trim();
        bool renamed = !string.Equals(DomainJoinCredentialContext.CanonicalizeDomainName(domain.DomainName),
            DomainJoinCredentialContext.CanonicalizeDomainName(name), StringComparison.Ordinal);
        if (renamed && domain.OrganizationalUnits.Count > 0)
            throw new ArgumentException("A domain that lists OUs cannot be renamed.", nameof(domainName));
        return Validated(Replace(current, domain with { DomainName = name, AccountName = NormalizeAccount(accountName) }), nameof(domainName));
    }

    /// <summary>
    /// Changes only a domain's name and keeps its dedicated account. Interactive authoring edits a domain without
    /// showing the account that Zero-touch stored on it.
    /// </summary>
    public static DomainJoinSettings Rename(DomainJoinSettings current, string domainId, string domainName) =>
        Update(current, domainId, domainName, Require(current, domainId).AccountName);

    /// <summary>Removes a domain with its OUs. A removed default moves to the first remaining domain.</summary>
    public static DomainJoinSettings Remove(DomainJoinSettings current, string domainId)
    {
        ArgumentNullException.ThrowIfNull(current);
        DomainJoinDomainSettings[] domains = current.Domains
            .Where(domain => !string.Equals(domain.Id, domainId, StringComparison.OrdinalIgnoreCase)).ToArray();
        bool removedDefault = string.Equals(current.DefaultDomainId, domainId, StringComparison.OrdinalIgnoreCase);
        return current with
        {
            Domains = domains,
            DefaultDomainId = domains.Length == 0 ? null : removedDefault ? domains[0].Id : current.DefaultDomainId
        };
    }

    public static DomainJoinSettings SetDefault(DomainJoinSettings current, string domainId) =>
        Validated(current with { DefaultDomainId = Require(current, domainId).Id }, nameof(domainId));

    internal static DomainJoinDomainSettings Require(DomainJoinSettings current, string domainId)
    {
        ArgumentNullException.ThrowIfNull(current);
        return current.FindDomain(domainId) ?? throw new ArgumentException("The domain is not listed.", nameof(domainId));
    }

    internal static DomainJoinSettings Replace(DomainJoinSettings current, DomainJoinDomainSettings updated) => current with
    {
        Domains = current.Domains.Select(domain => string.Equals(domain.Id, updated.Id, StringComparison.OrdinalIgnoreCase) ? updated : domain).ToArray()
    };

    internal static DomainJoinSettings Validated(DomainJoinSettings result, string parameterName) =>
        DomainJoinConfigurationValidator.ValidateMetadata(result).IsValid
            ? result
            : throw new ArgumentException("The domain list would not be valid.", parameterName);

    private static string? NormalizeAccount(string? accountName) => string.IsNullOrWhiteSpace(accountName) ? null : accountName.Trim();
}
