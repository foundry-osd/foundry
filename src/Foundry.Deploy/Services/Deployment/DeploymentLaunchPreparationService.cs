using Foundry.Deploy.Models;
using Foundry.Deploy.Services.ApplicationShell;
using Foundry.Deploy.Validation;

namespace Foundry.Deploy.Services.Deployment;

public sealed class DeploymentLaunchPreparationService : IDeploymentLaunchPreparationService
{
    private readonly IApplicationShellService _applicationShellService;

    public DeploymentLaunchPreparationService(IApplicationShellService applicationShellService)
    {
        _applicationShellService = applicationShellService;
    }

    public DeploymentLaunchPreparationResult Prepare(DeploymentLaunchRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.SelectedOperatingSystem is null)
        {
            return DeploymentLaunchPreparationResult.Failure(
                "Select an operating system.",
                ComputerNameRules.Normalize(request.TargetComputerName));
        }

        string normalizedComputerName = ComputerNameRules.Normalize(request.TargetComputerName);
        if (!ComputerNameRules.IsValid(normalizedComputerName))
        {
            return DeploymentLaunchPreparationResult.Failure("Enter a valid computer name.", normalizedComputerName);
        }

        TargetDiskInfo? effectiveTargetDisk = request.SelectedTargetDisk;
        if (effectiveTargetDisk is null && request.IsDryRun)
        {
            effectiveTargetDisk = TargetDiskInfoFactory.CreateDebugVirtualDisk();
        }

        if (effectiveTargetDisk is null)
        {
            return DeploymentLaunchPreparationResult.Failure("Select a target disk.", normalizedComputerName);
        }

        if (!request.IsDryRun && !effectiveTargetDisk.IsSelectable)
        {
            return DeploymentLaunchPreparationResult.Failure(
                $"Selected disk is blocked: {effectiveTargetDisk.SelectionWarning}",
                normalizedComputerName);
        }

        if (request.DriverPackSelectionKind == DriverPackSelectionKind.OemCatalog &&
            request.SelectedDriverPack is null)
        {
            return DeploymentLaunchPreparationResult.Failure(
                "Select a valid OEM model/version before starting deployment.",
                normalizedComputerName);
        }

        if (request.IsAutopilotEnabled && request.SelectedAutopilotProfile is null)
        {
            return DeploymentLaunchPreparationResult.Failure(
                "Select an Autopilot profile or disable Autopilot before starting deployment.",
                normalizedComputerName);
        }

        if (!request.IsDryRun && !ConfirmDestructiveDeployment(effectiveTargetDisk, request.SelectedOperatingSystem))
        {
            return DeploymentLaunchPreparationResult.Failure("Deployment cancelled by user.", normalizedComputerName);
        }

        DeploymentContext context = new()
        {
            Mode = request.Mode,
            CacheRootPath = request.CacheRootPath,
            TargetDiskNumber = effectiveTargetDisk.DiskNumber,
            TargetComputerName = normalizedComputerName,
            OperatingSystem = request.SelectedOperatingSystem,
            DriverPackSelectionKind = request.DriverPackSelectionKind,
            DriverPack = request.SelectedDriverPack,
            ApplyFirmwareUpdates = request.ApplyFirmwareUpdates,
            IsAutopilotEnabled = request.IsAutopilotEnabled,
            SelectedAutopilotProfile = request.SelectedAutopilotProfile,
            IsDryRun = request.IsDryRun
        };

        return DeploymentLaunchPreparationResult.Success(
            "Deployment preparation completed.",
            normalizedComputerName,
            effectiveTargetDisk,
            context);
    }

    private bool ConfirmDestructiveDeployment(TargetDiskInfo targetDisk, OperatingSystemCatalogItem operatingSystem)
    {
        string sizeGiB = targetDisk.SizeBytes > 0
            ? $"{(targetDisk.SizeBytes / 1024d / 1024d / 1024d):0.0} GiB"
            : "Unknown size";

        string message =
            "This operation will ERASE the selected disk and apply a new operating system." + Environment.NewLine +
            Environment.NewLine +
            $"Disk: {targetDisk.DiskNumber}" + Environment.NewLine +
            $"Model: {targetDisk.FriendlyName}" + Environment.NewLine +
            $"Bus: {targetDisk.BusType}" + Environment.NewLine +
            $"Size: {sizeGiB}" + Environment.NewLine +
            Environment.NewLine +
            $"OS: {operatingSystem.DisplayLabel}" + Environment.NewLine +
            "Continue?";

        return _applicationShellService.ConfirmWarning("Confirm Disk Erase", message);
    }
}
