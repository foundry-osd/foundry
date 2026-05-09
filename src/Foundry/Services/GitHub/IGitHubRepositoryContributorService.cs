namespace Foundry.Services.GitHub;

public interface IGitHubRepositoryContributorService
{
    Task<IReadOnlyList<GitHubRepositoryContributor>> GetContributorsAsync(CancellationToken cancellationToken = default);
}
