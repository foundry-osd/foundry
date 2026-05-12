namespace Foundry.Services.GitHub;

/// <summary>
/// Represents a GitHub contributor shown in the Foundry about dialog.
/// </summary>
/// <param name="Login">GitHub login name.</param>
/// <param name="DisplayName">Optional public profile display name.</param>
/// <param name="ProfileUri">Contributor profile URL.</param>
/// <param name="AvatarUri">Contributor avatar URL.</param>
/// <param name="Contributions">Contribution count returned by the repository contributors API.</param>
public sealed record GitHubRepositoryContributor(
    string Login,
    string? DisplayName,
    Uri ProfileUri,
    Uri AvatarUri,
    int Contributions);
