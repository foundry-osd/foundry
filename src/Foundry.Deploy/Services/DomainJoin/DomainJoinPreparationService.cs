// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.IO;
using System.Security.Cryptography;
using Foundry.Core.Models.Configuration;
using Foundry.Core.Models.Configuration.Deploy;
using Foundry.Core.Services.Autopilot;
using Foundry.Core.Services.Configuration;
using Foundry.Deploy.Models;
using Foundry.Deploy.Services.Security;

namespace Foundry.Deploy.Services.DomainJoin;

/// <summary>Provides owned input to the pre-confirmation launch boundary.</summary>
public interface IDomainJoinPreparationService
{
    /// <summary>Combines the media configuration with what the technician entered on the Domain Join wizard step.</summary>
    DomainJoinPreparationResult Prepare(DeployDomainJoinSettings settings, string computerName, DomainJoinSubmission? submission);
}

/// <summary>Resolves owned inputs without contacting AD or acquiring another media unlock.</summary>
public sealed class DomainJoinPreparationService(IDeploymentSecretKeySession keys) : IDomainJoinPreparationService
{
    /// <summary>Requires a concrete final name and validated runtime metadata before accepting credentials.</summary>
    public DomainJoinPreparationResult Prepare(DeployDomainJoinSettings settings, string computerName, DomainJoinSubmission? submission)
    {
        if (settings is null || !settings.IsEnabled || settings.Domains is null || !ComputerNameRules.IsValid(computerName) ||
            !DomainJoinConfigurationValidator.ValidateMetadata(ToAuthored(settings)).IsValid)
            return DomainJoinPreparationResult.Invalid(DomainJoinPreparationFailure.MetadataInvalid);
        try
        {
            DeployDomainJoinDomainSettings? domain = ResolveDomain(settings, submission?.SelectedDomainId);
            return settings.Mode == DomainJoinMode.Automatic
                ? PrepareAutomatic(settings, domain, computerName, submission)
                : PrepareInteractive(settings, domain, computerName, submission);
        }
        catch (Exception ex) when (ex is CryptographicException or InvalidDataException or ArgumentException or InvalidOperationException or FormatException)
        {
            return DomainJoinPreparationResult.Invalid(DomainJoinPreparationFailure.CredentialsInvalid);
        }
    }

    private static DomainJoinPreparationResult PrepareInteractive(DeployDomainJoinSettings settings, DeployDomainJoinDomainSettings? domain,
        string computerName, DomainJoinSubmission? submission)
    {
        if (submission is null) return DomainJoinPreparationResult.Invalid(DomainJoinPreparationFailure.CredentialsInvalid);
        string domainName = submission.DomainName;
        if (settings.Domains.Count > 0)
        {
            // A media that lists domains only joins one of them; a different typed name is never accepted.
            if (domain is null || !string.IsNullOrWhiteSpace(domainName) && !SameDomain(domainName, domain.DomainName))
                throw new InvalidDataException("The domain must be one of the listed domains.");
            domainName = domain.DomainName;
        }

        string? organizationalUnit = ResolveOrganizationalUnit(settings, domain, submission.SelectedOuId, submission.TypedOuDistinguishedName);
        return DomainJoinPreparationResult.Ready(new(new(domainName, submission.AccountName),
            computerName, organizationalUnit, submission.Password.Span));
    }

    private DomainJoinPreparationResult PrepareAutomatic(DeployDomainJoinSettings settings, DeployDomainJoinDomainSettings? domain,
        string computerName, DomainJoinSubmission? submission)
    {
        if (domain is null) return DomainJoinPreparationResult.Invalid(DomainJoinPreparationFailure.MetadataInvalid);
        SecretEnvelope? envelope = domain.EncryptedCredentials;
        if (!keys.IsUnlocked || envelope is null)
            return DomainJoinPreparationResult.Invalid(DomainJoinPreparationFailure.UnlockRequired);
        if (envelope.Ciphertext is null ||
            envelope.Ciphertext.Length > (DomainJoinConfigurationValidator.MaximumCredentialPayloadBytes + 2) / 3 * 4 ||
            envelope.Nonce is null || envelope.Nonce.Length > 24 ||
            envelope.Tag is null || envelope.Tag.Length > 24)
            return DomainJoinPreparationResult.Invalid(DomainJoinPreparationFailure.CredentialsInvalid);
        // The payload only decodes for the domain and account it was written for.
        var expected = new DomainJoinCredentialContext(domain.DomainName, domain.AccountName ?? "");
        byte[]? key = null;
        byte[]? plaintext = null;
        DomainJoinCredentialPayload? payload = null;
        try
        {
            key = keys.GetKeyCopy();
            plaintext = MediaSecretEnvelopeProtector.DecryptBytes(envelope, key, MediaSecretEnvelopeProtector.DeploymentKeyId);
            payload = DomainJoinCredentialPayloadCodec.Decode(plaintext, expected);
        }
        finally
        {
            if (key is not null) CryptographicOperations.ZeroMemory(key);
            if (plaintext is not null) CryptographicOperations.ZeroMemory(plaintext);
        }
        using (payload)
        {
            return DomainJoinPreparationResult.Ready(new(payload.Context, computerName,
                ResolveOrganizationalUnit(settings, domain, submission?.SelectedOuId, submission?.TypedOuDistinguishedName), payload.Password.Span));
        }
    }

    /// <summary>
    /// Returns the domain the join will use: the technician's choice when the media allows one, otherwise the
    /// default. It returns <see langword="null"/> when the media lists no domain or the choice is not listed.
    /// </summary>
    internal static DeployDomainJoinDomainSettings? ResolveDomain(DeployDomainJoinSettings settings, string? selectedDomainId)
    {
        string? id = settings.AllowDomainSelectionDuringDeployment && selectedDomainId is not null ? selectedDomainId : settings.DefaultDomainId;
        return id is null ? null : settings.Domains.FirstOrDefault(domain => string.Equals(domain.Id, id, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Classifies the frozen domain as the authored default, another listed domain, a typed domain, or none.</summary>
    internal static DomainJoinDomainSource ResolveDomainSource(DeployDomainJoinSettings settings, DomainJoinDeploymentIntent? intent)
    {
        if (string.IsNullOrWhiteSpace(intent?.DomainName)) return DomainJoinDomainSource.None;
        if (settings.Domains.Count == 0) return DomainJoinDomainSource.Typed;
        DeployDomainJoinDomainSettings? defaultDomain = ResolveDomain(settings, null);
        return defaultDomain is not null && SameDomain(defaultDomain.DomainName, intent.DomainName) ? DomainJoinDomainSource.Default : DomainJoinDomainSource.Selected;
    }

    /// <summary>Classifies the frozen target OU as the domain's default, another listed OU, a typed OU, or none.</summary>
    internal static DomainJoinOuSource ResolveOuSource(DeployDomainJoinDomainSettings? domain, DomainJoinDeploymentIntent? intent)
    {
        if (string.IsNullOrWhiteSpace(intent?.TargetOuDn)) return DomainJoinOuSource.None;
        if (domain is null || domain.OrganizationalUnits.Count == 0) return DomainJoinOuSource.Typed;
        string? defaultDn = ResolveDefaultOrganizationalUnit(domain)?.DistinguishedName;
        return string.Equals(defaultDn, intent.TargetOuDn, StringComparison.OrdinalIgnoreCase) ? DomainJoinOuSource.Default : DomainJoinOuSource.Selected;
    }

    /// <summary>Resolves a domain's default OU; <see langword="null"/> means the domain's default location.</summary>
    internal static DomainJoinOrganizationalUnitSettings? ResolveDefaultOrganizationalUnit(DeployDomainJoinDomainSettings? domain) =>
        domain?.DefaultOuId is { } id
            ? domain.OrganizationalUnits.FirstOrDefault(unit => string.Equals(unit.Id, id, StringComparison.OrdinalIgnoreCase))
            : null;

    private static bool SameDomain(string first, string second) => string.Equals(
        DomainJoinCredentialContext.CanonicalizeDomainName(first), DomainJoinCredentialContext.CanonicalizeDomainName(second), StringComparison.Ordinal);

    /// <summary>Determines whether the join applies to the selected image; positively unsupported editions skip it.</summary>
    internal static bool IsRequiredFor(DeployDomainJoinSettings? settings, OperatingSystemMetadata? operatingSystem)
    {
        if (settings?.IsEnabled != true) return false;
        string? edition = operatingSystem?.Edition;
        string? editionId = operatingSystem is OperatingSystemCatalogItem ? WindowsEditionCatalog.Find(edition)?.EditionId ?? edition : edition;
        return DomainJoinEditionRules.Evaluate(editionId) != DomainJoinEditionSupport.Unsupported;
    }

    /// <summary>
    /// Resolves the OU the join will use within the retained domain; <see langword="null"/> means the domain's
    /// default location. A typed OU is accepted only in Interactive mode and only when the domain lists no OU.
    /// </summary>
    internal static string? ResolveOrganizationalUnit(DeployDomainJoinSettings settings, DeployDomainJoinDomainSettings? domain,
        string? selectedId, string? typedOuDistinguishedName)
    {
        bool hasTypedOu = !string.IsNullOrWhiteSpace(typedOuDistinguishedName);
        IReadOnlyList<DomainJoinOrganizationalUnitSettings> units = domain?.OrganizationalUnits ?? [];
        if (units.Count == 0)
        {
            if (settings.Mode == DomainJoinMode.Automatic && hasTypedOu)
                throw new InvalidDataException("A Zero-touch OU must come from the saved OU list.");
            return hasTypedOu ? typedOuDistinguishedName : null;
        }
        if (hasTypedOu)
            throw new InvalidDataException("The OU must come from the saved OU list.");
        string? id = settings.AllowOuSelectionDuringDeployment ? selectedId : domain!.DefaultOuId;
        if (id is null && !settings.AllowOuSelectionDuringDeployment) return null;
        return units.FirstOrDefault(unit =>
            string.Equals(unit.Id, id, StringComparison.OrdinalIgnoreCase))?.DistinguishedName
            ?? throw new InvalidDataException("The selected OU is not in the saved OU list.");
    }

    /// <summary>Maps the media shape back to the authoring shape so both sides validate with the same rules.</summary>
    internal static DomainJoinSettings ToAuthored(DeployDomainJoinSettings settings) => new()
    {
        IsEnabled = settings.IsEnabled,
        Mode = settings.Mode,
        Domains = settings.Domains.Select(domain => new DomainJoinDomainSettings
        {
            Id = domain.Id,
            DomainName = domain.DomainName,
            AccountName = domain.AccountName,
            OrganizationalUnits = domain.OrganizationalUnits ?? [],
            DefaultOuId = domain.DefaultOuId
        }).ToArray(),
        DefaultDomainId = settings.DefaultDomainId,
        AllowDomainSelectionDuringDeployment = settings.AllowDomainSelectionDuringDeployment,
        AllowOuSelectionDuringDeployment = settings.AllowOuSelectionDuringDeployment
    };
}
/// <summary>Defines stable non-secret launch failures; exception text never crosses preparation boundaries.</summary>
public enum DomainJoinPreparationFailure { MetadataInvalid, CredentialsInvalid, UnlockRequired }
/// <summary>Only a ready result authorizes continuing to destructive confirmation.</summary>
public enum DomainJoinPreparationStatus { Ready, Invalid }

/// <summary>Owns prepared credentials until transferred into the launch result.</summary>
public sealed class DomainJoinPreparationResult : IDisposable
{
    private DomainJoinPreparedInput? input;
    private DomainJoinPreparationResult(DomainJoinPreparationStatus status, DomainJoinPreparedInput? input, DomainJoinPreparationFailure? failureCode)
    { Status = status; this.input = input; FailureCode = failureCode; }
    public DomainJoinPreparationStatus Status { get; }
    internal DomainJoinPreparedInput? Input => input;
    public DomainJoinPreparationFailure? FailureCode { get; }
    internal DomainJoinPreparedInput? TakeInput() => Interlocked.Exchange(ref input, null);
    internal static DomainJoinPreparationResult Ready(DomainJoinPreparedInput input) => new(DomainJoinPreparationStatus.Ready, input, null);

    internal static DomainJoinPreparationResult Invalid(DomainJoinPreparationFailure code) => new(DomainJoinPreparationStatus.Invalid, null, code);
    public void Dispose() => TakeInput()?.Dispose();
}
