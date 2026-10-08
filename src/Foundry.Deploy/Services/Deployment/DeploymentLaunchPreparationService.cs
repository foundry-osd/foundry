// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Deploy.Models;
using Foundry.Deploy.Models.Configuration;
using Foundry.Deploy.Services.ApplicationShell;
using Foundry.Deploy.Services.Localization;
using Foundry.Utilities.Storage;
using ComputerNameRules = Foundry.Core.Services.Configuration.ComputerNameRules;
using Foundry.Deploy.Services.DomainJoin;
using Foundry.Core.Services.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using DeployDomainJoinSettings = Foundry.Core.Models.Configuration.Deploy.DeployDomainJoinSettings;

namespace Foundry.Deploy.Services.Deployment;

/// <summary>
/// Validates launch selections and asks the shell for destructive deployment confirmation before creating a deployment context.
/// </summary>
public sealed class DeploymentLaunchPreparationService : IDeploymentLaunchPreparationService
{
    private readonly IApplicationShellService _applicationShellService;
    private readonly Unattend.UnattendContentService? _unattendContentService;
    private readonly IDomainJoinPreparationService? _domainJoinPreparationService;
    private readonly ILogger<DeploymentLaunchPreparationService> _logger;

    public DeploymentLaunchPreparationService(IApplicationShellService applicationShellService, Unattend.UnattendContentService? unattendContentService = null,
        IDomainJoinPreparationService? domainJoinPreparationService = null, ILogger<DeploymentLaunchPreparationService>? logger = null)
    {
        _applicationShellService = applicationShellService;
        _unattendContentService = unattendContentService;
        _domainJoinPreparationService = domainJoinPreparationService;
        _logger = logger ?? NullLogger<DeploymentLaunchPreparationService>.Instance;
    }

    /// <summary>
    /// Builds a deployment context when the request is valid and the user confirms disk erasure.
    /// </summary>
    /// <param name="request">The wizard selections and launch options to validate.</param>
    /// <returns>The normalized launch result, including a deployment context when startup can continue.</returns>
    public DeploymentLaunchPreparationResult Prepare(DeploymentLaunchRequest request) => Prepare(request, null);

    /// <summary>Accepts credential-bearing runtime settings and wizard input only as preparation-local input.</summary>
    public DeploymentLaunchPreparationResult Prepare(DeploymentLaunchRequest request, DeployDomainJoinSettings? domainJoin,
        DomainJoinSubmission? domainJoinSubmission = null)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.SelectedOperatingSystem is null)
        {
            return DeploymentLaunchPreparationResult.Failure(ComputerNameRules.Normalize(request.TargetComputerName));
        }

        bool requestedDomainJoin = domainJoin?.IsEnabled == true;
        if (requestedDomainJoin && (request.IsAutopilotEnabled ||
            !DomainJoinConfigurationValidator.ValidateMetadata(DomainJoinPreparationService.ToAuthored(domainJoin!)).IsValid))
            return DeploymentLaunchPreparationResult.Failure(request.TargetComputerName, LocalizationText.GetString("DomainJoin.Invalid"));
        bool requiresDomainJoin = DomainJoinPreparationService.IsRequiredFor(domainJoin, request.SelectedOperatingSystem);
        bool unsupportedDomainJoin = requestedDomainJoin && !requiresDomainJoin;

        string normalizedComputerName = ComputerNameRules.Normalize(request.TargetComputerName);
        if (!request.UsesCustomUnattend && !ComputerNameRules.IsValid(normalizedComputerName))
        {
            return DeploymentLaunchPreparationResult.Failure(normalizedComputerName);
        }

        bool hasCustomCommands = false;
        if (request.Unattend is not null)
        {
            try
            {
                if (_unattendContentService is null) return DeploymentLaunchPreparationResult.Failure(normalizedComputerName);
                using Unattend.UnattendSnapshot snapshot = _unattendContentService.Read(request.Unattend,
                    request.SelectedOperatingSystem.Architecture, request.IsAutopilotEnabled, request.AutopilotProvisioningMode, requiresDomainJoin);
                hasCustomCommands = snapshot.Inspection.HasCommands;
                if (requiresDomainJoin) normalizedComputerName = snapshot.Inspection.ConcreteComputerName!;
            }
            catch (Exception ex) when (ex is global::System.IO.InvalidDataException or global::System.IO.IOException or InvalidOperationException)
            {
                return DeploymentLaunchPreparationResult.UnattendFailure(normalizedComputerName, LocalizationText.GetString(
                    ex is InvalidOperationException ? "Unattend.AutopilotConflict" : "Unattend.Invalid"));
            }
        }

        TargetDiskInfo? effectiveTargetDisk = request.SelectedTargetDisk;
        if (effectiveTargetDisk is null && request.IsDryRun)
        {
            effectiveTargetDisk = TargetDiskInfoFactory.CreateDebugVirtualDisk();
        }

        if (effectiveTargetDisk is null)
        {
            return DeploymentLaunchPreparationResult.Failure(normalizedComputerName);
        }

        if (!request.IsDryRun && !effectiveTargetDisk.IsSelectable)
        {
            return DeploymentLaunchPreparationResult.Failure(normalizedComputerName);
        }

        DiskIdentity? confirmedIdentity = effectiveTargetDisk.Identity;
        if (!request.IsDryRun && (confirmedIdentity is not { IsUsable: true } ||
                                 confirmedIdentity.Number != effectiveTargetDisk.DiskNumber))
        {
            return DeploymentLaunchPreparationResult.Failure(
                normalizedComputerName, LocalizationText.GetString("Disk.IdentityCannotBeConfirmed"));
        }

        if (request.DriverPackSelectionKind == DriverPackSelectionKind.OemCatalog &&
            request.SelectedDriverPack is null)
        {
            return DeploymentLaunchPreparationResult.Failure(normalizedComputerName);
        }

        if (request.IsAutopilotEnabled &&
            request.AutopilotProvisioningMode == AutopilotProvisioningMode.JsonProfile &&
            request.SelectedAutopilotProfile is null)
        {
            return DeploymentLaunchPreparationResult.Failure(normalizedComputerName);
        }

        DomainJoinDeploymentRequest? domainRequest = null;
        DomainJoinDeploymentIntent? domainIntent = null;
        using DomainJoinPreparationResult? prepared = requiresDomainJoin && !request.IsDryRun
            ? _domainJoinPreparationService?.Prepare(domainJoin!, normalizedComputerName, domainJoinSubmission) : null;
        if (requestedDomainJoin)
        {
            DomainJoinDeploymentDisposition disposition = unsupportedDomainJoin ? DomainJoinDeploymentDisposition.UnsupportedEdition :
                request.IsDryRun ? DomainJoinDeploymentDisposition.DryRun : DomainJoinDeploymentDisposition.Ready;
            domainRequest = new(disposition, domainJoin!.Mode);
            if (disposition == DomainJoinDeploymentDisposition.Ready)
            {
                if (prepared?.Status != DomainJoinPreparationStatus.Ready || prepared.Input is null)
                {
                    _logger.LogWarning("Domain join preparation failed before deployment start. FailureCode={FailureCode}", prepared?.FailureCode);
                    return DeploymentLaunchPreparationResult.Failure(normalizedComputerName, LocalizationText.GetString("DomainJoin.Invalid"));
                }
                domainIntent = new(prepared.Input.CredentialContext.DomainName, prepared.Input.ComputerName, prepared.Input.TargetOuDn);
            }
            else if (disposition == DomainJoinDeploymentDisposition.DryRun)
            {
                domainIntent = CreateDryRunIntent(domainJoin!, domainJoinSubmission, normalizedComputerName);
            }

            domainRequest = domainRequest with
            {
                OuSource = DomainJoinPreparationService.ResolveOuSource(
                    DomainJoinPreparationService.ResolveDomain(domainJoin!, domainJoinSubmission?.SelectedDomainId), domainIntent),
                DomainSource = DomainJoinPreparationService.ResolveDomainSource(domainJoin!, domainIntent)
            };
        }

        if (!request.IsDryRun && !ConfirmDestructiveDeployment(effectiveTargetDisk, request.SelectedOperatingSystem, request, hasCustomCommands, domainRequest))
        {
            return DeploymentLaunchPreparationResult.Failure(normalizedComputerName);
        }

        DeploymentContext context = new()
        {
            DomainJoinRequest = domainRequest,
            DomainJoinIntent = domainIntent,
            Mode = request.Mode,
            CacheRootPath = request.CacheRootPath,
            TargetDiskNumber = effectiveTargetDisk.DiskNumber,
            TargetDiskIdentity = confirmedIdentity,
            Unattend = request.Unattend,
            TargetComputerName = request.UsesCustomUnattend ? string.Empty : normalizedComputerName,
            UploadComputerNameToAutopilot = request.UploadComputerNameToAutopilot,
            OperatingSystem = request.SelectedOperatingSystem,
            DriverPackSelectionKind = request.DriverPackSelectionKind,
            DriverPack = request.SelectedDriverPack,
            ApplyFirmwareUpdates = request.ApplyFirmwareUpdates,
            IsAutopilotEnabled = request.IsAutopilotEnabled,
            AutopilotProvisioningMode = request.AutopilotProvisioningMode,
            SelectedAutopilotProfile = request.SelectedAutopilotProfile,
            AutopilotHardwareHashUpload = request.AutopilotHardwareHashUpload,
            Network = request.Network,
            Oobe = request.UsesCustomUnattend ? new DeployOobeSettings() : request.Oobe,
            PreOobe = request.PreOobe,
            AppxRemoval = request.AppxRemoval,
            AiComponentRemoval = request.AiComponentRemoval,
            WindowsOptionalFeatures = request.WindowsOptionalFeatures,
            Completion = request.Completion,
            IsDryRun = request.IsDryRun
        };

        return DeploymentLaunchPreparationResult.Success(
            normalizedComputerName,
            effectiveTargetDisk,
            context,
            prepared?.TakeInput());
    }

    /// <summary>Simulates the join target from the wizard input without touching credentials; an unusable input yields no intent.</summary>
    private static DomainJoinDeploymentIntent? CreateDryRunIntent(DeployDomainJoinSettings settings, DomainJoinSubmission? submission, string computerName)
    {
        var retained = DomainJoinPreparationService.ResolveDomain(settings, submission?.SelectedDomainId);
        string? domain = settings.Domains.Count > 0 ? retained?.DomainName : submission?.DomainName;
        if (!Foundry.Core.Models.Configuration.DomainJoinCredentialContext.IsValidDomainName(domain)) return null;
        try
        {
            return new(domain!, computerName, DomainJoinPreparationService.ResolveOrganizationalUnit(
                settings, retained, submission?.SelectedOuId, submission?.TypedOuDistinguishedName));
        }
        catch (global::System.IO.InvalidDataException)
        {
            return null;
        }
    }

    /// <summary>
    /// Shows the final warning that live deployments erase the selected target disk.
    /// </summary>
    /// <param name="targetDisk">The disk that will be repartitioned.</param>
    /// <param name="operatingSystem">The operating system image that will be applied.</param>
    /// <param name="request">Effective customization and answer-file ownership shown in the confirmation.</param>
    /// <param name="hasCustomCommands">Whether preserved commands require an overlap warning.</param>
    /// <param name="domainRequest">Secret-free disposition, including any intentional edition skip.</param>
    /// <returns><see langword="true"/> when the user confirms the destructive operation.</returns>
    private bool ConfirmDestructiveDeployment(TargetDiskInfo targetDisk, OperatingSystemMetadata operatingSystem, DeploymentLaunchRequest request, bool hasCustomCommands,
        DomainJoinDeploymentRequest? domainRequest)
    {
        string sizeGiB = targetDisk.SizeBytes > 0
            ? $"{(targetDisk.SizeBytes / 1024d / 1024d / 1024d):0.0} GiB"
            : LocalizationText.GetString("Disk.UnknownSize");

        string message = LocalizationText.Format(
            "Launch.ConfirmDiskEraseMessageFormat",
            targetDisk.DiskNumber,
            targetDisk.FriendlyName,
            targetDisk.BusType,
            sizeGiB,
            operatingSystem.DisplayLabel);

        if (request.UsesCustomUnattend)
        {
            message += Environment.NewLine + Environment.NewLine + request.Unattend!.File.DisplayName + Environment.NewLine +
                LocalizationText.GetString("Unattend.Ownership") + Environment.NewLine +
                LocalizationText.GetString("Unattend.HookCompatibility");
            if (hasCustomCommands) message += Environment.NewLine + LocalizationText.GetString("Unattend.CommandsWarning");
            if (request.IsAutopilotEnabled && request.AutopilotProvisioningMode == AutopilotProvisioningMode.HardwareHashUpload)
                message += Environment.NewLine + LocalizationText.GetString("Unattend.HashWarning");
        }

        // The domain and OU are reviewed on the summary page; only the edition warning belongs to this confirmation.
        if (domainRequest?.Disposition == DomainJoinDeploymentDisposition.UnsupportedEdition)
            message += Environment.NewLine + LocalizationText.GetString("DomainJoin.UnsupportedEdition");
        return _applicationShellService.ConfirmWarning(LocalizationText.GetString("Launch.ConfirmDiskEraseTitle"), message);
    }
}
