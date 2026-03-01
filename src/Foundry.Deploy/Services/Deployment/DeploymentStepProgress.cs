namespace Foundry.Deploy.Services.Deployment;

public sealed record DeploymentStepProgress
{
    public required string StepName { get; init; }
    public required DeploymentStepState State { get; init; }
    public required int StepIndex { get; init; }
    public required int StepCount { get; init; }
    public required int ProgressPercent { get; init; }
    public string? Message { get; init; }
    public double? StepSubProgressPercent { get; init; }
    public bool StepSubProgressIndeterminate { get; init; } = true;
    public string? StepSubProgressLabel { get; init; }
}
