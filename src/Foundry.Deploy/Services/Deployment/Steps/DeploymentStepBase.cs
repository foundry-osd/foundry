// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Deploy.Services.Deployment.Steps;

public abstract class DeploymentStepBase : IDeploymentStep
{
    public abstract int Order { get; }

    public abstract string Name { get; }

    public Task<DeploymentStepResult> ExecuteAsync(DeploymentStepExecutionContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        return context.Request.IsDryRun
            ? ExecuteDryRunAsync(context, cancellationToken)
            : ExecuteLiveAsync(context, cancellationToken);
    }

    protected abstract Task<DeploymentStepResult> ExecuteLiveAsync(DeploymentStepExecutionContext context, CancellationToken cancellationToken);

    protected abstract Task<DeploymentStepResult> ExecuteDryRunAsync(DeploymentStepExecutionContext context, CancellationToken cancellationToken);
}
