// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Microsoft.Extensions.Logging;
using Foundry.Deploy.Services.Configuration;
using Foundry.Deploy.Services.Security;

namespace Foundry.Deploy.Services.Deployment;

public sealed class DeploymentExecutionService : IDeploymentExecutionService
{
    private readonly IDeploymentOrchestrator _deploymentOrchestrator;
    private readonly IDeployConfigurationService _configurationService;
    private readonly IDeploymentSecretKeySession _deploymentSecretKeySession;
    private readonly ILogger<DeploymentExecutionService> _logger;

    public DeploymentExecutionService(
        IDeploymentOrchestrator deploymentOrchestrator,
        IDeployConfigurationService configurationService,
        IDeploymentSecretKeySession deploymentSecretKeySession,
        ILogger<DeploymentExecutionService> logger)
    {
        _deploymentOrchestrator = deploymentOrchestrator;
        _configurationService = configurationService;
        _deploymentSecretKeySession = deploymentSecretKeySession;
        _logger = logger;
    }

    public async Task<DeploymentExecutionRunResult> ExecuteAsync(DeploymentContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        try
        {
            DeployConfigurationLoadResult configuration = _configurationService.LoadOptional();
            if (configuration.Exists && configuration.Document is null)
            {
                return AccessDenied();
            }

            if (configuration.Document?.Protection.IsEnabled == true && !_deploymentSecretKeySession.IsUnlocked)
            {
                return AccessDenied();
            }

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

    private static DeploymentExecutionRunResult AccessDenied() => new()
    {
        IsSuccess = false,
        Message = "Deployment access has not been authorized."
    };
}
