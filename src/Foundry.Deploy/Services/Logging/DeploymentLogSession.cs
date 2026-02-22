namespace Foundry.Deploy.Services.Logging;

public sealed record DeploymentLogSession
{
    public required string RootPath { get; init; }
    public required string LogsDirectoryPath { get; init; }
    public required string StateDirectoryPath { get; init; }
    public required string LogFilePath { get; init; }
    public required string StateFilePath { get; init; }
}
