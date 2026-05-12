namespace Foundry.Deploy.Services.Deployment;

/// <summary>
/// Represents the outcome of a single deployment step.
/// </summary>
public sealed record DeploymentStepResult
{
    /// <summary>
    /// Gets the final state reported by the step.
    /// </summary>
    public required DeploymentStepState State { get; init; }

    /// <summary>
    /// Gets the user-facing step result message.
    /// </summary>
    public required string Message { get; init; }

    /// <summary>
    /// Creates a successful step result.
    /// </summary>
    /// <param name="message">Result message.</param>
    /// <returns>A successful step result.</returns>
    public static DeploymentStepResult Succeeded(string message)
        => new() { State = DeploymentStepState.Succeeded, Message = message };

    /// <summary>
    /// Creates a skipped step result.
    /// </summary>
    /// <param name="message">Result message.</param>
    /// <returns>A skipped step result.</returns>
    public static DeploymentStepResult Skipped(string message)
        => new() { State = DeploymentStepState.Skipped, Message = message };

    /// <summary>
    /// Creates a failed step result.
    /// </summary>
    /// <param name="message">Result message.</param>
    /// <returns>A failed step result.</returns>
    public static DeploymentStepResult Failed(string message)
        => new() { State = DeploymentStepState.Failed, Message = message };
}
