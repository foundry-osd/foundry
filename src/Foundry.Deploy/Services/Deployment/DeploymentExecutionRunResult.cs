// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Deploy.Services.Deployment;

public sealed record DeploymentExecutionRunResult
{
    public required bool IsSuccess { get; init; }
    public required string Message { get; init; }
    public string LogsDirectoryPath { get; init; } = string.Empty;
}
