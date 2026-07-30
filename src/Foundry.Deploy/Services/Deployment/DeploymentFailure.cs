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
    string? Code = null);
