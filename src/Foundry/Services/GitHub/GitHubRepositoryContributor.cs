namespace Foundry.Services.GitHub;

public sealed record GitHubRepositoryContributor(
    string Login,
    Uri ProfileUri,
    Uri AvatarUri,
    int Contributions);
