namespace Foundry.Deploy.Services.Deployment;

/// <summary>
/// Coordinates ordered deployment steps and emits user-facing progress.
/// </summary>
public interface IDeploymentOrchestrator
{
    /// <summary>
    /// Gets deployment step names in execution order.
    /// </summary>
    IReadOnlyList<string> PlannedSteps { get; }

    /// <summary>
    /// Occurs when the active deployment step reports progress.
    /// </summary>
    event EventHandler<DeploymentStepProgress>? StepProgressChanged;

    /// <summary>
    /// Executes the deployment request.
    /// </summary>
    /// <param name="context">Deployment request selected by the user.</param>
    /// <param name="cancellationToken">Token that cancels the deployment.</param>
    /// <returns>The final deployment result.</returns>
    Task<DeploymentResult> RunAsync(DeploymentContext context, CancellationToken cancellationToken = default);
}
