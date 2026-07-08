// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Core.Services.WinPe;

/// <summary>
/// Searches the PowerShell Gallery for modules to integrate into a boot image.
/// </summary>
public interface IPowerShellGalleryModuleSearchService
{
    /// <summary>
    /// Searches the PowerShell Gallery for modules matching the supplied term, ordered by relevance.
    /// </summary>
    /// <param name="searchTerm">The module name or keyword to search for.</param>
    /// <param name="count">The maximum number of results to return.</param>
    /// <param name="cancellationToken">Token that cancels the search.</param>
    Task<WinPeResult<IReadOnlyList<PowerShellGalleryModule>>> SearchAsync(
        string searchTerm,
        int count = 20,
        CancellationToken cancellationToken = default);
}
