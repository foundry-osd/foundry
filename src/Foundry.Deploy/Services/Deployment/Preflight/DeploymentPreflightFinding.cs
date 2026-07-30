// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Deploy.Services.Deployment.Preflight;

/// <summary>
/// Represents one localized deployment-readiness message and its enforcement severity.
/// </summary>
public sealed record DeploymentPreflightFinding
{
    public required string Code { get; init; }
    public required DeploymentPreflightSeverity Severity { get; init; }
    public required string MessageResourceKey { get; init; }
    public IReadOnlyList<string> MessageArguments { get; init; } = [];

    /// <summary>
    /// Gets the stable code and argument signature used to track an acknowledged warning.
    /// </summary>
    public string AcknowledgementKey => string.Join(
        "\u001f",
        new[] { Code }.Concat(MessageArguments));
}
