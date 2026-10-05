// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Deploy.Services.Deployment.Unattend;
using Foundry.Deploy.Services.Logging;

namespace Foundry.Deploy.Services.Deployment.Steps;

/// <summary>Validates and retains custom answer-file bytes before any destructive disk operation.</summary>
public sealed class ValidateCustomUnattendStep(UnattendContentService contentService) : DeploymentStepBase
{
    public override string Name => DeploymentStepNames.ValidateCustomUnattend;

    protected override async Task<DeploymentStepResult> ExecuteLiveAsync(DeploymentStepExecutionContext context, CancellationToken cancellationToken)
    {
        if (context.Request.Unattend is null)
        {
            return DeploymentStepResult.Skipped("Native Foundry settings selected.");
        }
        cancellationToken.ThrowIfCancellationRequested();
        context.UnattendSnapshot?.Dispose();
        context.UnattendSnapshot = contentService.Read(context.Request.Unattend, context.Request.OperatingSystem.Architecture,
            context.Request.IsAutopilotEnabled, context.Request.AutopilotProvisioningMode,
            context.Request.DomainJoinRequest?.Disposition is DomainJoin.DomainJoinDeploymentDisposition.Ready or DomainJoin.DomainJoinDeploymentDisposition.DryRun);
        if (context.Request.DomainJoinIntent is { } intent && !string.Equals(
            context.UnattendSnapshot.Inspection.ConcreteComputerName, intent.ComputerName, StringComparison.Ordinal))
            return DeploymentStepResult.Failed("The custom answer-file computer name differs from the computer name confirmed for the domain join.",
                DeploymentFailure.Guard(DeploymentOperationNames.ValidateTarget, DeploymentFailureReasons.InvalidInput,
                    "domain_custom_name_mismatch"));
        if (PreOobe.PreOobeContentResolver.IsRequired(context.Request))
            context.UnattendSnapshot.ValidatePostInstallHook(context.Request.OperatingSystem.Architecture);
        if (context.UnattendSnapshot.Inspection.HasCommands)
        {
            await context.AppendLogAsync(DeploymentLogLevel.Warning,
                "Custom commands may overlap with Foundry setup hooks, network roaming, customization and enrollment. Compatibility requires a deployment test.", cancellationToken).ConfigureAwait(false);
        }
        return DeploymentStepResult.Succeeded("Custom answer file validated before disk preparation.");
    }

    protected override Task<DeploymentStepResult> ExecuteDryRunAsync(DeploymentStepExecutionContext context, CancellationToken cancellationToken) =>
        ExecuteLiveAsync(context, cancellationToken);
}
