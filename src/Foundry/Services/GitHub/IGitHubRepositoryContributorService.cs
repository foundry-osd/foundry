// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Services.GitHub;

/// <summary>
/// Loads repository contributor metadata for the about dialog.
/// </summary>
public interface IGitHubRepositoryContributorService
{
    /// <summary>
    /// Gets non-bot repository contributors sorted by contribution count.
    /// </summary>
    /// <param name="cancellationToken">Token that cancels GitHub API calls.</param>
    /// <returns>Contributor metadata.</returns>
    Task<IReadOnlyList<GitHubRepositoryContributor>> GetContributorsAsync(CancellationToken cancellationToken = default);
}
