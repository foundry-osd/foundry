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
        if (settings is null || !settings.IsEnabled || settings.OrganizationalUnits is null || !ComputerNameRules.IsValid(computerName) ||
            !DomainJoinConfigurationValidator.ValidateMetadata(ToAuthored(settings)).IsValid)
            return DomainJoinPreparationResult.Invalid(DomainJoinPreparationFailure.MetadataInvalid);
        try
        {
            return settings.Mode == DomainJoinMode.Automatic
                ? PrepareAutomatic(settings, computerName, submission)
                : PrepareInteractive(settings, computerName, submission);
        }
        catch (Exception ex) when (ex is CryptographicException or InvalidDataException or ArgumentException or InvalidOperationException or FormatException)
        {
            return DomainJoinPreparationResult.Invalid(DomainJoinPreparationFailure.CredentialsInvalid);
        }
    }

    private static DomainJoinPreparationResult PrepareInteractive(DeployDomainJoinSettings settings, string computerName, DomainJoinSubmission? submission)
    {
        if (submission is null) return DomainJoinPreparationResult.Invalid(DomainJoinPreparationFailure.CredentialsInvalid);
        string? organizationalUnit = ResolveOrganizationalUnit(settings, submission.DomainName, submission.SelectedOuId, submission.TypedOuDistinguishedName);
        return DomainJoinPreparationResult.Ready(new(new(submission.DomainName, submission.AccountName),
            computerName, organizationalUnit, submission.Password.Span));
    }

    private DomainJoinPreparationResult PrepareAutomatic(DeployDomainJoinSettings settings, string computerName, DomainJoinSubmission? submission)
    {
        if (!keys.IsUnlocked || settings.EncryptedCredentials is null)
            return DomainJoinPreparationResult.Invalid(DomainJoinPreparationFailure.UnlockRequired);
        if (settings.EncryptedCredentials.Ciphertext is null ||
            settings.EncryptedCredentials.Ciphertext.Length > (DomainJoinConfigurationValidator.MaximumCredentialPayloadBytes + 2) / 3 * 4 ||
            settings.EncryptedCredentials.Nonce is null || settings.EncryptedCredentials.Nonce.Length > 24 ||
            settings.EncryptedCredentials.Tag is null || settings.EncryptedCredentials.Tag.Length > 24)
            return DomainJoinPreparationResult.Invalid(DomainJoinPreparationFailure.CredentialsInvalid);
        var expected = new DomainJoinCredentialContext(settings.DomainName ?? "", settings.AccountName ?? "");
        byte[]? key = null;
        byte[]? plaintext = null;
        DomainJoinCredentialPayload? payload = null;
        try
        {
            key = keys.GetKeyCopy();
            plaintext = MediaSecretEnvelopeProtector.DecryptBytes(settings.EncryptedCredentials, key, MediaSecretEnvelopeProtector.DeploymentKeyId);
            payload = DomainJoinCredentialPayloadCodec.Decode(plaintext, expected);
        }
        finally
        {
            if (key is not null) CryptographicOperations.ZeroMemory(key);
            if (plaintext is not null) CryptographicOperations.ZeroMemory(plaintext);
        }
        using (payload)
        {
            string? selectedId = settings.AllowOuSelectionDuringDeployment ? submission?.SelectedOuId : settings.DefaultOuId;
            return DomainJoinPreparationResult.Ready(new(payload.Context, computerName,
                ResolveOrganizationalUnit(settings, settings.DomainName!, selectedId, submission?.TypedOuDistinguishedName), payload.Password.Span));
        }
    }

    /// <summary>Determines whether the saved OU list can be offered for the submitted domain.</summary>
    internal static bool HasCompatibleCatalog(DeployDomainJoinSettings settings, string? domain) =>
        settings.OrganizationalUnits.Count > 0 && DomainJoinCredentialContext.IsValidDomainName(domain) &&
        string.Equals(DomainJoinCredentialContext.CanonicalizeDomainName(domain),
            DomainJoinCredentialContext.CanonicalizeDomainName(settings.OuCatalogDomain ?? ""), StringComparison.Ordinal);

    /// <summary>Resolves the authored default OU for the domain; <see langword="null"/> means the domain default location.</summary>
    internal static DomainJoinOrganizationalUnitSettings? ResolveDefaultOrganizationalUnit(DeployDomainJoinSettings settings, string? domain) =>
        settings.DefaultOuId is { } id && HasCompatibleCatalog(settings, domain)
            ? settings.OrganizationalUnits.FirstOrDefault(unit => string.Equals(unit.Id, id, StringComparison.OrdinalIgnoreCase))
            : null;


    /// <summary>Determines whether the join applies to the selected image; positively unsupported editions skip it.</summary>
    internal static bool IsRequiredFor(DeployDomainJoinSettings? settings, OperatingSystemMetadata? operatingSystem)
    {
        if (settings?.IsEnabled != true) return false;
        string? edition = operatingSystem?.Edition;
        string? editionId = operatingSystem is OperatingSystemCatalogItem ? WindowsEditionCatalog.Find(edition)?.EditionId ?? edition : edition;
        return DomainJoinEditionRules.Evaluate(editionId) != DomainJoinEditionSupport.Unsupported;
    }

    /// <summary>
    /// Resolves the OU the join will use; <see langword="null"/> means the domain's default location. A typed OU is
    /// accepted only in Interactive mode and only when the saved list does not apply to the domain.
    /// </summary>
    internal static string? ResolveOrganizationalUnit(DeployDomainJoinSettings settings, string domain, string? selectedId, string? typedOuDistinguishedName)
    {
        bool hasTypedOu = !string.IsNullOrWhiteSpace(typedOuDistinguishedName);
        if (!HasCompatibleCatalog(settings, domain))
        {
            if (settings.Mode == DomainJoinMode.Automatic && hasTypedOu)
                throw new InvalidDataException("A Zero-touch OU must come from the saved OU list.");
            return hasTypedOu ? typedOuDistinguishedName : null;
        }
        if (hasTypedOu)
            throw new InvalidDataException("The OU must come from the saved OU list.");
        string? id = settings.AllowOuSelectionDuringDeployment ? selectedId : settings.DefaultOuId;
        if (id is null && !settings.AllowOuSelectionDuringDeployment) return null;
        return settings.OrganizationalUnits.FirstOrDefault(unit =>
            string.Equals(unit.Id, id, StringComparison.OrdinalIgnoreCase))?.DistinguishedName
            ?? throw new InvalidDataException("The selected OU is not in the saved OU list.");
    }

    internal static DomainJoinSettings ToAuthored(DeployDomainJoinSettings settings) => new()
    {
        IsEnabled = settings.IsEnabled,
        Mode = settings.Mode,
        DomainName = settings.DomainName,
        AccountName = settings.AccountName,
        OuCatalogDomain = settings.OuCatalogDomain,
        OrganizationalUnits = settings.OrganizationalUnits,
        DefaultOuId = settings.DefaultOuId,
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
