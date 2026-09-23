// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.IO;
using Foundry.Deploy.Services.Logging;

namespace Foundry.Deploy.Services.Deployment.Steps;

/// <summary>Validates the acquired image and its known capacity before external-source erasure or target-source application.</summary>
public sealed class CheckWindowsImageStep(
    IWindowsDeploymentService windowsDeploymentService,
    IDeploymentStorageService storageService) : DeploymentStepBase
{
    public override string Name => DeploymentStepNames.CheckWindowsImage;

    protected override async Task<DeploymentStepResult> ExecuteLiveAsync(DeploymentStepExecutionContext context, CancellationToken cancellationToken)
    {
        try
        {
            DeploymentPreflightState? prepared = context.Preflight;
            string? imagePath = context.RuntimeState.DownloadedOperatingSystemPath;
            if (prepared is null || !prepared.MatchesStoragePlan(context) ||
                string.IsNullOrWhiteSpace(imagePath) || !File.Exists(imagePath) ||
                !string.Equals(prepared.ImagePath, imagePath, StringComparison.OrdinalIgnoreCase))
            {
                throw PreflightDeploymentStep.Guard("Preflight.NotReady", "preflight_not_ready");
            }

            if (!prepared.UsesTargetStorage &&
                (prepared.SourceLease?.CanRead != true ||
                 !await context.IsExternalStorageAsync(imagePath, cancellationToken).ConfigureAwait(false)))
            {
                throw PreflightDeploymentStep.Guard("Preflight.NotReady", "preflight_not_ready");
            }

            long sourceBytes = prepared.SourceLease?.Length ?? new FileInfo(imagePath).Length;
            if (sourceBytes <= 0)
            {
                throw PreflightDeploymentStep.Guard("Preflight.InvalidImageMetadata", "invalid_image_metadata");
            }

            context.EmitCurrentStepIndeterminate("Checking Windows image...", "Inspecting image...", DeploymentOperationNames.InspectOperatingSystemImage);
            WindowsImageMetadata image = await windowsDeploymentService
                .InspectImageAsync(imagePath, context.Request.OperatingSystem.Edition, cancellationToken).ConfigureAwait(false);
            long driverBytes = PreflightDeploymentStep.ResolveTargetDriverBytes(context,
                prepared.UsesTargetStorage ? null : storageService.GetAvailableBytes(imagePath));
            DeploymentCapacityPolicy.EnsureTargetCapacity(context, image, prepared.UsesTargetStorage ? sourceBytes : 0, driverBytes);
            prepared.SourceSizeBytes = sourceBytes;
            prepared.TargetDriverBytes = driverBytes;
            prepared.Image = image;
            await context.AppendLogAsync(DeploymentLogLevel.Info,
                $"Image checked. ImageIndex={image.Index}; ImageExpandedBytes={image.SizeBytes}; SourceBytes={sourceBytes}; TargetDriverArchiveBytes={driverBytes}; OptionalSetupMediaBytes={image.SetupMediaSizeBytes?.ToString() ?? "unknown"}.",
                cancellationToken).ConfigureAwait(false);
            return DeploymentStepResult.Succeeded("Windows image and known capacity checked.");
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
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return DeploymentStepResult.Failed(
                Foundry.Deploy.Services.Localization.LocalizationText.GetString("Preflight.CacheUnavailable"),
                DeploymentFailureClassifier.Classify(exception, DeploymentOperationNames.InspectOperatingSystemImage));
        }
    }

    protected override Task<DeploymentStepResult> ExecuteDryRunAsync(DeploymentStepExecutionContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(DeploymentStepResult.Succeeded("Windows image checked (simulation)."));
    }
}
