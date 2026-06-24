// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Deploy.Services.Runtime;

public interface IDeploymentRuntimeContextService
{
    DeploymentRuntimeContext Resolve();
}
