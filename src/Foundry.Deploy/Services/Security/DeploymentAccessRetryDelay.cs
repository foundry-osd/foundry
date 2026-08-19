// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Deploy.Services.Security;

public sealed class DeploymentAccessRetryDelay : IDeploymentAccessRetryDelay
{
    public Task WaitAsync(CancellationToken cancellationToken) =>
        Task.Delay(TimeSpan.FromMilliseconds(500), cancellationToken);
}
