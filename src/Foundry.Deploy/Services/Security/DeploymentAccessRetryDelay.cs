// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Services.Security;

namespace Foundry.Deploy.Services.Security;

public sealed class DeploymentAccessRetryDelay : IDeploymentAccessRetryDelay
{
    public Task WaitAsync(int failedAttemptNumber, CancellationToken cancellationToken)
    {
        return Task.Delay(GetDelay(failedAttemptNumber), cancellationToken);
    }

    internal static TimeSpan GetDelay(int failedAttemptNumber)
    {
        return DeploymentPasswordRetryPolicy.GetDelay(failedAttemptNumber);
    }
}
