// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.IO;
using Foundry.Core.Models.Configuration;
using Foundry.Deploy.Services.Logging;
using Foundry.Deploy.Services.Cache;
using Foundry.Deploy.Services.Download;
using Foundry.Utilities.Processes;

namespace Foundry.Deploy.Services.Deployment.Steps;

public sealed class ApplyOperatingSystemImageStep : DeploymentStepBase
{
    private readonly IWindowsDeploymentService _windowsDeploymentService;
    private readonly IWindowsImageInspectionService _inspection;
    private readonly IArtifactDownloadService _artifacts;
    private readonly IVolumeStorageProbe _storage;

    public ApplyOperatingSystemImageStep(IWindowsDeploymentService windowsDeploymentService,
        IWindowsImageInspectionService inspection, IArtifactDownloadService artifacts, IVolumeStorageProbe storage)
    {
        _windowsDeploymentService = windowsDeploymentService;
        _inspection = inspection;
        _artifacts = artifacts;
        _storage = storage;
    }

    public override string Name => DeploymentStepNames.ApplyOperatingSystemImage;

    protected override async Task<DeploymentStepResult> ExecuteLiveAsync(DeploymentStepExecutionContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        DeploymentPreflightResult? preflight = context.RuntimeState.ImagePreflight;
        if (preflight is null || preflight.Selection != context.Request.OperatingSystem)
            return GuardFailure("The image preflight does not match the selected operating system.", "image_preflight_changed");
        if (context.Request.ConfirmedTargetDisk is null)
            return GuardFailure("The confirmed target disk is unavailable.", "missing_confirmed_target");
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

        using NativeFileLease? lease = TryOpenImageLease(imagePath);
        if (lease is null)
            return GuardFailure("The operating system image cannot be protected for verification.", "image_unavailable");
        ArtifactIdentity artifact = ArtifactIntegrityPolicy.FromOperatingSystem(context.Request.OperatingSystem);
        ArtifactDownloadResult? verified = await _artifacts.TryUseCachedAsync(artifact, imagePath, cancellationToken).ConfigureAwait(false);
        if (verified is null)
            return GuardFailure("The operating system image failed integrity verification.", "image_integrity_failed");

        string workingDirectory = Path.Combine(targetFoundryRoot, "Temp", "Deployment");
        Directory.CreateDirectory(workingDirectory);
        const string applyStepMessage = "Applying OS image...";

        context.EmitCurrentStepIndeterminate(
            applyStepMessage,
            "Inspecting image...",
            DeploymentOperationNames.InspectOperatingSystemImage);
        WindowsImageInfo image = await lease.RunAsync(
            () => _inspection.InspectImageAsync(imagePath, context.Request.OperatingSystem, workingDirectory, cancellationToken),
            cancellationToken).ConfigureAwait(false);
        long additionalBytes = context.Request.DriverPackSelectionKind == Models.DriverPackSelectionKind.OemCatalog
            ? context.Request.DriverPack?.SizeBytes ?? 0 : 0;
        long requiredTargetBytes = DeploymentPreflightCapacityPolicy.CalculateRequiredTargetBytes(
            context.Request.OperatingSystem, image, preflight.Level == ImagePreflightLevel.TargetBackedMetadataOnly, additionalBytes);
        if ((ulong)requiredTargetBytes > context.Request.ConfirmedTargetDisk.SizeBytes)
            return GuardFailure("The confirmed target is too small for the inspected image and selected payloads.", "insufficient_target_capacity");
        long requiredFreeBytes = checked(Math.Max(DeploymentPreflightCapacityPolicy.WindowsMinimumCapacityBytes,
            checked(image.ExpandedSizeBytes + DeploymentPreflightCapacityPolicy.ScratchAndSourceReserveBytes)) + additionalBytes);
        VolumeStorageStatus storage = _storage.Inspect(context.RuntimeState.TargetWindowsPartitionRoot);
        if (!storage.IsPresent || !storage.IsWritable || storage.FreeBytes is not long freeBytes || freeBytes < requiredFreeBytes)
            return GuardFailure("The Windows partition does not have sufficient verified writable space.", "insufficient_windows_space");
        int imageIndex = image.Index;

        context.RuntimeState.AppliedImageIndex = imageIndex;

        string scratchDirectory = Path.Combine(targetFoundryRoot, "Temp", "Dism");
        context.EmitCurrentStepIndeterminate(
            applyStepMessage,
            "Applying image...",
            DeploymentOperationNames.ApplyOperatingSystemImage);
        IProgress<double> applyImageProgress = context.CreateStepPercentProgressReporter(applyStepMessage, "Applying image");

        await lease.RunAsync(async () =>
        {
            await _windowsDeploymentService.ApplyImageAsync(
                imagePath,
                imageIndex,
                context.RuntimeState.TargetWindowsPartitionRoot,
                scratchDirectory,
                workingDirectory,
                cancellationToken,
                applyImageProgress)
                .ConfigureAwait(false);
            return true;
        }, cancellationToken).ConfigureAwait(false);

        context.EmitCurrentStepIndeterminate(
            applyStepMessage,
            "Configuring boot...",
            DeploymentOperationNames.ConfigureBoot);
        await _windowsDeploymentService
            .ConfigureBootAsync(
                context.RuntimeState.TargetWindowsPartitionRoot,
                context.RuntimeState.TargetSystemPartitionRoot,
                context.Request.OperatingSystem.BuildMajor,
                workingDirectory,
                cancellationToken)
            .ConfigureAwait(false);

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
                WindowsEditionDefinition? requestedEdition = WindowsEditionCatalog.Find(context.Request.OperatingSystem.Edition);
                DeploymentLogLevel editionLogLevel = requestedEdition is not null &&
                    requestedEdition.EditionId.Equals(appliedEdition, StringComparison.OrdinalIgnoreCase)
                    ? DeploymentLogLevel.Info
                    : DeploymentLogLevel.Warning;

                string message = editionLogLevel == DeploymentLogLevel.Info
                    ? $"Applied Windows edition verified: {appliedEdition}."
                    : $"Applied Windows edition ID '{appliedEdition}' does not match requested edition '{context.Request.OperatingSystem.Edition}'.";

                await context.AppendLogAsync(editionLogLevel, message, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
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
            $"OS image applied to {context.RuntimeState.TargetWindowsPartitionRoot} (index {imageIndex}); boot configured on {context.RuntimeState.TargetSystemPartitionRoot}.",
            cancellationToken).ConfigureAwait(false);

        return DeploymentStepResult.Succeeded("Operating system image applied.");
    }

    private static NativeFileLease? TryOpenImageLease(string imagePath)
    {
        try { return NativeFileLease.OpenRead(imagePath); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { return null; }
    }

    private static DeploymentStepResult GuardFailure(string message, string errorCode) => DeploymentStepResult.Failed(message,
        DeploymentFailure.Guard(DeploymentOperationNames.ApplyOperatingSystemImage, DeploymentFailureReasons.MissingResource, errorCode));

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
        context.RuntimeState.AppliedImageIndex = 1;

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
