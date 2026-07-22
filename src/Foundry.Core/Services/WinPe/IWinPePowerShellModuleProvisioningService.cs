// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Core.Services.WinPe;

/// <summary>
/// Integrates PowerShell modules (from the Gallery or a local folder) into a mounted WinPE boot image.
/// </summary>
public interface IWinPePowerShellModuleProvisioningService
{
    /// <summary>
    /// Downloads and/or copies each selected module into the boot image's Windows PowerShell modules folder.
    /// </summary>
    Task<WinPeResult> ProvisionAsync(
        WinPePowerShellModuleProvisioningOptions options,
        CancellationToken cancellationToken = default);
}
