// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Core.Services.WinPe;

/// <summary>
/// Integrates PowerShell 7 into a mounted WinPE boot image.
/// </summary>
public interface IWinPePowerShell7ProvisioningService
{
    /// <summary>
    /// Downloads the selected PowerShell 7 release (falling back to the latest on failure), extracts it into
    /// the image, and wires PATH/PSModulePath plus ICU so pwsh is usable from WinPE.
    /// </summary>
    Task<WinPeResult> ProvisionAsync(
        WinPePowerShell7ProvisioningOptions options,
        CancellationToken cancellationToken = default);
}
