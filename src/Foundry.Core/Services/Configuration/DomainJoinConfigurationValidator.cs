// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Models.Configuration;

namespace Foundry.Core.Services.Configuration;

/// <summary>Validates portable metadata separately from inputs required to build deployment media.</summary>
public static class DomainJoinConfigurationValidator
{
    public const int MaximumDomains = 32;
    public const int MaximumOrganizationalUnits = 1024;
    public const int MaximumPasswordUtf8Bytes = 2560;
    public const int MaximumCredentialPayloadBytes = 32 * 1024;
    public const int MaximumResultBytes = 64 * 1024;

    /// <summary>Rejects unsafe bounds and inconsistent domain or OU lists; credentials are not examined here.</summary>
    public static DomainJoinValidationResult ValidateMetadata(DomainJoinSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var issues = new List<DomainJoinValidationIssue>();
        if (!Enum.IsDefined(settings.Mode)) Add(DomainJoinValidationCode.UnsupportedMode);
        if (!IsBounded(settings.SharedAccountName, 512)) Add(DomainJoinValidationCode.InvalidAccountName);
        if (!IsBounded(settings.DefaultDomainId, 128)) Add(DomainJoinValidationCode.InvalidDomainId);
        IReadOnlyList<DomainJoinDomainSettings> domains = settings.Domains ?? [];
        if (domains.Count > MaximumDomains) Add(DomainJoinValidationCode.TooManyDomains);
        var domainIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var domainNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (DomainJoinDomainSettings domain in domains.Take(MaximumDomains))
        {
            if (domain is null) { Add(DomainJoinValidationCode.InvalidDomainId); continue; }
            string? id = IsRequiredBounded(domain.Id, 128) ? domain.Id : null;
            if (id is null) Add(DomainJoinValidationCode.InvalidDomainId);
            else if (!domainIds.Add(id)) Add(DomainJoinValidationCode.DuplicateDomainId, id);
            ValidateDomain(domain, id, domainNames, issues);
        }

        // A listed domain always has a default; without domains there is nothing a default could name.
        if (domains.Count > 0 ? settings.DefaultDomainId is null || !domainIds.Contains(settings.DefaultDomainId) : settings.DefaultDomainId is not null)
            Add(DomainJoinValidationCode.DefaultDomainMissing);
        return new(issues.Count == 0, issues);

        void Add(DomainJoinValidationCode code, string? domainId = null) => issues.Add(new(code, domainId));
    }

    private static void ValidateDomain(DomainJoinDomainSettings domain, string? id, HashSet<string> domainNames, List<DomainJoinValidationIssue> issues)
    {
        if (!IsBounded(domain.DomainName, 253) || !DomainJoinCredentialContext.IsValidDomainName(domain.DomainName))
            Add(DomainJoinValidationCode.InvalidDomainName);
        // Compared in canonical form, so case and a trailing dot do not make two domains out of one.
        string canonicalName = IsBounded(domain.DomainName, 253) ? DomainJoinCredentialContext.CanonicalizeDomainName(domain.DomainName) : string.Empty;
        if (canonicalName.Length > 0 && !domainNames.Add(canonicalName)) Add(DomainJoinValidationCode.DuplicateDomainName);
        if (!IsBounded(domain.AccountName, 512)) Add(DomainJoinValidationCode.InvalidAccountName);
        if (!IsBounded(domain.DefaultOuId, 128)) Add(DomainJoinValidationCode.InvalidOuId);
        IReadOnlyList<DomainJoinOrganizationalUnitSettings> units = domain.OrganizationalUnits ?? [];
        if (units.Count > MaximumOrganizationalUnits) Add(DomainJoinValidationCode.TooManyOrganizationalUnits);
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (DomainJoinOrganizationalUnitSettings unit in units.Take(MaximumOrganizationalUnits))
        {
            if (unit is null) { Add(DomainJoinValidationCode.InvalidOuId); continue; }
            if (!IsRequiredBounded(unit.Id, 128)) Add(DomainJoinValidationCode.InvalidOuId);
            else if (!ids.Add(unit.Id)) Add(DomainJoinValidationCode.DuplicateOuId);
            if (!IsRequiredBounded(unit.DisplayName, 120)) Add(DomainJoinValidationCode.InvalidOuDisplayName);
            if (!DistinguishedNameRules.TryParse(unit.DistinguishedName, out ParsedDistinguishedName parsed) ||
                !DistinguishedNameRules.IsOrganizationalUnit(parsed))
                Add(DomainJoinValidationCode.InvalidDistinguishedName);
            else
            {
                if (!names.Add(DistinguishedNameRules.GetComparisonKey(parsed))) Add(DomainJoinValidationCode.DuplicateDistinguishedName);
                if (!DistinguishedNameRules.IsWithinDomain(unit.DistinguishedName, domain.DomainName ?? string.Empty))
                    Add(DomainJoinValidationCode.OuOutsideDomain);
            }
        }

        if (domain.DefaultOuId is not null && !ids.Contains(domain.DefaultOuId)) Add(DomainJoinValidationCode.DefaultOuMissing);

        void Add(DomainJoinValidationCode code) => issues.Add(new(code, id));
    }

    /// <summary>
    /// Adds what media creation needs on top of valid metadata. Zero-touch needs a domain, and for each domain a
    /// qualified account that owns a password; Interactive needs nothing more because the technician supplies it.
    /// </summary>
    /// <param name="hasPassword">Tells whether the given resolved account owns a password in this session.</param>
    public static DomainJoinValidationResult EvaluateReadiness(DomainJoinSettings settings, Func<string, bool> hasPassword, bool isMediaProtected)
    {
        ArgumentNullException.ThrowIfNull(hasPassword);
        DomainJoinValidationResult metadata = ValidateMetadata(settings);
        if (!settings.IsEnabled || settings.Mode != DomainJoinMode.Automatic) return metadata;
        var issues = metadata.Issues.ToList();
        IReadOnlyList<DomainJoinDomainSettings> domains = settings.Domains ?? [];
        if (domains.Count == 0) issues.Add(new(DomainJoinValidationCode.DomainsRequired));
        foreach (DomainJoinDomainSettings domain in domains.Take(MaximumDomains))
        {
            if (domain is null) continue;
            string? account = settings.ResolveAccountName(domain);
            if (string.IsNullOrWhiteSpace(account)) issues.Add(new(DomainJoinValidationCode.SharedAccountRequired, domain.Id));
            else if (!IsQualifiedAccount(account)) issues.Add(new(DomainJoinValidationCode.QualifiedAccountRequired, domain.Id));
            else if (!hasPassword(account)) issues.Add(new(DomainJoinValidationCode.PasswordRequired, domain.Id));
        }

        if (!isMediaProtected) issues.Add(new(DomainJoinValidationCode.MediaProtectionRequired));
        return new(issues.Count == 0, issues);
    }

    /// <summary>Rejects contradictory saved documents rather than choosing a silent import winner.</summary>
    public static void ThrowIfProvisioningModesConflict(AutopilotSettings autopilot, DomainJoinSettings domainJoin)
    {
        ArgumentNullException.ThrowIfNull(autopilot);
        ArgumentNullException.ThrowIfNull(domainJoin);
        if (autopilot.IsEnabled && domainJoin.IsEnabled)
            throw new InvalidOperationException("Autopilot and domain joining cannot both be enabled.");
    }

    /// <summary>Accepts explicit UPN or down-level logon names supported by online joining.</summary>
    public static bool IsQualifiedAccount(string? value)
    {
        if (!IsRequiredBounded(value, 512) || value!.Any(char.IsControl)) return false;
        string account = value!.Trim();
        if (account.Any(char.IsWhiteSpace)) return false;
        string[] downLevel = account.Split('\\');
        if (downLevel.Length == 2)
            return downLevel.All(part => part.Length > 0 && !part.Contains('@') && !part.Contains('/'));
        if (downLevel.Length != 1) return false;
        string[] upn = account.Split('@');
        return upn.Length == 2 && upn[0].Length > 0 && !upn[0].Contains('/') && DomainJoinCredentialContext.IsValidDomainName(upn[1]);
    }

    private static bool IsBounded(string? value, int maximum) => value is null || (value.Length <= maximum && !value.Contains('\0'));
    private static bool IsRequiredBounded(string? value, int maximum) => !string.IsNullOrWhiteSpace(value) && IsBounded(value, maximum);
}

/// <summary>Contains non-secret validation outcomes.</summary>
public sealed record DomainJoinValidationResult(bool IsValid, IReadOnlyList<DomainJoinValidationIssue> Issues);

/// <summary>Identifies the rule that needs attention, and the domain it concerns when there is one, without echoing input.</summary>
public sealed record DomainJoinValidationIssue(DomainJoinValidationCode Code, string? DomainId = null);

/// <summary>Stable validation codes for authoring and runtime presentation.</summary>
public enum DomainJoinValidationCode
{
    UnsupportedMode, InvalidDomainName, InvalidAccountName, TooManyOrganizationalUnits,
    InvalidOuId, InvalidOuDisplayName, InvalidDistinguishedName, DuplicateOuId, DuplicateDistinguishedName,
    OuOutsideDomain, DefaultOuMissing, DomainsRequired, TooManyDomains, InvalidDomainId, DuplicateDomainId,
    DuplicateDomainName, DefaultDomainMissing, SharedAccountRequired, QualifiedAccountRequired, PasswordRequired,
    MediaProtectionRequired
}
