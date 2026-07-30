// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Deploy.Models;
using Foundry.Deploy.Services.Deployment.Preflight;
using Foundry.Deploy.Services.Hardware;
using Foundry.Deploy.Services.Logging;

namespace Foundry.Deploy.Services.Deployment.Steps;

public sealed class ValidateTargetConfigurationStep : DeploymentStepBase
{
    private readonly IHardwareProfileService _hardwareProfileService;
    private readonly IDeploymentPreflightService _deploymentPreflightService;

    public ValidateTargetConfigurationStep(
        IHardwareProfileService hardwareProfileService,
        IDeploymentPreflightService deploymentPreflightService)
    {
        _hardwareProfileService = hardwareProfileService;
        _deploymentPreflightService = deploymentPreflightService;
    }

    public override int Order => 3;

    public override string Name => DeploymentStepNames.ValidateTargetConfiguration;

    protected override async Task<DeploymentStepResult> ExecuteLiveAsync(DeploymentStepExecutionContext context, CancellationToken cancellationToken)
    {
        context.EmitCurrentStepIndeterminate(
            "Validating target configuration...",
            "Revalidating target disk...",
            DeploymentOperationNames.ValidateTargetDisk);
        (TargetDiskInfo? selectedDisk, DeploymentStepResult? validationFailure) = await context.TryGetValidatedTargetDiskAsync(cancellationToken).ConfigureAwait(false);
        if (validationFailure is not null)
        {
            return validationFailure;
        }

        if (string.IsNullOrWhiteSpace(context.Request.OperatingSystem.Url))
        {
            return DeploymentStepResult.Failed("Operating system URL is missing.");
        }

        if (context.Request.TargetDiskNumber < 0)
        {
            return DeploymentStepResult.Failed("Target disk number is required.");
        }

        context.EmitCurrentStepIndeterminate(
            "Validating target configuration...",
            "Detecting hardware profile...",
            DeploymentOperationNames.DetectHardware);
        HardwareProfile hardware = await _hardwareProfileService.GetCurrentAsync(cancellationToken).ConfigureAwait(false);
        context.RuntimeState.HardwareProfile = hardware;
        await context.AppendLogAsync(DeploymentLogLevel.Info, $"Detected hardware: {hardware.DisplayLabel}", cancellationToken).ConfigureAwait(false);

        DeploymentPreflightResult preflight = _deploymentPreflightService.Evaluate(hardware, selectedDisk, context.Request.OperatingSystem);
        foreach (DeploymentPreflightFinding finding in preflight.Findings)
        {
            DeploymentLogLevel level = finding.Severity == DeploymentPreflightSeverity.Blocking
                ? DeploymentLogLevel.Error
                : DeploymentLogLevel.Warning;
            await context.AppendLogAsync(
                level,
                $"Deployment preflight [{finding.Code}]: {DeploymentPreflightLocalization.FormatFinding(finding)}",
                cancellationToken).ConfigureAwait(false);
        }

        if (preflight.HasBlockingFindings)
        {
            string reasons = string.Join(
                Environment.NewLine,
                preflight.Findings
                    .Where(finding => finding.Severity == DeploymentPreflightSeverity.Blocking)
                    .Select(DeploymentPreflightLocalization.FormatFinding));
            return DeploymentStepResult.Failed(reasons);
        }

        IReadOnlyList<DeploymentPreflightFinding> unacknowledgedWarnings =
            preflight.GetUnacknowledgedWarnings(context.Request.AcknowledgedPreflightWarnings);
        if (unacknowledgedWarnings.Count > 0)
        {
            return DeploymentStepResult.Failed(string.Join(
                Environment.NewLine,
                unacknowledgedWarnings.Select(DeploymentPreflightLocalization.FormatFinding)));
        }

        return DeploymentStepResult.Succeeded("Target configuration validated.");
    }

    protected override async Task<DeploymentStepResult> ExecuteDryRunAsync(DeploymentStepExecutionContext context, CancellationToken cancellationToken)
    {
        context.EmitCurrentStepIndeterminate(
            "Validating target configuration...",
            "Detecting hardware profile...",
            DeploymentOperationNames.DetectHardware);
        HardwareProfile hardware = await _hardwareProfileService.GetCurrentAsync(cancellationToken).ConfigureAwait(false);
        context.RuntimeState.HardwareProfile = hardware;
        await context.AppendLogAsync(DeploymentLogLevel.Info, $"[DRY-RUN] Hardware detected: {hardware.DisplayLabel}", cancellationToken).ConfigureAwait(false);
        await Task.Delay(120, cancellationToken).ConfigureAwait(false);

        return DeploymentStepResult.Succeeded("Target configuration validated (simulation).");
    }
}
