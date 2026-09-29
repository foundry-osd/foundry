// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Foundry.Services.GitHub;

/// <summary>
/// Credits authors and coauthors of commits reachable from the repository's default branch.
/// </summary>
public sealed class GitHubRepositoryContributorService : IGitHubRepositoryContributorService
{
    private static readonly HttpClient HttpClient = CreateHttpClient();
    private static readonly Regex CoAuthorTrailer = new(
        @"^Co-authored-by:[ \t]*(?<name>[^<>\r\n]+?)\s*<(?<email>[^<>\s]+)>[ \t]*$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex GitHubEmail = new(
        @"^(?:\d+\+)?(?<login>[a-z\d-]+(?:\[bot\])?)@users\.noreply\.github\.com$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <inheritdoc />
    public async Task<IReadOnlyList<GitHubRepositoryContributor>> GetContributorsAsync(CancellationToken cancellationToken = default)
    {
        List<JsonElement> commits = await GetCommitsAsync(cancellationToken).ConfigureAwait(false);
        Dictionary<string, GitHubRepositoryContributor> authorsByEmail = new(StringComparer.OrdinalIgnoreCase);
        HashSet<string> botEmails = new(StringComparer.OrdinalIgnoreCase);
        HashSet<string> botLogins = new(StringComparer.OrdinalIgnoreCase);

        // Resolve aliases before counting, including an email first seen on an older page.
        foreach (JsonElement commit in commits)
        {
            JsonElement author = commit.GetProperty("commit").GetProperty("author");
            if (!TryGetString(author, "email", out string email)
                || !commit.TryGetProperty("author", out JsonElement profile)
                || !TryGetString(profile, "login", out string login))
            {
                continue;
            }

            if (IsBotContributor(login)
                || (TryGetString(profile, "type", out string type) && type != "User"))
            {
                botEmails.Add(email);
                botLogins.Add(login);
                continue;
            }

            authorsByEmail[email] = CreateContributor(login, null);
        }

        Dictionary<string, GitHubRepositoryContributor> contributors = new(StringComparer.OrdinalIgnoreCase);
        HashSet<string> seenCommits = new(StringComparer.OrdinalIgnoreCase);
        foreach (JsonElement commit in commits)
        {
            if (!seenCommits.Add(commit.GetProperty("sha").GetString()!))
            {
                continue;
            }

            HashSet<string> credited = new(StringComparer.OrdinalIgnoreCase);
            JsonElement details = commit.GetProperty("commit");
            JsonElement author = details.GetProperty("author");
            if (TryGetString(author, "name", out string name) && TryGetString(author, "email", out string email))
            {
                AddContributor(name, email);
            }

            if (TryGetString(details, "message", out string message))
            {
                // Git trailers belong to the final paragraph, not quoted text in the body.
                string normalized = message.Replace("\r\n", "\n").TrimEnd();
                int trailerStart = normalized.LastIndexOf("\n\n", StringComparison.Ordinal);
                if (trailerStart >= 0)
                {
                    foreach (string line in normalized[(trailerStart + 2)..].Split('\n'))
                    {
                        Match match = CoAuthorTrailer.Match(line);
                        if (match.Success)
                        {
                            AddContributor(match.Groups["name"].Value.Trim(), match.Groups["email"].Value);
                        }
                    }
                }
            }

            void AddContributor(string name, string email)
            {
                if (botEmails.Contains(email) || IsBotContributor(name))
                {
                    return;
                }

                if (!authorsByEmail.TryGetValue(email, out GitHubRepositoryContributor? contributor))
                {
                    Match match = GitHubEmail.Match(email);
                    string login = match.Success ? match.Groups["login"].Value : string.Empty;
                    contributor = CreateContributor(login, name);
                }

                string key = string.IsNullOrEmpty(contributor.Login) ? $"email:{email}" : $"login:{contributor.Login}";
                if (IsBotContributor(contributor.Login) || botLogins.Contains(contributor.Login) || !credited.Add(key))
                {
                    return;
                }

                int count = contributors.TryGetValue(key, out GitHubRepositoryContributor? existing) ? existing.Contributions : 0;
                contributors[key] = contributor with { Contributions = count + 1 };
            }
        }

        List<GitHubRepositoryContributor> result = [];
        foreach (GitHubRepositoryContributor contributor in contributors.Values)
        {
            GitHubRepositoryContributor? enriched = await EnrichContributorAsync(contributor, cancellationToken).ConfigureAwait(false);
            if (enriched is not null)
            {
                result.Add(enriched);
            }
        }

        return result
            .OrderByDescending(contributor => contributor.Contributions)
            .ThenBy(contributor => contributor.Login.Length > 0 ? contributor.Login : contributor.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static async Task<List<JsonElement>> GetCommitsAsync(CancellationToken cancellationToken)
    {
        List<JsonElement> commits = [];
        string? head = null;
        for (int page = 1; ; page++)
        {
            string url = $"{FoundryApplicationInfo.CommitsApiUrl}?per_page=100&page={page}";
            if (head is not null)
            {
                // Pin subsequent pages so a new push cannot shift the pagination window.
                url += $"&sha={Uri.EscapeDataString(head)}";
            }

            using HttpResponseMessage response = await HttpClient.GetAsync(url, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            using JsonDocument document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
            foreach (JsonElement commit in document.RootElement.EnumerateArray())
            {
                head ??= commit.GetProperty("sha").GetString();
                commits.Add(commit.Clone());
            }

            if (!response.Headers.TryGetValues("Link", out IEnumerable<string>? links)
                || !links.Any(link => link.Contains("rel=\"next\"", StringComparison.Ordinal)))
            {
                return commits;
            }
        }
    }

    private static GitHubRepositoryContributor CreateContributor(string login, string? name)
    {
        return new GitHubRepositoryContributor(
            login,
            name,
            login.Length > 0 ? new Uri($"https://github.com/{login}") : null,
            login.Length > 0 ? new Uri($"https://github.com/{login}.png") : null,
            0);
    }

    private static async Task<GitHubRepositoryContributor?> EnrichContributorAsync(
        GitHubRepositoryContributor contributor,
        CancellationToken cancellationToken)
    {
        if (contributor.Login.Length == 0)
        {
            return contributor;
        }

        try
        {
            using HttpResponseMessage response = await HttpClient.GetAsync(
                $"https://api.github.com/users/{Uri.EscapeDataString(contributor.Login)}", cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return contributor;
            }

            await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            using JsonDocument document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
            JsonElement profile = document.RootElement;
            if (TryGetString(profile, "type", out string type) && type != "User")
            {
                return null;
            }

            return contributor with
            {
                DisplayName = TryGetString(profile, "name", out string name) ? name : contributor.DisplayName,
                AvatarUri = TryGetString(profile, "avatar_url", out string avatarUrl) && Uri.TryCreate(avatarUrl, UriKind.Absolute, out Uri? avatarUri)
                    ? avatarUri : contributor.AvatarUri
            };
        }
        catch (HttpRequestException)
        {
            return contributor;
        }
        catch (JsonException)
        {
            return contributor;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return contributor;
        }
    }

    private static bool TryGetString(JsonElement element, string propertyName, out string value)
    {
        value = string.Empty;
        if (element.ValueKind != JsonValueKind.Object
            || !element.TryGetProperty(propertyName, out JsonElement property)
            || property.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        value = property.GetString() ?? string.Empty;
        return !string.IsNullOrWhiteSpace(value);
    }

    private static bool IsBotContributor(string login)
    {
        return login.EndsWith("[bot]", StringComparison.OrdinalIgnoreCase)
            || login.Equals("dependabot", StringComparison.OrdinalIgnoreCase)
            || login.Equals("dependabot-preview", StringComparison.OrdinalIgnoreCase);
    }

    private static HttpClient CreateHttpClient()
    {
        HttpClient httpClient = new()
        {
            Timeout = TimeSpan.FromSeconds(15)
        };

        httpClient.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("Foundry", "1.0"));
        httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        return httpClient;
    }
}
