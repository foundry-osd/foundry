// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Deploy.Models;
using Foundry.Deploy.Models.Configuration;
using Foundry.Deploy.Services.ApplicationShell;
using Foundry.Deploy.Services.Deployment.Preflight;
using Foundry.Deploy.Services.Localization;
using Foundry.Deploy.Validation;

namespace Foundry.Deploy.Services.Deployment;

/// <summary>
/// Validates launch selections and asks the shell for destructive deployment confirmation before creating a deployment context.
/// </summary>
public sealed class DeploymentLaunchPreparationService : IDeploymentLaunchPreparationService
{
    private readonly IApplicationShellService _applicationShellService;
    private readonly IDeploymentPreflightService _deploymentPreflightService;

    public DeploymentLaunchPreparationService(
        IApplicationShellService applicationShellService,
        IDeploymentPreflightService deploymentPreflightService)
    {
        _applicationShellService = applicationShellService;
        _deploymentPreflightService = deploymentPreflightService;
    }

    /// <summary>
    /// Builds a deployment context when the request is valid and the user confirms disk erasure.
    /// </summary>
    /// <param name="request">The wizard selections and launch options to validate.</param>
    /// <returns>The normalized launch result, including a deployment context when startup can continue.</returns>
    public DeploymentLaunchPreparationResult Prepare(DeploymentLaunchRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.SelectedOperatingSystem is null)
        {
            return DeploymentLaunchPreparationResult.Failure(ComputerNameRules.Normalize(request.TargetComputerName));
        }

        string normalizedComputerName = ComputerNameRules.Normalize(request.TargetComputerName);
        if (!ComputerNameRules.IsValid(normalizedComputerName))
        {
            return DeploymentLaunchPreparationResult.Failure(normalizedComputerName);
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

        DeploymentPreflightResult preflight = request.IsDryRun
            ? new DeploymentPreflightResult { Findings = [] }
            : _deploymentPreflightService.Evaluate(request.DetectedHardware, effectiveTargetDisk, request.SelectedOperatingSystem);
        if (preflight.HasBlockingFindings)
        {
            return DeploymentLaunchPreparationResult.Failure(normalizedComputerName);
        }

        if (!request.IsDryRun && !ConfirmDestructiveDeployment(effectiveTargetDisk, request.SelectedOperatingSystem, preflight))
        {
            return DeploymentLaunchPreparationResult.Failure(normalizedComputerName);
        }

        DeploymentContext context = new()
        {
            Mode = request.Mode,
            CacheRootPath = request.CacheRootPath,
            TargetDiskNumber = effectiveTargetDisk.DiskNumber,
            TargetComputerName = normalizedComputerName,
            DefaultTimeZoneId = string.IsNullOrWhiteSpace(request.DefaultTimeZoneId) ? null : request.DefaultTimeZoneId.Trim(),
            OperatingSystem = request.SelectedOperatingSystem,
            AcknowledgedPreflightWarnings = preflight.Findings
                .Where(finding => finding.Severity == DeploymentPreflightSeverity.Warning)
                .Select(finding => finding.AcknowledgementKey)
                .ToArray(),
            DriverPackSelectionKind = request.DriverPackSelectionKind,
            DriverPack = request.SelectedDriverPack,
            ApplyFirmwareUpdates = request.ApplyFirmwareUpdates,
            IsAutopilotEnabled = request.IsAutopilotEnabled,
            AutopilotProvisioningMode = request.AutopilotProvisioningMode,
            SelectedAutopilotProfile = request.SelectedAutopilotProfile,
            AutopilotHardwareHashUpload = request.AutopilotHardwareHashUpload,
            Network = request.Network,
            Oobe = request.Oobe,
            AppxRemoval = request.AppxRemoval,
            AiComponentRemoval = request.AiComponentRemoval,
            IsDryRun = request.IsDryRun
        };

        return DeploymentLaunchPreparationResult.Success(
            normalizedComputerName,
            effectiveTargetDisk,
            context);
    }

    /// <summary>
    /// Shows the final warning that live deployments erase the selected target disk.
    /// </summary>
    /// <param name="targetDisk">The disk that will be repartitioned.</param>
    /// <param name="operatingSystem">The operating system image that will be applied.</param>
    /// <param name="preflight">The readiness result whose warnings require acknowledgement.</param>
    /// <returns><see langword="true"/> when the user confirms the destructive operation.</returns>
    private bool ConfirmDestructiveDeployment(
        TargetDiskInfo targetDisk,
        OperatingSystemCatalogItem operatingSystem,
        DeploymentPreflightResult preflight)
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

        if (preflight.HasWarnings)
        {
            string warnings = string.Join(
                Environment.NewLine,
                preflight.Findings
                    .Where(finding => finding.Severity == DeploymentPreflightSeverity.Warning)
                    .Select(finding => $"• {DeploymentPreflightLocalization.FormatFinding(finding)}"));
            message = $"{message}{Environment.NewLine}{Environment.NewLine}{LocalizationText.GetString("Preflight.WarningConfirmationIntro")}{Environment.NewLine}{warnings}";
        }

        return _applicationShellService.ConfirmWarning(LocalizationText.GetString("Launch.ConfirmDiskEraseTitle"), message);
    }
}
