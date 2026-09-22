// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.IO;
using Foundry.Deploy.Models;
using Foundry.Deploy.Services.DriverPacks;
using Foundry.Deploy.Services.Logging;

namespace Foundry.Deploy.Services.Deployment.Steps;

public sealed class DownloadFirmwareUpdateStep : DeploymentStepBase
{
    private readonly IMicrosoftUpdateCatalogFirmwareService _firmwareService;

    public DownloadFirmwareUpdateStep(IMicrosoftUpdateCatalogFirmwareService firmwareService)
    {
        _firmwareService = firmwareService;
    }

    public override string Name => DeploymentStepNames.DownloadFirmwareUpdate;

    protected override async Task<DeploymentStepResult> ExecuteLiveAsync(DeploymentStepExecutionContext context, CancellationToken cancellationToken)
    {
        ResetFirmwareRuntimeState(context.RuntimeState);

        if (!context.Request.ApplyFirmwareUpdates)
        {
            return DeploymentStepResult.Skipped("Firmware updates are disabled.");
        }

        HardwareProfile hardwareProfile = context.RuntimeState.HardwareProfile
            ?? throw new InvalidOperationException("Hardware profile is unavailable for firmware lookup.");

        if (hardwareProfile.IsVirtualMachine)
        {
            return DeploymentStepResult.Skipped("Firmware updates are disabled for virtual machines.");
        }

        if (hardwareProfile.IsOnBattery)
        {
            return DeploymentStepResult.Skipped("Firmware updates are skipped while the device is running on battery power.");
        }

        if (string.IsNullOrWhiteSpace(hardwareProfile.SystemFirmwareHardwareId))
        {
            return DeploymentStepResult.Skipped("System firmware hardware identifier is unavailable.");
        }

        string targetFoundryRoot = context.EnsureTargetFoundryRoot();
        string rawDirectory = Path.Combine(targetFoundryRoot, "Temp", "FirmwareUpdate", "Raw");
        string cacheDirectory = context.ResolveMicrosoftUpdateCatalogFirmwareCacheRoot();

        context.EmitCurrentStepIndeterminate("Downloading firmware update...", "Preparing Microsoft Update Catalog lookup...", DeploymentOperationNames.ResolveFirmware);
        IProgress<double> progress = context.CreateStepPercentProgressReporter("Downloading firmware update...", "Downloading");

        MicrosoftUpdateCatalogFirmwareResult result = await _firmwareService
            .DownloadAsync(hardwareProfile, context.Request.OperatingSystem.Architecture, rawDirectory, cacheDirectory, cancellationToken, progress)
            .ConfigureAwait(false);

        await context.AppendLogAsync(DeploymentLogLevel.Info, result.Message, cancellationToken).ConfigureAwait(false);

        if (!result.IsUpdateAvailable)
        {
            return DeploymentStepResult.Skipped(result.Message);
        }

        if (string.IsNullOrWhiteSpace(result.DownloadedDirectory) || !Directory.Exists(result.DownloadedDirectory) ||
            !Directory.EnumerateFiles(result.DownloadedDirectory, "*.cab", SearchOption.AllDirectories).Any())
        {
            return DeploymentStepResult.Failed("The selected firmware payload is unavailable.",
                DeploymentFailure.Guard(DeploymentOperationNames.DownloadFirmware, DeploymentFailureReasons.MissingResource, "missing_firmware_payload"));
        }

        context.RuntimeState.DownloadedFirmwarePath = result.DownloadedDirectory;
        context.RuntimeState.FirmwareUpdateId = result.UpdateId;
        context.RuntimeState.FirmwareUpdateTitle = result.Title;

        return result.DownloadedCount == 0 && result.ReusedCount > 0
            ? DeploymentStepResult.Skipped("Firmware update resolved from cache.")
            : DeploymentStepResult.Succeeded("Firmware update downloaded.");
    }

    protected override async Task<DeploymentStepResult> ExecuteDryRunAsync(DeploymentStepExecutionContext context, CancellationToken cancellationToken)
    {
        ResetFirmwareRuntimeState(context.RuntimeState);

        if (!context.Request.ApplyFirmwareUpdates)
        {
            await Task.Delay(120, cancellationToken).ConfigureAwait(false);
            return DeploymentStepResult.Skipped("Firmware updates are disabled.");
        }

        HardwareProfile? hardwareProfile = context.RuntimeState.HardwareProfile;
        if (hardwareProfile?.IsVirtualMachine == true)
        {
            await Task.Delay(120, cancellationToken).ConfigureAwait(false);
            return DeploymentStepResult.Skipped("Firmware updates are disabled for virtual machines.");
        }

        if (hardwareProfile?.IsOnBattery == true)
        {
            await Task.Delay(120, cancellationToken).ConfigureAwait(false);
            return DeploymentStepResult.Skipped("Firmware updates are skipped while the device is running on battery power.");
        }

        if (hardwareProfile is null || string.IsNullOrWhiteSpace(hardwareProfile.SystemFirmwareHardwareId))
        {
            await Task.Delay(120, cancellationToken).ConfigureAwait(false);
            return DeploymentStepResult.Skipped("System firmware hardware identifier is unavailable.");
        }

        string targetFoundryRoot = context.EnsureTargetFoundryRoot();
        string rawDirectory = Path.Combine(targetFoundryRoot, "Temp", "FirmwareUpdate", "Raw");
        Directory.CreateDirectory(rawDirectory);

        string cabPath = Path.Combine(rawDirectory, "firmware.cab");
        await File.WriteAllTextAsync(cabPath, "dry-run", cancellationToken).ConfigureAwait(false);

        context.RuntimeState.DownloadedFirmwarePath = rawDirectory;
        context.RuntimeState.FirmwareUpdateId = "dry-run-firmware";
        context.RuntimeState.FirmwareUpdateTitle = "Dry-run firmware update";

        await context.AppendLogAsync(
            DeploymentLogLevel.Info,
            $"[DRY-RUN] Simulated firmware update download: {rawDirectory}",
            cancellationToken).ConfigureAwait(false);
        await Task.Delay(150, cancellationToken).ConfigureAwait(false);

        return DeploymentStepResult.Succeeded("Firmware update downloaded (simulation).");
    }

    private static void ResetFirmwareRuntimeState(DeploymentRuntimeState runtimeState)
    {
        runtimeState.DownloadedFirmwarePath = null;
        runtimeState.ExtractedFirmwarePath = null;
        runtimeState.FirmwareUpdateId = null;
        runtimeState.FirmwareUpdateTitle = null;
    }
}
