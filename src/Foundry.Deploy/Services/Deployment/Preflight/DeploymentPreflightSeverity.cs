// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Deploy.Services.Deployment.Preflight;

/// <summary>
/// Determines whether a deployment finding requires acknowledgement or prevents deployment.
/// </summary>
public enum DeploymentPreflightSeverity
{
    Warning,
    Blocking
}
