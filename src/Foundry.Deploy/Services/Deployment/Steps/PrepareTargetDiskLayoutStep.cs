// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.IO;
using Foundry.Deploy.Models;
using Foundry.Deploy.Services.Logging;

namespace Foundry.Deploy.Services.Deployment.Steps;

public sealed class PrepareTargetDiskLayoutStep : DeploymentStepBase
{
    private readonly IWindowsDeploymentService _windowsDeploymentService;
    private readonly IImageSourceProbe _sourceProbe;

    public PrepareTargetDiskLayoutStep(IWindowsDeploymentService windowsDeploymentService, IImageSourceProbe? sourceProbe = null)
    {
        _windowsDeploymentService = windowsDeploymentService;
        _sourceProbe = sourceProbe ?? new ImageSourceProbe();
    }

    public override string Name => DeploymentStepNames.PrepareTargetDiskLayout;

    protected override async Task<DeploymentStepResult> ExecuteLiveAsync(DeploymentStepExecutionContext context, CancellationToken cancellationToken)
    {
        DeploymentPreflightState? preflight = context.Preflight;
        if (preflight is null || preflight.ErasureStarted || !preflight.Matches(context))
        {
            return DeploymentStepResult.Failed(
                Foundry.Deploy.Services.Localization.LocalizationText.GetString("Preflight.NotReady"),
                DeploymentFailure.Guard(DeploymentOperationNames.PrepareTargetDisk, DeploymentFailureReasons.InvalidState, "preflight_not_ready"));
        }

        string workingDirectory = context.ResolveWorkspaceTempPath("Deployment");
        Directory.CreateDirectory(workingDirectory);

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (preflight.UsesTargetStorage)
            {
                context.EmitCurrentStepIndeterminate("Checking deployment readiness...", "Checking source access...", DeploymentOperationNames.ProbeOperatingSystemSource);
                long? sourceSize = await _sourceProbe.ProbeAsync((context.Request.OperatingSystem as OperatingSystemCatalogItem)?.Url ?? string.Empty, cancellationToken).ConfigureAwait(false);
                DeploymentCapacityPolicy.EnsureTargetCapacity(context, null, Math.Max(preflight.SourceSizeBytes, sourceSize ?? 0), preflight.TargetDriverBytes);
            }
            else
            {
                bool readable;
                if (context.Request.OperatingSystem is CustomImageSelection custom)
                {
                    readable = preflight.CustomSourceLease?.IsReadable() == true &&
                        await context.IsCustomSourceSeparateAsync(custom.Asset, preflight.ImagePath!, cancellationToken).ConfigureAwait(false);
                }
                else readable = await context.IsExternalStorageAsync(preflight.ImagePath!, cancellationToken).ConfigureAwait(false) && preflight.SourceLease!.ReadByte() >= 0;
                if (!readable)
                {
                    throw PreflightDeploymentStep.Guard("Preflight.NotReady", "preflight_not_ready");
                }
                if (preflight.SourceLease is not null) preflight.SourceLease.Position = 0;
                DeploymentCapacityPolicy.EnsureTargetCapacity(context, preflight.Image, 0, preflight.TargetDriverBytes);
            }
        }
        catch (DeploymentOperationException exception)
        {
            return DeploymentStepResult.Failed(exception.Message, exception.Failure);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or OverflowException)
        {
            return DeploymentStepResult.Failed(
                Foundry.Deploy.Services.Localization.LocalizationText.GetString("Preflight.NotReady"),
                DeploymentFailure.Guard(DeploymentOperationNames.PreflightDeployment, DeploymentFailureReasons.InvalidState, "preflight_not_ready"));
        }

        context.EmitCurrentStepIndeterminate(
            "Preparing target disk layout...",
            "Revalidating target disk...",
            DeploymentOperationNames.ValidateTargetDisk);
        (_, DeploymentStepResult? validationFailure) = await context.TryGetValidatedTargetDiskAsync(cancellationToken).ConfigureAwait(false);
        if (validationFailure is not null)
        {
            return validationFailure;
        }

        context.EmitCurrentStepIndeterminate(
            "Preparing target disk layout...",
            "Partitioning target disk...",
            DeploymentOperationNames.PartitionTargetDisk);
        DeploymentTargetLayout layout;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            preflight.ErasureStarted = true;
            layout = await _windowsDeploymentService
                .PrepareTargetDiskAsync(
                    context.Request.TargetDiskIdentity!,
                    workingDirectory,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (DeploymentOperationException exception) when (exception.Failure.Kind == DeploymentFailureKinds.Validation)
        {
            return DeploymentStepResult.Failed(exception.Message, exception.Failure);
        }

        context.RuntimeState.TargetSystemPartitionRoot = layout.SystemPartitionRoot;
        context.RuntimeState.TargetWindowsPartitionRoot = layout.WindowsPartitionRoot;
        context.RuntimeState.TargetRecoveryPartitionRoot = layout.RecoveryPartitionRoot;
        context.RuntimeState.TargetRecoveryPartitionLetter = layout.RecoveryPartitionLetter;
        context.RuntimeState.TargetFoundryRoot = Path.Combine(layout.WindowsPartitionRoot, "Foundry");

        context.EmitCurrentStepIndeterminate(
            "Preparing target disk layout...",
            "Preparing target workspace...",
            DeploymentOperationNames.PrepareTargetWorkspace);
        await context.RebindLogSessionToTargetAsync(context.RuntimeState.TargetFoundryRoot, cancellationToken).ConfigureAwait(false);

        await context.AppendLogAsync(
            DeploymentLogLevel.Info,
            $"Target disk prepared: system='{layout.SystemPartitionRoot}', windows='{layout.WindowsPartitionRoot}', recovery='{layout.RecoveryPartitionRoot}'.",
            cancellationToken).ConfigureAwait(false);

        return DeploymentStepResult.Succeeded("Target disk layout prepared.");
    }

    protected override async Task<DeploymentStepResult> ExecuteDryRunAsync(DeploymentStepExecutionContext context, CancellationToken cancellationToken)
    {
        string targetRoot = context.ResolveWorkspaceTempPath("DryRunTarget");
        string systemRoot = Path.Combine(targetRoot, "System");
        string windowsRoot = Path.Combine(targetRoot, "Windows");
        string recoveryRoot = Path.Combine(targetRoot, "Recovery");

        context.EmitCurrentStepIndeterminate(
            "Preparing target disk layout...",
            "Creating simulated partitions...",
            DeploymentOperationNames.PartitionTargetDisk);
        Directory.CreateDirectory(systemRoot);
        Directory.CreateDirectory(windowsRoot);
        Directory.CreateDirectory(recoveryRoot);

        context.RuntimeState.TargetSystemPartitionRoot = systemRoot;
        context.RuntimeState.TargetWindowsPartitionRoot = windowsRoot;
        context.RuntimeState.TargetRecoveryPartitionRoot = recoveryRoot;
        context.RuntimeState.TargetRecoveryPartitionLetter = 'R';
        context.RuntimeState.TargetFoundryRoot = Path.Combine(windowsRoot, "Foundry");

        context.EmitCurrentStepIndeterminate(
            "Preparing target disk layout...",
            "Preparing target workspace...",
            DeploymentOperationNames.PrepareTargetWorkspace);
        await context.RebindLogSessionToTargetAsync(context.RuntimeState.TargetFoundryRoot, cancellationToken).ConfigureAwait(false);

        await context.AppendLogAsync(
            DeploymentLogLevel.Info,
            $"[DRY-RUN] Simulated target disk layout: system='{systemRoot}', windows='{windowsRoot}', recovery='{recoveryRoot}'.",
            cancellationToken).ConfigureAwait(false);
        await Task.Delay(120, cancellationToken).ConfigureAwait(false);

        return DeploymentStepResult.Succeeded("Target disk layout prepared (simulation).");
    }
}
