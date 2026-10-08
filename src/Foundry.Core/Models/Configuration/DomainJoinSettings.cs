// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Core.Models.Configuration;

/// <summary>Stores the portable list of joinable domains without persisting credentials.</summary>
public sealed record DomainJoinSettings
{
    public bool IsEnabled { get; init; }
    public DomainJoinMode Mode { get; init; } = DomainJoinMode.Interactive;
    /// <summary>Gets the account used by every domain that has no account of its own. Zero-touch only.</summary>
    public string? SharedAccountName { get; init; }
    public IReadOnlyList<DomainJoinDomainSettings> Domains { get; init; } = [];
    /// <summary>Gets the domain joined when the technician does not choose one.</summary>
    public string? DefaultDomainId { get; init; }
    public bool AllowDomainSelectionDuringDeployment { get; init; }
    public bool AllowOuSelectionDuringDeployment { get; init; }

    /// <summary>Returns the account a domain joins with: its own, or the shared one.</summary>
    public string? ResolveAccountName(DomainJoinDomainSettings domain)
    {
        ArgumentNullException.ThrowIfNull(domain);
        return string.IsNullOrWhiteSpace(domain.AccountName) ? SharedAccountName : domain.AccountName;
    }

    public DomainJoinDomainSettings? FindDomain(string? id) => id is null
        ? null
        : Domains.FirstOrDefault(domain => string.Equals(domain.Id, id, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Returns the distinct canonical accounts that may own a password: the shared account, even before a domain
    /// uses it, and every dedicated account. Only Zero-touch stores accounts, so every other state references none.
    /// </summary>
    public IReadOnlyList<string> GetReferencedAccountNames() => !IsEnabled || Mode != DomainJoinMode.Automatic
        ? []
        : Domains.Select(ResolveAccountName).Prepend(SharedAccountName)
            .Where(account => !string.IsNullOrWhiteSpace(account))
            .Select(account => DomainJoinCredentialContext.CanonicalizeAccountName(account))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
}
