using System.Windows;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using Foundry.Services.ApplicationShell;
using Foundry.Services.Localization;
using Microsoft.Extensions.Logging;

namespace Foundry.Services.ApplicationUpdate;

public sealed class ApplicationUpdateService : IApplicationUpdateService
{
    private const string GitHubApiVersion = "2022-11-28";
    private static readonly HttpClient HttpClient = CreateHttpClient();
    private static readonly Regex VersionPattern = new(@"(?<version>\d+(?:\.\d+){1,3})", RegexOptions.Compiled);

    private readonly IApplicationShellService _applicationShellService;
    private readonly ILocalizationService _localizationService;
    private readonly ILogger<ApplicationUpdateService> _logger;
    private readonly SemaphoreSlim _checkGate = new(1, 1);

    private int _startupCheckCompleted;

    public ApplicationUpdateService(
        IApplicationShellService applicationShellService,
        ILocalizationService localizationService,
        ILogger<ApplicationUpdateService> logger)
    {
        _applicationShellService = applicationShellService;
        _localizationService = localizationService;
        _logger = logger;
    }

    public Task CheckForUpdatesAsync(CancellationToken cancellationToken = default)
    {
        return CheckForUpdatesCoreAsync(notifyWhenCurrent: true, notifyWhenFailure: true, cancellationToken);
    }

    public async Task CheckForUpdatesOnStartupAsync(CancellationToken cancellationToken = default)
    {
        if (Interlocked.Exchange(ref _startupCheckCompleted, 1) != 0)
        {
            return;
        }

        if (IsPlaceholderVersion(FoundryApplicationInfo.Version))
        {
            _logger.LogInformation(
                "Skipping automatic startup update check because the current version is the placeholder development version. CurrentVersion={CurrentVersion}",
                FoundryApplicationInfo.Version);
            return;
        }

        try
        {
            await CheckForUpdatesCoreAsync(notifyWhenCurrent: false, notifyWhenFailure: false, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private async Task CheckForUpdatesCoreAsync(
        bool notifyWhenCurrent,
        bool notifyWhenFailure,
        CancellationToken cancellationToken)
    {
        await _checkGate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            UpdateCheckResult result = await GetUpdateCheckResultAsync(cancellationToken).ConfigureAwait(false);

            switch (result.Status)
            {
                case UpdateCheckStatus.UpdateAvailable:
                    _applicationShellService.ShowUpdateAvailable(result.UpdateInfo!);
                    break;

                case UpdateCheckStatus.UpToDate:
                    if (notifyWhenCurrent)
                    {
                        _applicationShellService.ShowMessage(
                            _localizationService.Strings["UpdateCheckUpToDateTitle"],
                            _localizationService.Strings["UpdateCheckUpToDateMessage"],
                            MessageBoxImage.Information);
                    }
                    break;

                case UpdateCheckStatus.Failed:
                    if (notifyWhenFailure)
                    {
                        _applicationShellService.ShowMessage(
                            _localizationService.Strings["UpdateCheckFailedTitle"],
                            _localizationService.Strings["UpdateCheckFailedMessage"],
                            MessageBoxImage.Error);
                    }
                    break;
            }
        }
        finally
        {
            _checkGate.Release();
        }
    }

    private async Task<UpdateCheckResult> GetUpdateCheckResultAsync(CancellationToken cancellationToken)
    {
        string currentVersionDisplay = FoundryApplicationInfo.Version;
        Version? currentVersion = TryParseVersion(currentVersionDisplay);
        if (currentVersion is null)
        {
            _logger.LogWarning("Unable to parse the current Foundry version for update checks. CurrentVersion={CurrentVersion}", currentVersionDisplay);
            return UpdateCheckResult.Failed();
        }

        try
        {
            GitHubReleaseInfo latestRelease = await GetLatestReleaseAsync(cancellationToken).ConfigureAwait(false);
            Version? latestVersion = TryParseVersion(latestRelease.TagName);
            if (latestVersion is null)
            {
                _logger.LogWarning("Unable to parse the latest GitHub release tag for update checks. TagName={TagName}", latestRelease.TagName);
                return UpdateCheckResult.Failed();
            }

            if (latestVersion <= currentVersion)
            {
                _logger.LogInformation(
                    "Foundry is up to date. CurrentVersion={CurrentVersion}, LatestVersion={LatestVersion}",
                    currentVersion,
                    latestVersion);
                return UpdateCheckResult.UpToDate();
            }

            _logger.LogInformation(
                "Foundry update available. CurrentVersion={CurrentVersion}, LatestVersion={LatestVersion}, ReleaseUrl={ReleaseUrl}",
                currentVersion,
                latestVersion,
                latestRelease.ReleaseUrl);

            return UpdateCheckResult.UpdateAvailable(new ApplicationUpdateInfo(
                CurrentVersion: NormalizeVersionDisplay(currentVersionDisplay),
                LatestVersion: NormalizeVersionDisplay(latestRelease.TagName),
                ReleaseTitle: NormalizeReleaseTitle(latestRelease.ReleaseTitle),
                ReleaseUrl: latestRelease.ReleaseUrl,
                PublishedAt: latestRelease.PublishedAt,
                ReleaseNotes: latestRelease.ReleaseNotes));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Foundry failed to query the latest GitHub release.");
            return UpdateCheckResult.Failed();
        }
    }

    private static async Task<GitHubReleaseInfo> GetLatestReleaseAsync(CancellationToken cancellationToken)
    {
        using HttpRequestMessage request = new(HttpMethod.Get, FoundryApplicationInfo.LatestReleaseApiUrl);
        request.Headers.Accept.ParseAdd("application/vnd.github+json");
        request.Headers.Add("X-GitHub-Api-Version", GitHubApiVersion);

        using HttpResponseMessage response = await HttpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        await using Stream contentStream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using JsonDocument document = await JsonDocument.ParseAsync(contentStream, cancellationToken: cancellationToken).ConfigureAwait(false);

        JsonElement root = document.RootElement;
        string tagName = ReadString(root, "tag_name");
        string releaseTitle = FirstNonEmpty(ReadString(root, "name"), tagName, "Latest release");
        string releaseUrl = FirstNonEmpty(ReadString(root, "html_url"), FoundryApplicationInfo.LatestReleaseUrl);
        string releaseNotes = NormalizeReleaseNotes(ReadString(root, "body"));

        return new GitHubReleaseInfo(
            tagName,
            releaseTitle,
            releaseUrl,
            ReadDateTimeOffset(root, "published_at"),
            releaseNotes);
    }

    private static string ReadString(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out JsonElement property) &&
               property.ValueKind == JsonValueKind.String
            ? property.GetString()?.Trim() ?? string.Empty
            : string.Empty;
    }

    private static DateTimeOffset? ReadDateTimeOffset(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out JsonElement property) ||
            property.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        return DateTimeOffset.TryParse(property.GetString(), out DateTimeOffset parsed)
            ? parsed
            : null;
    }

    private static string NormalizeReleaseNotes(string notes)
    {
        if (string.IsNullOrWhiteSpace(notes))
        {
            return string.Empty;
        }

        return notes
            .Replace("\r\n", Environment.NewLine, StringComparison.Ordinal)
            .Replace("\n", Environment.NewLine, StringComparison.Ordinal)
            .Trim();
    }

    private static Version? TryParseVersion(string? rawVersion)
    {
        if (string.IsNullOrWhiteSpace(rawVersion))
        {
            return null;
        }

        Match match = VersionPattern.Match(rawVersion.Trim());
        if (!match.Success)
        {
            return null;
        }

        return Version.TryParse(match.Groups["version"].Value, out Version? parsedVersion)
            ? parsedVersion
            : null;
    }

    private static bool IsPlaceholderVersion(string? rawVersion)
    {
        Version? version = TryParseVersion(rawVersion);
        return version is not null && version == new Version(1, 0, 0, 0);
    }

    private static string NormalizeVersionDisplay(string? rawVersion)
    {
        Version? version = TryParseVersion(rawVersion);
        return version?.ToString() ?? rawVersion?.Trim() ?? string.Empty;
    }

    private static string NormalizeReleaseTitle(string? releaseTitle)
    {
        if (string.IsNullOrWhiteSpace(releaseTitle))
        {
            return string.Empty;
        }

        return VersionPattern.Replace(releaseTitle.Trim(), match => NormalizeVersionDisplay(match.Value));
    }

    private static string FirstNonEmpty(params string[] values)
    {
        return values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty;
    }

    private static HttpClient CreateHttpClient()
    {
        HttpClient client = new()
        {
            Timeout = TimeSpan.FromSeconds(15)
        };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Foundry");
        return client;
    }

    private enum UpdateCheckStatus
    {
        UpdateAvailable,
        UpToDate,
        Failed
    }

    private sealed record UpdateCheckResult(UpdateCheckStatus Status, ApplicationUpdateInfo? UpdateInfo = null)
    {
        public static UpdateCheckResult UpdateAvailable(ApplicationUpdateInfo updateInfo)
        {
            return new(UpdateCheckStatus.UpdateAvailable, updateInfo);
        }

        public static UpdateCheckResult UpToDate()
        {
            return new(UpdateCheckStatus.UpToDate);
        }

        public static UpdateCheckResult Failed()
        {
            return new(UpdateCheckStatus.Failed);
        }
    }

    private sealed record GitHubReleaseInfo(
        string TagName,
        string ReleaseTitle,
        string ReleaseUrl,
        DateTimeOffset? PublishedAt,
        string ReleaseNotes);
}
