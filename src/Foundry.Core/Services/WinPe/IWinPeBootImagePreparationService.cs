// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Core.Services.WinPe;

/// <summary>
/// Prepares dependencies from a Windows source image for WinPE and WinRE boot media.
/// </summary>
public interface IWinPeBootImagePreparationService
{
    /// <summary>
    /// Stages dependencies outside the source mount before discarding it, replacing boot.wim only for WinRE Wi-Fi media.
    /// </summary>
    Task<WinPeResult<WinPeBootImagePreparationResult>> PrepareAsync(
        WinPeBootImagePreparationOptions options,
        CancellationToken cancellationToken = default);
}
