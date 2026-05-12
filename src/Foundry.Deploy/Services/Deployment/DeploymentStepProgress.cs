namespace Foundry.Deploy.Services.Deployment;

/// <summary>
/// Describes the current deployment step progress shown by the shell.
/// </summary>
public sealed record DeploymentStepProgress
{
    /// <summary>
    /// Gets the deployment step name.
    /// </summary>
    public required string StepName { get; init; }

    /// <summary>
    /// Gets the current step state.
    /// </summary>
    public required DeploymentStepState State { get; init; }

    /// <summary>
    /// Gets the one-based index of the current step.
    /// </summary>
    public required int StepIndex { get; init; }

    /// <summary>
    /// Gets the total number of planned steps.
    /// </summary>
    public required int StepCount { get; init; }

    /// <summary>
    /// Gets the overall deployment progress percentage.
    /// </summary>
    public required int ProgressPercent { get; init; }

    /// <summary>
    /// Gets the optional primary step message.
    /// </summary>
    public string? Message { get; init; }

    /// <summary>
    /// Gets optional nested progress for the current step.
    /// </summary>
    public double? StepSubProgressPercent { get; init; }

    /// <summary>
    /// Gets a value indicating whether nested progress should be rendered as indeterminate.
    /// </summary>
    public bool StepSubProgressIndeterminate { get; init; } = true;

    /// <summary>
    /// Gets the optional nested progress label.
    /// </summary>
    public string? StepSubProgressLabel { get; init; }
}
