namespace Foundry.Deploy.Services.Deployment;

public interface IDeploymentOrchestrator
{
    IReadOnlyList<string> PlannedSteps { get; }

    event EventHandler<DeploymentStepProgress>? StepProgressChanged;

    Task<DeploymentResult> RunAsync(DeploymentContext context, CancellationToken cancellationToken = default);
}
