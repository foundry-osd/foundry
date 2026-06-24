// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Deploy.Services.Deployment.PreOobe;

/// <summary>
/// Reports the files written while staging the pre-OOBE script runner.
/// </summary>
public sealed record PreOobeScriptProvisioningResult
{
    /// <summary>
    /// Gets the offline path to the staged SetupComplete.cmd file.
    /// </summary>
    public required string SetupCompletePath { get; init; }

    /// <summary>
    /// Gets the offline path to the generated PowerShell runner.
    /// </summary>
    public required string RunnerPath { get; init; }

    /// <summary>
    /// Gets the offline path to the generated execution manifest.
    /// </summary>
    public required string ManifestPath { get; init; }

    /// <summary>
    /// Gets the offline paths to the staged embedded PowerShell scripts.
    /// </summary>
    public required IReadOnlyList<string> StagedScriptPaths { get; init; }
}
