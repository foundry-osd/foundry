// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Core.Services.WinPe;

public interface IWinReBootImagePreparationService
{
    Task<WinPeResult<WinReBootImagePreparationResult>> ReplaceBootWimAsync(
        WinReBootImagePreparationOptions options,
        CancellationToken cancellationToken = default);
}
