// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Deploy.Services.Deployment;

/// <summary>
/// Describes a deployment failure using stable, low-cardinality telemetry values.
/// </summary>
/// <param name="OperationName">Logical deployment operation that failed.</param>
/// <param name="Kind">Broad failure category.</param>
/// <param name="Reason">Stable failure reason.</param>
/// <param name="Code">Optional normalized process, HTTP, or application code.</param>
public sealed record DeploymentFailure(
    string OperationName,
    string Kind,
    string Reason,
    string? Code = null)
{
    /// <summary>
    /// Creates an explicitly classified validation failure for a deployment guard condition.
    /// </summary>
    /// <param name="operationName">Logical deployment operation that failed.</param>
    /// <param name="reason">Stable validation reason.</param>
    /// <param name="code">Stable application code.</param>
    /// <returns>The classified deployment failure.</returns>
    public static DeploymentFailure Guard(string operationName, string reason, string code) =>
        new(operationName, DeploymentFailureKinds.Validation, reason, code);
}
