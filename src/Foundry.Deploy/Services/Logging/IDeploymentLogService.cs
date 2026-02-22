namespace Foundry.Deploy.Services.Logging;

public interface IDeploymentLogService
{
    DeploymentLogSession Initialize(string rootPath);
    Task AppendAsync(DeploymentLogSession session, DeploymentLogLevel level, string message, CancellationToken cancellationToken = default);
    Task SaveStateAsync<TState>(DeploymentLogSession session, TState state, CancellationToken cancellationToken = default);
}
