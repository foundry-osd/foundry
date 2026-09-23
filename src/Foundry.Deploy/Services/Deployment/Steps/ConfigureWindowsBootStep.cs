// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.IO;

namespace Foundry.Deploy.Services.Deployment.Steps;

/// <summary>Creates UEFI boot files from the applied image before Windows recovery is configured.</summary>
public sealed class ConfigureWindowsBootStep(IWindowsDeploymentService windowsDeploymentService) : DeploymentStepBase
{
    public override string Name => DeploymentStepNames.ConfigureWindowsBoot;

    protected override async Task<DeploymentStepResult> ExecuteLiveAsync(DeploymentStepExecutionContext context, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(context.RuntimeState.TargetWindowsPartitionRoot) ||
            string.IsNullOrWhiteSpace(context.RuntimeState.TargetSystemPartitionRoot) ||
            context.RuntimeState.AppliedImageIndex is null)
        {
            return DeploymentStepResult.Failed("Windows image was not applied.",
                DeploymentFailure.Guard(DeploymentOperationNames.ConfigureBoot, DeploymentFailureReasons.MissingResource, "missing_applied_image"));
        }

        string workingDirectory = Path.Combine(context.EnsureTargetFoundryRoot(), "Temp", "Deployment");
        Directory.CreateDirectory(workingDirectory);
        context.EmitCurrentStepIndeterminate("Configuring Windows boot...", "Configuring boot...", DeploymentOperationNames.ConfigureBoot);
        await windowsDeploymentService.ConfigureBootAsync(context.RuntimeState.TargetWindowsPartitionRoot,
            context.RuntimeState.TargetSystemPartitionRoot, context.Request.OperatingSystem.BuildMajor, workingDirectory, cancellationToken).ConfigureAwait(false);
        return DeploymentStepResult.Succeeded("Windows boot configured.");
    }

    protected override Task<DeploymentStepResult> ExecuteDryRunAsync(DeploymentStepExecutionContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(DeploymentStepResult.Succeeded("Windows boot configured (simulation)."));
    }
}
