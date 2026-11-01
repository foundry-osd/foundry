// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Models.Autopilot;
using Foundry.Deploy.Services.Download;

namespace Foundry.Deploy.Services.Autopilot;

/// <summary>
/// Installs and configures <c>Foundry.AutopilotInteractive</c> in an applied Windows image, for Foundry.PostInstall to run before OOBE.
/// </summary>
public interface IAutopilotInteractiveProvisioningService
{
    /// <summary>
    /// Creates the runtime, state and logs folders, installs the verified runtime archive into the runtime folder and writes the
    /// runtime configuration next to the executable.
    /// </summary>
    /// <param name="targetWindowsPartitionRoot">Root of the applied Windows partition.</param>
    /// <param name="origin">Archive resolved by the deployment preflight.</param>
    /// <param name="configuration">Runtime configuration the application reads at startup.</param>
    /// <param name="progress">Receives download progress, or <see langword="null"/>.</param>
    /// <param name="cancellationToken">Cancels the installation.</param>
    /// <returns>Offline paths of the installed runtime.</returns>
    Task<AutopilotInteractiveProvisioningResult> ProvisionAsync(
        string targetWindowsPartitionRoot,
        AutopilotInteractiveRuntimeOrigin origin,
        AutopilotInteractiveRuntimeConfiguration configuration,
        IProgress<DownloadProgress>? progress,
        CancellationToken cancellationToken);
}
