// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Deploy.Services.Deployment;

/// <summary>
/// Represents the final outcome returned by the deployment orchestrator.
/// </summary>
public sealed record DeploymentResult
{
    /// <summary>
    /// Gets a value indicating whether all required deployment steps completed successfully.
    /// </summary>
    public required bool IsSuccess { get; init; }

    /// <summary>
    /// Gets the final user-facing result message.
    /// </summary>
    public required string Message { get; init; }

    /// <summary>
    /// Gets the directory that contains deployment logs.
    /// </summary>
    public string LogsDirectoryPath { get; init; } = string.Empty;
}
