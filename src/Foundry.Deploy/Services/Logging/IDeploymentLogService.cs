// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Deploy.Services.Logging;

public interface IDeploymentLogService
{
    DeploymentLogSession Initialize(string rootPath);
    Task AppendAsync(DeploymentLogSession session, DeploymentLogLevel level, string message, CancellationToken cancellationToken = default);
    Task SaveStateAsync<TState>(DeploymentLogSession session, TState state, CancellationToken cancellationToken = default);
    void Release(DeploymentLogSession session);
}
