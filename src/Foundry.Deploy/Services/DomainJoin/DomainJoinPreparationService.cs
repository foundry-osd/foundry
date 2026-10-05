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
    DomainJoinPreparationResult Prepare(DeployDomainJoinSettings settings, string computerName);
}

/// <summary>Resolves owned inputs without contacting AD or acquiring another media unlock.</summary>
public sealed class DomainJoinPreparationService(IDomainJoinDialogService dialogs, IDeploymentSecretKeySession keys) : IDomainJoinPreparationService
{
    /// <summary>Requires a concrete final name and validated runtime metadata before collecting credentials.</summary>
    public DomainJoinPreparationResult Prepare(DeployDomainJoinSettings settings, string computerName)
    {
        if (settings is null || !settings.IsEnabled || settings.OrganizationalUnits is null || !ComputerNameRules.IsValid(computerName) ||
            !DomainJoinConfigurationValidator.ValidateMetadata(ToAuthored(settings)).IsValid)
            return DomainJoinPreparationResult.Invalid(DomainJoinPreparationFailure.MetadataInvalid);
        try
        {
            return settings.Mode == DomainJoinMode.Automatic
                ? PrepareAutomatic(settings, computerName)
                : PrepareInteractive(settings, computerName);
        }
        catch (Exception ex) when (ex is CryptographicException or InvalidDataException or ArgumentException or InvalidOperationException or FormatException)
        {
            return DomainJoinPreparationResult.Invalid(DomainJoinPreparationFailure.CredentialsInvalid);
        }
    }

    private DomainJoinPreparationResult PrepareInteractive(DeployDomainJoinSettings settings, string computerName)
    {
        using DomainJoinDialogResult? submitted = dialogs.Show(settings, requiresCredentials: true);
        if (submitted is null) return DomainJoinPreparationResult.Canceled();
        string? destination = ResolveDestination(settings, submitted.DomainName, submitted.SelectedOuId, submitted.DestinationDn);
        return DomainJoinPreparationResult.Ready(new(new(submitted.DomainName, submitted.AccountName),
            computerName, destination, submitted.Password.Span));
    }

    private DomainJoinPreparationResult PrepareAutomatic(DeployDomainJoinSettings settings, string computerName)
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
            string? selectedId = settings.DefaultOuId;
            string? destinationDn = null;
            if (settings.AllowOuSelectionDuringDeployment)
            {
                using DomainJoinDialogResult? submitted = dialogs.Show(settings, requiresCredentials: false);
                if (submitted is null) return DomainJoinPreparationResult.Canceled();
                selectedId = submitted.SelectedOuId;
                destinationDn = submitted.DestinationDn;
            }
            return DomainJoinPreparationResult.Ready(new(payload.Context, computerName,
                ResolveDestination(settings, settings.DomainName!, selectedId, destinationDn), payload.Password.Span));
        }
    }

    /// <summary>Determines whether authored destinations can be offered for the submitted domain.</summary>
    internal static bool HasCompatibleCatalog(DeployDomainJoinSettings settings, string? domain) =>
        settings.OrganizationalUnits.Count > 0 && DomainJoinCredentialContext.IsValidDomainName(domain) &&
        string.Equals(DomainJoinCredentialContext.CanonicalizeDomainName(domain),
            DomainJoinCredentialContext.CanonicalizeDomainName(settings.OuCatalogDomain ?? ""), StringComparison.Ordinal);

    /// <summary>Resolves the authored default OU for the domain; <see langword="null"/> means the domain default location.</summary>
    internal static DomainJoinOrganizationalUnitSettings? ResolveDefaultOrganizationalUnit(DeployDomainJoinSettings settings, string? domain) =>
        settings.DefaultOuId is { } id && HasCompatibleCatalog(settings, domain)
            ? settings.OrganizationalUnits.FirstOrDefault(unit => string.Equals(unit.Id, id, StringComparison.OrdinalIgnoreCase))
            : null;

    /// <summary>Determines whether the OU is only known at launch because the domain or the OU itself is asked then.</summary>
    internal static bool IsOrganizationalUnitChosenAtLaunch(DeployDomainJoinSettings settings) =>
        !DomainJoinCredentialContext.IsValidDomainName(settings.DomainName) ||
        settings.AllowOuSelectionDuringDeployment && HasCompatibleCatalog(settings, settings.DomainName);

    /// <summary>Determines whether the join applies to the selected image; positively unsupported editions skip it.</summary>
    internal static bool IsRequiredFor(DeployDomainJoinSettings? settings, OperatingSystemMetadata? operatingSystem)
    {
        if (settings?.IsEnabled != true) return false;
        string? edition = operatingSystem?.Edition;
        string? editionId = operatingSystem is OperatingSystemCatalogItem ? WindowsEditionCatalog.Find(edition)?.EditionId ?? edition : edition;
        return DomainJoinEditionRules.Evaluate(editionId) != DomainJoinEditionSupport.Unsupported;
    }

    private static string? ResolveDestination(DeployDomainJoinSettings settings, string domain, string? selectedId, string? destinationDn)
    {
        bool hasTypedDestination = !string.IsNullOrWhiteSpace(destinationDn);
        if (!HasCompatibleCatalog(settings, domain))
        {
            if (settings.Mode == DomainJoinMode.Automatic && hasTypedDestination)
                throw new InvalidDataException("An automatic domain destination must come from the catalog.");
            return hasTypedDestination ? destinationDn : null;
        }
        if (hasTypedDestination)
            throw new InvalidDataException("The domain destination must come from the compatible catalog.");
        string? id = settings.AllowOuSelectionDuringDeployment ? selectedId : settings.DefaultOuId;
        if (id is null && !settings.AllowOuSelectionDuringDeployment) return null;
        return settings.OrganizationalUnits.FirstOrDefault(unit =>
            string.Equals(unit.Id, id, StringComparison.OrdinalIgnoreCase))?.DistinguishedName
            ?? throw new InvalidDataException("The selected domain destination is invalid.");
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
/// <summary>Cancellation never authorizes continuing to destructive confirmation.</summary>
public enum DomainJoinPreparationStatus { Ready, Canceled, Invalid }

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
    internal static DomainJoinPreparationResult Canceled() => new(DomainJoinPreparationStatus.Canceled, null, null);
    internal static DomainJoinPreparationResult Invalid(DomainJoinPreparationFailure code) => new(DomainJoinPreparationStatus.Invalid, null, code);
    public void Dispose() => TakeInput()?.Dispose();
}
