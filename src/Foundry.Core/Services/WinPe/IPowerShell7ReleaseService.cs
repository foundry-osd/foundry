// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Core.Services.WinPe;

/// <summary>
/// Resolves the most recent stable PowerShell 7 releases available for boot image integration.
/// </summary>
public interface IPowerShell7ReleaseService
{
    /// <summary>
    /// Gets the most recent stable (non-prerelease) PowerShell 7 releases that publish an asset for the
    /// requested architecture, ordered newest first.
    /// </summary>
    /// <param name="architecture">The target WinPE architecture.</param>
    /// <param name="count">The maximum number of releases to return.</param>
    /// <param name="cancellationToken">Token that cancels the lookup.</param>
    Task<WinPeResult<IReadOnlyList<PowerShell7Release>>> GetLatestStableReleasesAsync(
        WinPeArchitecture architecture,
        int count = 3,
        CancellationToken cancellationToken = default);
}
