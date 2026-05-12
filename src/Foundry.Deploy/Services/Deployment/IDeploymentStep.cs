namespace Foundry.Deploy.Services.Deployment;

/// <summary>
/// Represents one ordered step in the Foundry.Deploy workflow.
/// </summary>
public interface IDeploymentStep
{
    /// <summary>
    /// Gets the numeric execution order used by the orchestrator.
    /// </summary>
    int Order { get; }

    /// <summary>
    /// Gets the stable step name used for progress, validation, and runtime state.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Executes the deployment step.
    /// </summary>
    /// <param name="context">The shared deployment execution context.</param>
    /// <param name="cancellationToken">A token used to cancel the step.</param>
    /// <returns>The step result.</returns>
    Task<DeploymentStepResult> ExecuteAsync(DeploymentStepExecutionContext context, CancellationToken cancellationToken);
}
