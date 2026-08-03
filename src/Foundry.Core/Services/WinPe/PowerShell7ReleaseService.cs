// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Net.Http.Headers;
using System.Text.Json;

namespace Foundry.Core.Services.WinPe;

/// <inheritdoc />
public sealed class PowerShell7ReleaseService : IPowerShell7ReleaseService
{
    private const string ReleasesUri = "https://api.github.com/repos/PowerShell/PowerShell/releases?per_page=30";
    private readonly HttpClient _httpClient;

    /// <summary>
    /// Initializes the service with a default HTTP client configured for the GitHub API.
    /// </summary>
    public PowerShell7ReleaseService()
        : this(CreateDefaultHttpClient())
    {
    }

    internal PowerShell7ReleaseService(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    /// <inheritdoc />
    public async Task<WinPeResult<IReadOnlyList<PowerShell7Release>>> GetLatestStableReleasesAsync(
        WinPeArchitecture architecture,
        int count = 3,
        CancellationToken cancellationToken = default)
    {
        if (!Enum.IsDefined(architecture))
        {
            return WinPeResult<IReadOnlyList<PowerShell7Release>>.Failure(
                WinPeErrorCodes.ValidationFailed,
                "WinPE architecture value is invalid.",
                $"Value: '{architecture}'.");
        }

        if (count <= 0)
        {
            return WinPeResult<IReadOnlyList<PowerShell7Release>>.Success([]);
        }

        string assetSuffix = $"-{architecture.ToDotnetRuntimeIdentifier()}.zip";

        try
        {
            using HttpResponseMessage response = await _httpClient
                .GetAsync(ReleasesUri, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            await using Stream contentStream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            using JsonDocument document = await JsonDocument.ParseAsync(contentStream, cancellationToken: cancellationToken).ConfigureAwait(false);

            List<PowerShell7Release> releases = ParseReleases(document.RootElement, assetSuffix, count);
            return WinPeResult<IReadOnlyList<PowerShell7Release>>.Success(releases);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or IOException or TaskCanceledException)
        {
            return WinPeResult<IReadOnlyList<PowerShell7Release>>.Failure(
                WinPeErrorCodes.DownloadFailed,
                "Failed to query PowerShell 7 releases from GitHub.",
                ex.Message);
        }
    }

    private static List<PowerShell7Release> ParseReleases(JsonElement root, string assetSuffix, int count)
    {
        List<PowerShell7Release> releases = [];
        if (root.ValueKind != JsonValueKind.Array)
        {
            return releases;
        }

        foreach (JsonElement release in root.EnumerateArray())
        {
            if (releases.Count >= count)
            {
                break;
            }

            if (GetBool(release, "prerelease") || GetBool(release, "draft"))
            {
                continue;
            }

            string tag = GetString(release, "tag_name");
            if (string.IsNullOrWhiteSpace(tag))
            {
                continue;
            }

            if (!TryResolveArchitectureAsset(release, assetSuffix, out string assetName, out string downloadUrl))
            {
                continue;
            }

            releases.Add(new PowerShell7Release
            {
                Version = tag.TrimStart('v', 'V'),
                Tag = tag,
                AssetName = assetName,
                DownloadUrl = downloadUrl
            });
        }

        return releases;
    }

    private static bool TryResolveArchitectureAsset(
        JsonElement release,
        string assetSuffix,
        out string assetName,
        out string downloadUrl)
    {
        assetName = string.Empty;
        downloadUrl = string.Empty;

        if (!release.TryGetProperty("assets", out JsonElement assets) || assets.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        foreach (JsonElement asset in assets.EnumerateArray())
        {
            string name = GetString(asset, "name");

            // Match e.g. "PowerShell-7.4.17-win-x64.zip" and skip checksum/hash sidecar files.
            if (name.StartsWith("PowerShell-", StringComparison.OrdinalIgnoreCase) &&
                name.EndsWith(assetSuffix, StringComparison.OrdinalIgnoreCase))
            {
                assetName = name;
                downloadUrl = GetString(asset, "browser_download_url");
                return !string.IsNullOrWhiteSpace(downloadUrl);
            }
        }

        return false;
    }

    private static bool GetBool(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out JsonElement value) &&
               value.ValueKind == JsonValueKind.True;
    }

    private static string GetString(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;
    }

    private static HttpClient CreateDefaultHttpClient()
    {
        var client = new HttpClient();
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("Foundry", "1.0"));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        return client;
    }
}
