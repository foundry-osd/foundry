namespace Foundry.Deploy.Services.Deployment;

public interface IDeploymentOrchestrator
{
    IReadOnlyList<string> PlannedSteps { get; }

    event EventHandler<DeploymentStepProgress>? StepProgressChanged;
    event EventHandler<string>? LogEmitted;

    Task<DeploymentResult> RunAsync(DeploymentContext context, CancellationToken cancellationToken = default);
}
