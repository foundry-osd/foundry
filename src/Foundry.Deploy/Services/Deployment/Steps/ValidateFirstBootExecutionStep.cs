// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Deploy.Models.Configuration;
using Foundry.Deploy.Services.Deployment.PreOobe;
using Foundry.Deploy.Services.DriverPacks;

namespace Foundry.Deploy.Services.Deployment.Steps;

/// <summary>Rejects hook-dependent deployments before erasure when their entry point is unproven.</summary>
public sealed class ValidateFirstBootExecutionStep(IDriverPackStrategyResolver driverPackStrategyResolver) : DeploymentStepBase
{
    public override string Name => DeploymentStepNames.ValidateFirstBootExecution;

    protected override Task<DeploymentStepResult> ExecuteLiveAsync(DeploymentStepExecutionContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        DeploymentContext request = context.Request;
        DriverPackExecutionPlan driver = driverPackStrategyResolver.Resolve(request.DriverPackSelectionKind,
            request.DriverPack, request.DriverPack?.FileName ?? string.Empty);
        bool needsCustomization =
            request.AppxRemoval.IsEnabled && request.AppxRemoval.PackageNames.Any(name => !string.IsNullOrWhiteSpace(name)) ||
            request.AiComponentRemoval.IsEnabled && PreOobeScriptDefinitionBuilder.HasAnyAiComponentRemovalAppxOptionEnabled(request.AiComponentRemoval) ||
            request.Network.ProfileRoaming.IsAnyEnabled || driver.InstallMode == DriverPackInstallMode.DeferredSetupComplete;
        bool needsInteractive = request.IsAutopilotEnabled && request.AutopilotProvisioningMode == AutopilotProvisioningMode.InteractiveHardwareHashUpload;

        // Catalog channel describes the source image, not a firmware/custom key used by Windows Setup.
        // No current native qualification establishes the selected actions in specialize.
        FirstBootExecutionPlan plan = FirstBootExecutionPolicy.Evaluate(request.OperatingSystem.Edition,
            request.OperatingSystem.LicenseChannel, request.UsesCustomUnattend,
            context.UnattendSnapshot?.Inspection.HasFoundrySpecializeLauncher == true,
            effectiveKeyChannelKnown: false, needsCustomization, needsInteractive,
            areSpecializeActionsQualified: false);
        context.RuntimeState.FirstBootExecutionPlan = plan;
        return Task.FromResult(plan.FailureCode is null
            ? DeploymentStepResult.Succeeded("First-boot entry points validated; execution remains pending after deployment.")
            : DeploymentStepResult.Failed(
                "The selected first-boot actions require a setup hook that is not proven for this Windows edition and effective license channel. Use a supported Enterprise/Server image or remove the hook-dependent features before deployment.",
                DeploymentFailure.Guard("Validate first-boot execution", DeploymentFailureReasons.InvalidInput, plan.FailureCode)));
    }

    protected override Task<DeploymentStepResult> ExecuteDryRunAsync(DeploymentStepExecutionContext context, CancellationToken cancellationToken)
        => ExecuteLiveAsync(context, cancellationToken);
}
