namespace Foundry.Deploy.Services.Deployment;

public sealed record DeploymentStepResult
{
    public required DeploymentStepState State { get; init; }

    public required string Message { get; init; }

    public static DeploymentStepResult Succeeded(string message)
        => new() { State = DeploymentStepState.Succeeded, Message = message };

    public static DeploymentStepResult Skipped(string message)
        => new() { State = DeploymentStepState.Skipped, Message = message };

    public static DeploymentStepResult Failed(string message)
        => new() { State = DeploymentStepState.Failed, Message = message };
}
