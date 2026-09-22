// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.IO;
using Foundry.Deploy.Models.Configuration;
using Foundry.Deploy.Services.Logging;

namespace Foundry.Deploy.Services.Deployment.Steps;

/// <summary>Applies offline AI policies independently of native or custom Windows answer files.</summary>
public sealed class ConfigureAiPoliciesStep(IWindowsDeploymentService windowsDeploymentService) : DeploymentStepBase
{
    public override string Name => DeploymentStepNames.ConfigureAiPolicies;

    /// <summary>Identifies policy work; AI Hub removal alone requires only a post-reboot script.</summary>
    public static bool IsRequired(DeployAiComponentRemovalSettings settings) =>
        settings.IsEnabled &&
        (settings.RemoveCopilot || settings.DisableRecall || settings.DisableClickToDo ||
         settings.DisableAiServiceAutoStart || settings.DisableEdgeAi || settings.DisablePaintAi || settings.DisableNotepadAi);

    protected override Task<DeploymentStepResult> ExecuteLiveAsync(DeploymentStepExecutionContext context, CancellationToken cancellationToken) =>
        ConfigureAsync(context, false, cancellationToken);

    protected override Task<DeploymentStepResult> ExecuteDryRunAsync(DeploymentStepExecutionContext context, CancellationToken cancellationToken) =>
        ConfigureAsync(context, true, cancellationToken);

    private async Task<DeploymentStepResult> ConfigureAsync(
        DeploymentStepExecutionContext context,
        bool dryRun,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!IsRequired(context.RuntimeState.AiComponentRemoval))
        {
            return DeploymentStepResult.Skipped("Offline customization disabled.");
        }

        if (string.IsNullOrWhiteSpace(context.RuntimeState.TargetWindowsPartitionRoot))
        {
            return DeploymentStepResult.Failed(
                "Target Windows partition is unavailable.",
                DeploymentFailure.Guard(DeploymentOperationNames.WriteAiPolicyRegistry,
                    DeploymentFailureReasons.MissingResource, "missing_target_partition"));
        }

        context.EmitCurrentStepIndeterminate("Configuring AI component removal...", "Writing offline AI policies...", DeploymentOperationNames.WriteAiPolicyRegistry);
        if (!dryRun)
        {
            string workingDirectory = Path.Combine(context.EnsureTargetFoundryRoot(), "Temp", "Deployment");
            Directory.CreateDirectory(workingDirectory);
            await windowsDeploymentService.ConfigureOfflineAiComponentRemovalAsync(
                context.RuntimeState.TargetWindowsPartitionRoot,
                context.RuntimeState.AiComponentRemoval,
                workingDirectory,
                cancellationToken).ConfigureAwait(false);
        }

        await context.AppendLogAsync(DeploymentLogLevel.Info,
            dryRun ? "[DRY-RUN] Simulated offline AI policies." : "Offline AI policies configured.", cancellationToken).ConfigureAwait(false);
        return DeploymentStepResult.Succeeded(dryRun
            ? "Offline customization configured (simulation)."
            : "Offline customization configured.");
    }
}
