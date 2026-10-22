// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Core.Services.WinPe;

/// <summary>
/// Prepares the boot image of a WinPE workspace from WinRE.
/// </summary>
public interface IWinPeBootImagePreparationService
{
    /// <summary>
    /// Replaces boot.wim with the newest available WinRE and stages its dependencies; x64 keeps the ADK image when no
    /// WinRE can be obtained, and the result then reports <see cref="WinPeBootImageSource.WinPe"/> with the cause.
    /// </summary>
    Task<WinPeResult<WinPeBootImagePreparationResult>> PrepareAsync(
        WinPeBootImagePreparationOptions options,
        CancellationToken cancellationToken = default);
}
