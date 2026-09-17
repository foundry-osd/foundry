// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Microsoft.Extensions.Logging;
using Foundry.Deploy.Models.Configuration;
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

    public async Task<DeploymentExecutionRunResult> ExecuteAsync(DeploymentContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            DeployConfigurationLoadResult configuration = _configurationService.LoadOptional();
            if (configuration.Exists && configuration.Document is null)
            {
                return AccessDenied();
            }

            DeployProtectionSettings protection = configuration.Document?.Protection ?? new DeployProtectionSettings();
            bool requiresAuthorization = DeploymentProtectionDetector.RequiresUnlock(protection) ||
                                         DeploymentProtectionDetector.HasProtectedArtifacts(configuration);
            if (requiresAuthorization && !_deploymentSecretKeySession.IsUnlocked)
            {
                return AccessDenied();
            }

            DeploymentResult result = await _deploymentOrchestrator
                .RunAsync(context, cancellationToken)
                .ConfigureAwait(false);

            _logger.LogInformation(
                "Deployment run completed. IsSuccess={IsSuccess}, IsCancelled={IsCancelled}, LogsDirectoryPath={LogsDirectoryPath}",
                result.IsSuccess,
                result.IsCancelled,
                result.LogsDirectoryPath);

            return new DeploymentExecutionRunResult
            {
                IsSuccess = result.IsSuccess,
                IsCancelled = result.IsCancelled,
                Message = result.Message,
                LogsDirectoryPath = result.LogsDirectoryPath
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _logger.LogInformation("Deployment execution cancelled.");
            return new DeploymentExecutionRunResult
            {
                IsSuccess = false,
                IsCancelled = true,
                Message = "Deployment cancelled."
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Deployment execution failed.");
            return new DeploymentExecutionRunResult
            {
                IsSuccess = false,
                Message = ex is TimeoutException or OperationCanceledException ? "Transfer timed out." : ex.Message
            };
        }
    }

    private static DeploymentExecutionRunResult AccessDenied() => new()
    {
        IsSuccess = false,
        Message = "Deployment access has not been authorized."
    };
}
