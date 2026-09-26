// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.IO;
using Foundry.Core.Models.Configuration;
using Foundry.Deploy.Services.Logging;

namespace Foundry.Deploy.Services.Deployment.Steps;

public sealed class ApplyOperatingSystemImageStep : DeploymentStepBase
{
    private readonly IWindowsDeploymentService _windowsDeploymentService;
    private readonly IDeploymentStorageService _storageService;

    public ApplyOperatingSystemImageStep(IWindowsDeploymentService windowsDeploymentService, IDeploymentStorageService? storageService = null)
    {
        _windowsDeploymentService = windowsDeploymentService;
        _storageService = storageService ?? new DeploymentStorageService();
    }

    public override string Name => DeploymentStepNames.ApplyOperatingSystemImage;

    protected override async Task<DeploymentStepResult> ExecuteLiveAsync(DeploymentStepExecutionContext context, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(context.RuntimeState.TargetWindowsPartitionRoot) ||
            string.IsNullOrWhiteSpace(context.RuntimeState.TargetSystemPartitionRoot))
        {
            return DeploymentStepResult.Failed(
                "Target disk layout was not prepared.",
                DeploymentFailure.Guard(
                    DeploymentOperationNames.ApplyOperatingSystemImage,
                    DeploymentFailureReasons.MissingResource,
                    "missing_target_layout"));
        }

        string targetFoundryRoot = context.EnsureTargetFoundryRoot();
        string imagePath = context.RuntimeState.DownloadedOperatingSystemPath ?? string.Empty;
        if (!File.Exists(imagePath))
        {
            return DeploymentStepResult.Failed(
                "Operating system image was not downloaded.",
                DeploymentFailure.Guard(
                    DeploymentOperationNames.ApplyOperatingSystemImage,
                    DeploymentFailureReasons.MissingResource,
                    "missing_os_image"));
        }

        string workingDirectory = Path.Combine(targetFoundryRoot, "Temp", "Deployment");
        Directory.CreateDirectory(workingDirectory);
        const string applyStepMessage = "Applying OS image...";

        context.EmitCurrentStepIndeterminate(
            applyStepMessage,
            "Checking available space...",
            DeploymentOperationNames.ApplyOperatingSystemImage);
        WindowsImageMetadata metadata;
        try
        {
            DeploymentPreflightState? preflight = context.Preflight;
            if (preflight?.Image is null || !preflight.Matches(context) ||
                !string.Equals(preflight.ImagePath, imagePath, StringComparison.OrdinalIgnoreCase))
            {
                throw PreflightDeploymentStep.Guard("Preflight.NotReady", "preflight_not_ready");
            }
            metadata = preflight.Image;
            long actualArchiveBytes = preflight.UsesTargetStorage ? new FileInfo(imagePath).Length : 0;
            long targetDriverBytes = preflight.TargetDriverBytes;
            DeploymentCapacityPolicy.EnsureTargetCapacity(context, metadata, actualArchiveBytes, targetDriverBytes);
            long remainingBytes = DeploymentCapacityPolicy.RequiredWindowsBytes(metadata, 0, targetDriverBytes,
                DeploymentCapacityPolicy.NeedsOptionalFeatureSource(context));
            long? availableBytes = _storageService.GetAvailableBytes(context.RuntimeState.TargetWindowsPartitionRoot);
            if (availableBytes is null)
            {
                throw PreflightDeploymentStep.Guard("Preflight.TargetSpaceUnavailable", "target_space_unavailable");
            }
            if (availableBytes.Value < remainingBytes)
            {
                throw DeploymentCapacityPolicy.Failure();
            }
        }
        catch (DeploymentOperationException exception)
        {
            return DeploymentStepResult.Failed(exception.Message, exception.Failure);
        }
        catch (OverflowException)
        {
            DeploymentOperationException exception = PreflightDeploymentStep.Guard("Preflight.InvalidMetadata", "invalid_capacity_metadata");
            return DeploymentStepResult.Failed(exception.Message, exception.Failure);
        }

        int imageIndex = metadata.Index;

        string scratchDirectory = Path.Combine(targetFoundryRoot, "Temp", "Dism");
        context.EmitCurrentStepIndeterminate(
            applyStepMessage,
            "Applying image...",
            DeploymentOperationNames.ApplyOperatingSystemImage);
        IProgress<double> applyImageProgress = context.CreateStepPercentProgressReporter(applyStepMessage, "Applying image");

        await _windowsDeploymentService
            .ApplyImageAsync(
                imagePath,
                imageIndex,
                context.RuntimeState.TargetWindowsPartitionRoot,
                scratchDirectory,
                workingDirectory,
                cancellationToken,
                applyImageProgress)
            .ConfigureAwait(false);

        context.RuntimeState.AppliedImageIndex = imageIndex;
        context.EmitCurrentStepIndeterminate(
            applyStepMessage,
            "Verifying image...",
            DeploymentOperationNames.VerifyOperatingSystemEdition);
        try
        {
            string? appliedEdition = await _windowsDeploymentService
                .GetAppliedWindowsEditionAsync(context.RuntimeState.TargetWindowsPartitionRoot, workingDirectory, cancellationToken)
                .ConfigureAwait(false);

            if (!string.IsNullOrWhiteSpace(appliedEdition))
            {
                string? requestedEdition = context.Request.OperatingSystem is Models.CustomImageSelection ? metadata.EditionId : WindowsEditionCatalog.Find(context.Request.OperatingSystem.Edition)?.EditionId;
                DeploymentLogLevel editionLogLevel = requestedEdition is not null &&
                    requestedEdition.Equals(appliedEdition, StringComparison.OrdinalIgnoreCase)
                    ? DeploymentLogLevel.Info
                    : DeploymentLogLevel.Warning;

                string message = editionLogLevel == DeploymentLogLevel.Info
                    ? $"Applied Windows edition verified: {appliedEdition}."
                    : $"Applied Windows edition ID '{appliedEdition}' does not match requested edition '{context.Request.OperatingSystem.Edition}'.";

                await context.AppendLogAsync(editionLogLevel, message, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            await context.AppendLogAsync(
                DeploymentLogLevel.Warning,
                $"Unable to verify the applied Windows edition: {ex.Message}",
                cancellationToken).ConfigureAwait(false);
        }

        await context.AppendLogAsync(
            DeploymentLogLevel.Info,
            $"OS image applied to {context.RuntimeState.TargetWindowsPartitionRoot} (index {imageIndex}).",
            cancellationToken).ConfigureAwait(false);

        return DeploymentStepResult.Succeeded("Operating system image applied.");
    }

    protected override async Task<DeploymentStepResult> ExecuteDryRunAsync(DeploymentStepExecutionContext context, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(context.RuntimeState.TargetWindowsPartitionRoot) ||
            string.IsNullOrWhiteSpace(context.RuntimeState.TargetSystemPartitionRoot))
        {
            return DeploymentStepResult.Failed(
                "Target disk layout was not prepared.",
                DeploymentFailure.Guard(
                    DeploymentOperationNames.ApplyOperatingSystemImage,
                    DeploymentFailureReasons.MissingResource,
                    "missing_target_layout"));
        }

        string targetFoundryRoot = context.EnsureTargetFoundryRoot();
        string targetRoot = Path.Combine(targetFoundryRoot, "Temp", "Deployment");
        Directory.CreateDirectory(targetRoot);
        context.RuntimeState.AppliedImageIndex = context.Request.OperatingSystem is Models.CustomImageSelection custom ? custom.Index.Index : 1;

        await File.WriteAllTextAsync(
            Path.Combine(targetRoot, "apply-image.log"),
            $"Dry-run image apply at {DateTimeOffset.UtcNow:O}{Environment.NewLine}OS={context.Request.OperatingSystem.DisplayLabel}",
            cancellationToken).ConfigureAwait(false);

        await context.AppendLogAsync(DeploymentLogLevel.Info, $"[DRY-RUN] Simulated OS apply to {context.RuntimeState.TargetWindowsPartitionRoot}.", cancellationToken).ConfigureAwait(false);
        await context.AppendLogAsync(DeploymentLogLevel.Info, $"[DRY-RUN] Simulated applied Windows edition: {context.Request.OperatingSystem.Edition}.", cancellationToken).ConfigureAwait(false);
        await Task.Delay(180, cancellationToken).ConfigureAwait(false);

        return DeploymentStepResult.Succeeded("Operating system image applied (simulation).");
    }

}
