// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Deploy.Services.Localization;

namespace Foundry.Deploy.Services.Deployment.Preflight;

/// <summary>
/// Resolves localized deployment-readiness messages.
/// </summary>
public static class DeploymentPreflightLocalization
{
    public static string FormatFinding(DeploymentPreflightFinding finding)
    {
        ArgumentNullException.ThrowIfNull(finding);

        return finding.MessageArguments.Count == 0
            ? LocalizationText.GetString(finding.MessageResourceKey)
            : LocalizationText.Format(finding.MessageResourceKey, finding.MessageArguments.Cast<object>().ToArray());
    }
}
