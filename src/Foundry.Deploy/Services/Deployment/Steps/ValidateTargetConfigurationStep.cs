using Foundry.Deploy.Models;
using Foundry.Deploy.Services.Hardware;
using Foundry.Deploy.Services.Logging;

namespace Foundry.Deploy.Services.Deployment.Steps;

public sealed class ValidateTargetConfigurationStep : DeploymentStepBase
{
    private readonly IHardwareProfileService _hardwareProfileService;

    public ValidateTargetConfigurationStep(IHardwareProfileService hardwareProfileService)
    {
        _hardwareProfileService = hardwareProfileService;
    }

    public override int Order => 3;

    public override string Name => DeploymentStepNames.ValidateTargetConfiguration;

    protected override async Task<DeploymentStepResult> ExecuteLiveAsync(DeploymentStepExecutionContext context, CancellationToken cancellationToken)
    {
        (_, DeploymentStepResult? validationFailure) = await context.TryGetValidatedTargetDiskAsync(cancellationToken).ConfigureAwait(false);
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

        HardwareProfile hardware = await _hardwareProfileService.GetCurrentAsync(cancellationToken).ConfigureAwait(false);
        context.RuntimeState.HardwareProfile = hardware;
        await context.AppendLogAsync(DeploymentLogLevel.Info, $"Detected hardware: {hardware.DisplayLabel}", cancellationToken).ConfigureAwait(false);
        await context.AppendLogAsync(DeploymentLogLevel.Info, $"Autopilot capable: {hardware.IsAutopilotCapable}", cancellationToken).ConfigureAwait(false);

        return DeploymentStepResult.Succeeded("Target configuration validated.");
    }

    protected override async Task<DeploymentStepResult> ExecuteDryRunAsync(DeploymentStepExecutionContext context, CancellationToken cancellationToken)
    {
        HardwareProfile hardware = await _hardwareProfileService.GetCurrentAsync(cancellationToken).ConfigureAwait(false);
        context.RuntimeState.HardwareProfile = hardware;
        await context.AppendLogAsync(DeploymentLogLevel.Info, $"[DRY-RUN] Hardware detected: {hardware.DisplayLabel}", cancellationToken).ConfigureAwait(false);
        await Task.Delay(120, cancellationToken).ConfigureAwait(false);

        return DeploymentStepResult.Succeeded("Target configuration validated (simulation).");
    }
}
