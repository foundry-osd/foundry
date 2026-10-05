// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Models.Configuration;

namespace Foundry.Core.Services.Configuration;

/// <summary>Validates portable metadata separately from inputs required to build deployment media.</summary>
public static class DomainJoinConfigurationValidator
{
    public const int MaximumOrganizationalUnits = 1024;
    public const int MaximumPasswordUtf8Bytes = 2560;
    public const int MaximumCredentialPayloadBytes = 32 * 1024;
    public const int MaximumResultBytes = 64 * 1024;

    /// <summary>Accepts incomplete secret-free drafts while rejecting unsafe bounds and inconsistent OU lists.</summary>
    public static DomainJoinValidationResult ValidateMetadata(DomainJoinSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var issues = new List<DomainJoinValidationIssue>();
        if (!Enum.IsDefined(settings.Mode)) Add(DomainJoinValidationCode.UnsupportedMode);
        if (!IsBounded(settings.DomainName, 253)) Add(DomainJoinValidationCode.InvalidDomainName);
        if (!IsBounded(settings.AccountName, 512)) Add(DomainJoinValidationCode.InvalidAccountName);
        if (!IsBounded(settings.OuCatalogDomain, 253)) Add(DomainJoinValidationCode.InvalidCatalogDomain);
        if (!IsBounded(settings.DefaultOuId, 128)) Add(DomainJoinValidationCode.InvalidOuId);
        IReadOnlyList<DomainJoinOrganizationalUnitSettings> units = settings.OrganizationalUnits ?? [];
        if (units.Count > MaximumOrganizationalUnits) Add(DomainJoinValidationCode.TooManyOrganizationalUnits);
        if (units.Count == 0 && (settings.AllowOuSelectionDuringDeployment || settings.DefaultOuId is not null))
            Add(DomainJoinValidationCode.OrganizationalUnitsRequired);
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (units.Count > 0)
        {
            if (string.IsNullOrWhiteSpace(settings.OuCatalogDomain)) Add(DomainJoinValidationCode.CatalogDomainRequired);
            else if (!DomainJoinCredentialContext.IsValidDomainName(settings.OuCatalogDomain)) Add(DomainJoinValidationCode.InvalidCatalogDomain);
            if (DomainJoinCredentialContext.IsValidDomainName(settings.DomainName) && !string.Equals(
                DomainJoinCredentialContext.CanonicalizeDomainName(settings.DomainName),
                DomainJoinCredentialContext.CanonicalizeDomainName(settings.OuCatalogDomain), StringComparison.Ordinal))
                Add(DomainJoinValidationCode.CatalogDomainMismatch);
        }

        foreach (DomainJoinOrganizationalUnitSettings unit in units.Take(MaximumOrganizationalUnits))
        {
            if (unit is null) { Add(DomainJoinValidationCode.InvalidOuId); continue; }
            string? issueId = IsRequiredBounded(unit.Id, 128) ? unit.Id : null;
            if (issueId is null) Add(DomainJoinValidationCode.InvalidOuId);
            else if (!ids.Add(unit.Id)) Add(DomainJoinValidationCode.DuplicateOuId, issueId);
            if (!IsRequiredBounded(unit.DisplayName, 120)) Add(DomainJoinValidationCode.InvalidOuDisplayName, issueId);
            if (!DistinguishedNameRules.TryParse(unit.DistinguishedName, out ParsedDistinguishedName parsed) ||
                !DistinguishedNameRules.IsOrganizationalUnit(parsed))
                Add(DomainJoinValidationCode.InvalidDistinguishedName, issueId);
            else
            {
                if (!names.Add(DistinguishedNameRules.GetComparisonKey(parsed))) Add(DomainJoinValidationCode.DuplicateDistinguishedName, issueId);
                if (!DistinguishedNameRules.IsWithinDomain(unit.DistinguishedName, settings.OuCatalogDomain ?? string.Empty))
                    Add(DomainJoinValidationCode.OuOutsideDomain, issueId);
            }
        }

        if (settings.DefaultOuId is not null && !ids.Contains(settings.DefaultOuId)) Add(DomainJoinValidationCode.DefaultOuMissing);
        return new(issues.Count == 0, issues);

        void Add(DomainJoinValidationCode code, string? id = null) => issues.Add(new(code, id));
    }

    /// <summary>Requires automatic credential inputs only when that mode is active; interactive media needs no password.</summary>
    public static DomainJoinValidationResult EvaluateReadiness(DomainJoinSettings settings, bool hasMatchingPassword, bool isMediaProtected)
    {
        DomainJoinValidationResult metadata = ValidateMetadata(settings);
        if (!settings.IsEnabled) return metadata;
        var issues = metadata.Issues.ToList();
        if (!string.IsNullOrWhiteSpace(settings.DomainName) && !DomainJoinCredentialContext.IsValidDomainName(settings.DomainName))
            issues.Add(new(DomainJoinValidationCode.InvalidDomainName));
        if (settings.Mode == DomainJoinMode.Automatic)
        {
            if (string.IsNullOrWhiteSpace(settings.DomainName)) issues.Add(new(DomainJoinValidationCode.DomainNameRequired));
            if (string.IsNullOrWhiteSpace(settings.AccountName)) issues.Add(new(DomainJoinValidationCode.AccountNameRequired));
            else if (!IsQualifiedAccount(settings.AccountName)) issues.Add(new(DomainJoinValidationCode.QualifiedAccountRequired));
            if (!hasMatchingPassword) issues.Add(new(DomainJoinValidationCode.PasswordRequired));
            if (!isMediaProtected) issues.Add(new(DomainJoinValidationCode.MediaProtectionRequired));
        }

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

/// <summary>Identifies the rule and optional authored OU that need attention without echoing input.</summary>
public sealed record DomainJoinValidationIssue(DomainJoinValidationCode Code, string? OuId = null);

/// <summary>Stable validation codes for authoring and runtime presentation.</summary>
public enum DomainJoinValidationCode
{
    UnsupportedMode, InvalidDomainName, InvalidAccountName, InvalidCatalogDomain, TooManyOrganizationalUnits,
    InvalidOuId, InvalidOuDisplayName, InvalidDistinguishedName, DuplicateOuId, DuplicateDistinguishedName,
    CatalogDomainRequired, CatalogDomainMismatch, OuOutsideDomain, DefaultOuMissing, OrganizationalUnitsRequired,
    DomainNameRequired, AccountNameRequired, QualifiedAccountRequired, PasswordRequired, MediaProtectionRequired
}
