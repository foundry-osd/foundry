// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Deploy.Services.Deployment.Preflight;

/// <summary>
/// Contains the actionable findings produced for one deployment selection.
/// </summary>
public sealed record DeploymentPreflightResult
{
    public required IReadOnlyList<DeploymentPreflightFinding> Findings { get; init; }

    public bool HasBlockingFindings => Findings.Any(finding => finding.Severity == DeploymentPreflightSeverity.Blocking);

    public bool HasWarnings => Findings.Any(finding => finding.Severity == DeploymentPreflightSeverity.Warning);

    public IReadOnlyList<DeploymentPreflightFinding> GetUnacknowledgedWarnings(
        IReadOnlyCollection<string> acknowledgedWarnings)
    {
        ArgumentNullException.ThrowIfNull(acknowledgedWarnings);

        return Findings
            .Where(finding =>
                finding.Severity == DeploymentPreflightSeverity.Warning &&
                !acknowledgedWarnings.Contains(finding.AcknowledgementKey, StringComparer.Ordinal))
            .ToArray();
    }
}
