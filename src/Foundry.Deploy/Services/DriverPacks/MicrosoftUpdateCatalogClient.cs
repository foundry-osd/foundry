using System.Globalization;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.RegularExpressions;
using Foundry.Deploy.Services.Http;
using HtmlAgilityPack;
using Microsoft.Extensions.Logging;

namespace Foundry.Deploy.Services.DriverPacks;

public sealed class MicrosoftUpdateCatalogClient : IMicrosoftUpdateCatalogClient
{
    private static readonly HttpClient HttpClient = InsecureHttpClientFactory.Create(TimeSpan.FromMinutes(5));
    private static readonly Uri HomeUri = new("https://www.catalog.update.microsoft.com/Home.aspx");
    private static readonly Uri DownloadDialogUri = new("https://www.catalog.update.microsoft.com/DownloadDialog.aspx");
    private static readonly Regex DownloadUrlRegex = new(
        "(http[s]?://dl\\.delivery\\.mp\\.microsoft\\.com/[^\\'\\\"]*)|(http[s]?://download\\.windowsupdate\\.com/[^\\'\\\"]*)|(http[s]?://catalog\\.s\\.download\\.windowsupdate\\.com/[^\\'\\\"]*)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private readonly ILogger<MicrosoftUpdateCatalogClient> _logger;

    public MicrosoftUpdateCatalogClient(ILogger<MicrosoftUpdateCatalogClient> logger)
    {
        _logger = logger;
    }

    public async Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using HttpResponseMessage response = await SendAsync(
                    () => new HttpRequestMessage(HttpMethod.Head, HomeUri),
                    cancellationToken)
                .ConfigureAwait(false);

            if (response.IsSuccessStatusCode)
            {
                return true;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Microsoft Update Catalog HEAD request failed. Falling back to GET.");
        }

        try
        {
            using HttpResponseMessage response = await SendAsync(
                    () => new HttpRequestMessage(HttpMethod.Get, HomeUri),
                    cancellationToken)
                .ConfigureAwait(false);

            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Microsoft Update Catalog GET request failed.");
            return false;
        }
    }

    public async Task<IReadOnlyList<MicrosoftUpdateCatalogUpdate>> SearchAsync(
        string searchQuery,
        bool descending = true,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(searchQuery);

        string encodedQuery = Uri.EscapeDataString(searchQuery);
        string requestUri = $"https://www.catalog.update.microsoft.com/Search.aspx?q={encodedQuery}";
        string html = await SendStringAsync(requestUri, "Microsoft Update Catalog search", cancellationToken).ConfigureAwait(false);

        var document = new HtmlDocument();
        document.LoadHtml(html);

        HtmlNode? errorNode = document.GetElementbyId("errorPageDisplayedError");
        if (errorNode is not null)
        {
            _logger.LogWarning("Microsoft Update Catalog search returned an error page: {ErrorText}", HtmlEntity.DeEntitize(errorNode.InnerText).Trim());
            return [];
        }

        if (document.GetElementbyId("ctl00_catalogBody_noResultText") is not null)
        {
            return [];
        }

        HtmlNode? table = document.GetElementbyId("ctl00_catalogBody_updateMatches");
        if (table is null)
        {
            _logger.LogWarning("Microsoft Update Catalog search did not return the expected results table for query '{SearchQuery}'.", searchQuery);
            return [];
        }

        HtmlNodeCollection? rows = table.SelectNodes(".//tr");
        if (rows is null || rows.Count == 0)
        {
            return [];
        }

        IEnumerable<MicrosoftUpdateCatalogUpdate> parsed = rows
            .Where(row => !string.Equals(row.Id, "headerRow", StringComparison.OrdinalIgnoreCase))
            .Select(ParseUpdate)
            .Where(update => update is not null)
            .Cast<MicrosoftUpdateCatalogUpdate>();

        return descending
            ? parsed.OrderByDescending(update => update.LastUpdated ?? DateTimeOffset.MinValue).ThenBy(update => update.Title, StringComparer.OrdinalIgnoreCase).ToArray()
            : parsed.ToArray();
    }

    public async Task<IReadOnlyList<string>> GetDownloadUrlsAsync(
        string updateId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(updateId);

        string payload = $"[{{\"size\":0,\"updateID\":\"{updateId}\",\"uidInfo\":\"{updateId}\"}}]";
        string html = await SendFormAsync(
                DownloadDialogUri.ToString(),
                [new KeyValuePair<string, string>("updateIDs", payload)],
                "Microsoft Update Catalog download dialog",
                cancellationToken)
            .ConfigureAwait(false);

        string normalizedHtml = html.Replace("www.download.windowsupdate", "download.windowsupdate", StringComparison.OrdinalIgnoreCase);
        MatchCollection matches = DownloadUrlRegex.Matches(normalizedHtml);

        return matches
            .Select(match => match.Value)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private async Task<HttpResponseMessage> SendAsync(
        Func<HttpRequestMessage> requestFactory,
        CancellationToken cancellationToken)
    {
        return await HttpRetryPolicy
            .ExecuteAsync(
                async ct =>
                {
                    using HttpRequestMessage request = requestFactory();
                    ApplyNoCacheHeaders(request);
                    return await HttpClient
                        .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct)
                        .ConfigureAwait(false);
                },
                _logger,
                "Microsoft Update Catalog request",
                cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<string> SendStringAsync(
        string requestUri,
        string operationName,
        CancellationToken cancellationToken)
    {
        return await HttpRetryPolicy
            .ExecuteAsync(
                async ct =>
                {
                    using HttpRequestMessage request = new(HttpMethod.Get, requestUri);
                    ApplyNoCacheHeaders(request);
                    using HttpResponseMessage response = await HttpClient
                        .SendAsync(request, HttpCompletionOption.ResponseContentRead, ct)
                        .ConfigureAwait(false);
                    response.EnsureSuccessStatusCode();
                    return await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                },
                _logger,
                operationName,
                cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<string> SendFormAsync(
        string requestUri,
        IReadOnlyList<KeyValuePair<string, string>> formValues,
        string operationName,
        CancellationToken cancellationToken)
    {
        return await HttpRetryPolicy
            .ExecuteAsync(
                async ct =>
                {
                    using HttpRequestMessage request = new(HttpMethod.Post, requestUri)
                    {
                        Content = new FormUrlEncodedContent(formValues)
                    };
                    ApplyNoCacheHeaders(request);
                    using HttpResponseMessage response = await HttpClient
                        .SendAsync(request, HttpCompletionOption.ResponseContentRead, ct)
                        .ConfigureAwait(false);
                    response.EnsureSuccessStatusCode();
                    return await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                },
                _logger,
                operationName,
                cancellationToken)
            .ConfigureAwait(false);
    }

    private static void ApplyNoCacheHeaders(HttpRequestMessage request)
    {
        request.Headers.CacheControl = new CacheControlHeaderValue
        {
            NoCache = true
        };
        request.Headers.Pragma.Add(new NameValueHeaderValue("no-cache"));
    }

    private static MicrosoftUpdateCatalogUpdate? ParseUpdate(HtmlNode row)
    {
        HtmlNodeCollection? cells = row.SelectNodes("td");
        if (cells is null || cells.Count < 8)
        {
            return null;
        }

        HtmlNodeCollection? inputNodes = cells[7].SelectNodes(".//input");
        string updateId = inputNodes?.FirstOrDefault()?.GetAttributeValue("id", string.Empty) ?? string.Empty;
        if (string.IsNullOrWhiteSpace(updateId))
        {
            return null;
        }

        HtmlNodeCollection? sizeSpans = cells[6].SelectNodes(".//span");
        string size = sizeSpans?.FirstOrDefault() is HtmlNode sizeNode
            ? HtmlEntity.DeEntitize(sizeNode.InnerText).Trim()
            : HtmlEntity.DeEntitize(cells[6].InnerText).Trim();
        long sizeInBytes = sizeSpans is not null && sizeSpans.Count > 1
            ? ParseLong(HtmlEntity.DeEntitize(sizeSpans[1].InnerText).Trim())
            : 0L;

        return new MicrosoftUpdateCatalogUpdate
        {
            UpdateId = updateId,
            Title = HtmlEntity.DeEntitize(cells[1].InnerText).Trim(),
            Products = HtmlEntity.DeEntitize(cells[2].InnerText).Trim(),
            Classification = HtmlEntity.DeEntitize(cells[3].InnerText).Trim(),
            LastUpdated = ParseDate(HtmlEntity.DeEntitize(cells[4].InnerText).Trim()),
            Version = HtmlEntity.DeEntitize(cells[5].InnerText).Trim(),
            Size = size,
            SizeInBytes = sizeInBytes
        };
    }

    private static DateTimeOffset? ParseDate(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out DateTimeOffset invariantParsed))
        {
            return invariantParsed;
        }

        if (DateTimeOffset.TryParse(value, CultureInfo.GetCultureInfo("en-US"), DateTimeStyles.AssumeLocal, out DateTimeOffset enUsParsed))
        {
            return enUsParsed;
        }

        return DateTimeOffset.TryParse(value, out DateTimeOffset parsed)
            ? parsed
            : null;
    }

    private static long ParseLong(string value)
    {
        return long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out long parsed)
            ? parsed
            : 0L;
    }
}
