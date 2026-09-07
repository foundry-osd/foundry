// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Deploy.Services.Deployment;

/// <summary>
/// Defines preparation of the confirmed target disk.
/// </summary>
public interface IWindowsDeploymentService
{
    /// <summary>
    /// Cleans and repartitions the target disk for UEFI Windows deployment.
    /// </summary>
    /// <param name="expectedDisk">The exact device snapshot retained at destructive confirmation.</param>
    /// <param name="workingDirectory">The directory used for temporary scripts.</param>
    /// <param name="cancellationToken">A token used to cancel disk preparation.</param>
    /// <returns>The resulting target partition layout.</returns>
    Task<DeploymentTargetLayout> PrepareTargetDiskAsync(
        TargetDiskIdentity expectedDisk,
        string workingDirectory,
        CancellationToken cancellationToken = default);

}
