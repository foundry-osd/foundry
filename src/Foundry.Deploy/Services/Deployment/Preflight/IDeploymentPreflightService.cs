// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Deploy.Models;

namespace Foundry.Deploy.Services.Deployment.Preflight;

/// <summary>
/// Evaluates Windows 11 firmware, security, architecture, and storage prerequisites.
/// </summary>
public interface IDeploymentPreflightService
{
    DeploymentPreflightResult Evaluate(
        HardwareProfile? hardware,
        TargetDiskInfo? targetDisk,
        OperatingSystemCatalogItem? operatingSystem);
}
