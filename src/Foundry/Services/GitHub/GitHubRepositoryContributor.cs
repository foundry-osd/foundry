namespace Foundry.Services.GitHub;

public sealed record GitHubRepositoryContributor(
    string Login,
    string? DisplayName,
    Uri ProfileUri,
    Uri AvatarUri,
    int Contributions);
