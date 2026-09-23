// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.IO;
using Foundry.Deploy.Services.DriverPacks;
using Foundry.Deploy.Services.Logging;

namespace Foundry.Deploy.Services.Deployment.Steps;

/// <summary>Extracts the selected firmware payload before it is staged into offline Windows.</summary>
public sealed class ExtractFirmwareUpdateStep(IMicrosoftUpdateCatalogFirmwareService firmwareService) : DeploymentStepBase
{
    public override string Name => DeploymentStepNames.ExtractFirmwareUpdate;

    protected override async Task<DeploymentStepResult> ExecuteLiveAsync(DeploymentStepExecutionContext context, CancellationToken cancellationToken)
    {
        DeploymentStepResult? unavailable = ValidateSource(context);
        if (unavailable is not null)
        {
            return unavailable;
        }

        string extractedDirectory = Path.Combine(context.EnsureTargetFoundryRoot(), "Extracted", "Firmware");
        context.EmitCurrentStepIndeterminate("Extracting firmware update...", "Extracting firmware payload...", DeploymentOperationNames.ExtractFirmware);
        int infCount = await firmwareService.ExtractAsync(context.RuntimeState.DownloadedFirmwarePath!, extractedDirectory,
            cancellationToken, context.CreateStepPercentProgressReporter("Extracting firmware update...", "Extracting")).ConfigureAwait(false);
        if (infCount <= 0 || !Directory.Exists(extractedDirectory) ||
            !Directory.EnumerateFiles(extractedDirectory, "*.inf", SearchOption.AllDirectories).Any())
        {
            return MissingPayloadFailure();
        }

        context.RuntimeState.ExtractedFirmwarePath = extractedDirectory;
        await context.AppendLogAsync(DeploymentLogLevel.Info, $"Firmware payload extracted: {infCount} INF files.", cancellationToken).ConfigureAwait(false);
        return DeploymentStepResult.Succeeded("Firmware update extracted.");
    }

    protected override async Task<DeploymentStepResult> ExecuteDryRunAsync(DeploymentStepExecutionContext context, CancellationToken cancellationToken)
    {
        DeploymentStepResult? unavailable = ValidateSource(context);
        if (unavailable is not null)
        {
            return unavailable;
        }

        string extractedDirectory = Path.Combine(context.EnsureTargetFoundryRoot(), "Extracted", "Firmware");
        Directory.CreateDirectory(extractedDirectory);
        await File.WriteAllTextAsync(Path.Combine(extractedDirectory, "firmware.inf"), "; dry-run only", cancellationToken).ConfigureAwait(false);
        context.RuntimeState.ExtractedFirmwarePath = extractedDirectory;
        return DeploymentStepResult.Succeeded("Firmware update extracted (simulation).");
    }

    private static DeploymentStepResult? ValidateSource(DeploymentStepExecutionContext context)
    {
        string? source = context.RuntimeState.DownloadedFirmwarePath;
        if (string.IsNullOrWhiteSpace(source))
        {
            return DeploymentStepResult.Skipped("No firmware update payload is available.");
        }

        return Directory.Exists(source) && Directory.EnumerateFiles(source, "*.cab", SearchOption.AllDirectories).Any()
            ? null
            : MissingPayloadFailure();
    }

    private static DeploymentStepResult MissingPayloadFailure() =>
        DeploymentStepResult.Failed("The selected firmware payload is unavailable.",
            DeploymentFailure.Guard(DeploymentOperationNames.ExtractFirmware, DeploymentFailureReasons.MissingResource, "missing_firmware_payload"));
}
