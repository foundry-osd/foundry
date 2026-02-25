namespace Foundry.Deploy.Services.Deployment;

public sealed record DeploymentResult
{
    public required bool IsSuccess { get; init; }
    public required string Message { get; init; }
    public string LogsDirectoryPath { get; init; } = string.Empty;
}
