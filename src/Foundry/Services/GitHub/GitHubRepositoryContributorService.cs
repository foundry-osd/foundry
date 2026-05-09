using System.Net.Http.Headers;
using System.Text.Json;

namespace Foundry.Services.GitHub;

public sealed class GitHubRepositoryContributorService : IGitHubRepositoryContributorService
{
    private static readonly HttpClient HttpClient = CreateHttpClient();

    public async Task<IReadOnlyList<GitHubRepositoryContributor>> GetContributorsAsync(CancellationToken cancellationToken = default)
    {
        using HttpRequestMessage request = new(HttpMethod.Get, FoundryApplicationInfo.ContributorsApiUrl);
        using HttpResponseMessage response = await HttpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using JsonDocument document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);

        if (document.RootElement.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        List<GitHubRepositoryContributor> contributors = [];
        foreach (JsonElement contributorElement in document.RootElement.EnumerateArray())
        {
            if (!TryReadContributor(contributorElement, out GitHubRepositoryContributor? contributor)
                || contributor is null)
            {
                continue;
            }

            if (IsBotContributor(contributor.Login))
            {
                continue;
            }

            contributors.Add(await EnrichContributorAsync(contributor, cancellationToken).ConfigureAwait(false));
        }

        return contributors
            .OrderByDescending(contributor => contributor.Contributions)
            .ThenBy(contributor => contributor.Login, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static bool TryReadContributor(JsonElement element, out GitHubRepositoryContributor? contributor)
    {
        contributor = null;

        if (!TryGetString(element, "login", out string login)
            || !TryGetString(element, "html_url", out string profileUrl)
            || !TryGetString(element, "avatar_url", out string avatarUrl))
        {
            return false;
        }

        if (TryGetString(element, "type", out string contributorType)
            && !contributorType.Equals("User", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        int contributions = element.TryGetProperty("contributions", out JsonElement contributionsElement)
            && contributionsElement.TryGetInt32(out int value)
                ? value
                : 0;

        if (!Uri.TryCreate(profileUrl, UriKind.Absolute, out Uri? profileUri)
            || !Uri.TryCreate(avatarUrl, UriKind.Absolute, out Uri? avatarUri))
        {
            return false;
        }

        contributor = new GitHubRepositoryContributor(login, null, profileUri, avatarUri, contributions);
        return true;
    }

    private static async Task<GitHubRepositoryContributor> EnrichContributorAsync(
        GitHubRepositoryContributor contributor,
        CancellationToken cancellationToken)
    {
        string? displayName = await TryGetDisplayNameAsync(contributor.Login, cancellationToken).ConfigureAwait(false);
        return contributor with { DisplayName = displayName };
    }

    private static async Task<string?> TryGetDisplayNameAsync(string login, CancellationToken cancellationToken)
    {
        try
        {
            using HttpRequestMessage request = new(HttpMethod.Get, $"https://api.github.com/users/{Uri.EscapeDataString(login)}");
            using HttpResponseMessage response = await HttpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            using JsonDocument document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);

            return TryGetString(document.RootElement, "name", out string name) ? name : null;
        }
        catch (HttpRequestException)
        {
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static bool TryGetString(JsonElement element, string propertyName, out string value)
    {
        value = string.Empty;
        if (!element.TryGetProperty(propertyName, out JsonElement property)
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
