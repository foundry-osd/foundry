using Microsoft.Extensions.Logging;

namespace Foundry.Deploy.Services.Deployment;

public sealed class DeploymentExecutionService : IDeploymentExecutionService
{
    private readonly IDeploymentOrchestrator _deploymentOrchestrator;
    private readonly ILogger<DeploymentExecutionService> _logger;

    public DeploymentExecutionService(
        IDeploymentOrchestrator deploymentOrchestrator,
        ILogger<DeploymentExecutionService> logger)
    {
        _deploymentOrchestrator = deploymentOrchestrator;
        _logger = logger;
    }

    public async Task<DeploymentExecutionRunResult> ExecuteAsync(DeploymentContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        try
        {
            DeploymentResult result = await _deploymentOrchestrator
                .RunAsync(context)
                .ConfigureAwait(false);

            _logger.LogInformation(
                "Deployment run completed. IsSuccess={IsSuccess}, LogsDirectoryPath={LogsDirectoryPath}",
                result.IsSuccess,
                result.LogsDirectoryPath);

            return new DeploymentExecutionRunResult
            {
                IsSuccess = result.IsSuccess,
                Message = result.Message,
                LogsDirectoryPath = result.LogsDirectoryPath
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Deployment execution failed.");
            return new DeploymentExecutionRunResult
            {
                IsSuccess = false,
                Message = ex.Message
            };
        }
    }
}
