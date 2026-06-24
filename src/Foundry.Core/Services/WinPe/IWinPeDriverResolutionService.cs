// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Core.Services.WinPe;

/// <summary>
/// Resolves selected driver vendors and custom drivers into injectable package directories.
/// </summary>
public interface IWinPeDriverResolutionService
{
    /// <summary>
    /// Resolves driver package directories for a WinPE customization run.
    /// </summary>
    /// <param name="request">The driver resolution request.</param>
    /// <param name="cancellationToken">A token used to cancel catalog, download, or extraction work.</param>
    /// <returns>Injectable driver directory paths or a diagnostic failure.</returns>
    Task<WinPeResult<IReadOnlyList<string>>> ResolveAsync(
        WinPeDriverResolutionRequest request,
        CancellationToken cancellationToken = default);
}
