// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Services.GitHub;

/// <summary>
/// Represents a GitHub contributor shown in the Foundry about dialog.
/// </summary>
/// <param name="Login">GitHub login name, or empty when the commit identity cannot be linked to a profile.</param>
/// <param name="DisplayName">Public profile name or the name credited in the commit.</param>
/// <param name="ProfileUri">Contributor profile URL, when known.</param>
/// <param name="AvatarUri">Contributor avatar URL, when known.</param>
/// <param name="Contributions">Number of distinct commits authored or coauthored by this contributor.</param>
public sealed record GitHubRepositoryContributor(
    string Login,
    string? DisplayName,
    Uri? ProfileUri,
    Uri? AvatarUri,
    int Contributions);
