// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Deploy.Services.Deployment;

/// <summary>
/// Coordinates ordered deployment steps and emits user-facing progress.
/// </summary>
public interface IDeploymentOrchestrator
{
    /// <summary>
    /// Occurs when the active deployment step reports progress.
    /// </summary>
    event EventHandler<DeploymentStepProgress>? StepProgressChanged;

    /// <summary>
    /// Occurs synchronously before committing the terminal outcome. UI consumers must stop accepting
    /// cancellation before returning; previously accepted requests are checked afterward.
    /// The operation remains busy until terminal persistence, telemetry and cleanup finish.
    /// </summary>
    event EventHandler? CompletionStarting;

    /// <summary>
    /// Executes the deployment request when no other deployment is active.
    /// </summary>
    /// <param name="context">Deployment request selected by the user.</param>
    /// <param name="cancellationToken">Token that cancels the deployment.</param>
    /// <returns>The final deployment result, or a failed result when the single-operation gate rejects the request.</returns>
    Task<DeploymentResult> RunAsync(DeploymentContext context, CancellationToken cancellationToken = default);
}
