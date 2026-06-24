// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Deploy.Services.Logging;

public sealed record DeploymentLogSession
{
    public required string RootPath { get; init; }
    public required string LogsDirectoryPath { get; init; }
    public required string StateDirectoryPath { get; init; }
    public required string LogFilePath { get; init; }
    public required string StateFilePath { get; init; }
}
