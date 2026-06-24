// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Deploy.Services.Runtime;

namespace Foundry.Deploy.Services.Startup;

public sealed record DeploymentStartupRequest
{
    public required DeploymentRuntimeContext RuntimeContext { get; init; }
    public required bool IsDebugSafeMode { get; init; }
    public required string FallbackComputerName { get; init; }
}
