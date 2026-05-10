using System.Windows;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Diagnostics;
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
        _logger.LogInformation("Manual update check requested.");
        return CheckForUpdatesCoreAsync(UpdateCheckTrigger.Manual, notifyWhenCurrent: true, notifyWhenFailure: true, cancellationToken);
    }

    public async Task CheckForUpdatesOnStartupAsync(CancellationToken cancellationToken = default)
    {
        if (Interlocked.Exchange(ref _startupCheckCompleted, 1) != 0)
        {
            _logger.LogDebug("Skipping automatic startup update check because it has already been completed for this application session.");
            return;
        }

        if (IsVisualStudioDebugSession())
        {
            _logger.LogInformation(
                "Skipping automatic startup update check because Foundry is running in a Visual Studio debug session.");
            return;
        }

        _logger.LogDebug("Automatic startup update check requested.");

        try
        {
            await CheckForUpdatesCoreAsync(UpdateCheckTrigger.Startup, notifyWhenCurrent: false, notifyWhenFailure: false, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _logger.LogDebug("Automatic startup update check was canceled.");
        }
    }

    private async Task CheckForUpdatesCoreAsync(
        UpdateCheckTrigger trigger,
        bool notifyWhenCurrent,
        bool notifyWhenFailure,
        CancellationToken cancellationToken)
    {
        await _checkGate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            UpdateCheckResult result = await GetUpdateCheckResultAsync(trigger, cancellationToken).ConfigureAwait(false);

            switch (result.Status)
            {
                case UpdateCheckStatus.UpdateAvailable:
                    _logger.LogInformation(
                        "Update check completed with an available release. Trigger={Trigger}, CurrentVersion={CurrentVersion}, LatestVersion={LatestVersion}, ReleaseUrl={ReleaseUrl}",
                        trigger,
                        result.UpdateInfo!.CurrentVersion,
                        result.UpdateInfo.LatestVersion,
                        result.UpdateInfo.ReleaseUrl);
                    _applicationShellService.ShowUpdateAvailable(result.UpdateInfo!);
                    break;

                case UpdateCheckStatus.UpToDate:
                    _logger.LogInformation(
                        "Update check completed with no available update. Trigger={Trigger}, CurrentVersion={CurrentVersion}, LatestVersion={LatestVersion}",
                        trigger,
                        NormalizeVersionDisplay(FoundryApplicationInfo.Version),
                        result.LatestVersionDisplay ?? "unknown");
                    if (notifyWhenCurrent)
                    {
                        _applicationShellService.ShowMessage(
                            _localizationService.Strings["UpdateCheck.UpToDateTitle"],
                            _localizationService.Strings["UpdateCheck.UpToDateMessage"],
                            MessageBoxImage.Information);
                    }
                    break;

                case UpdateCheckStatus.Failed:
                    _logger.LogWarning("Update check failed. Trigger={Trigger}", trigger);
                    if (notifyWhenFailure)
                    {
                        _applicationShellService.ShowMessage(
                            _localizationService.Strings["UpdateCheck.FailedTitle"],
                            _localizationService.Strings["UpdateCheck.FailedMessage"],
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

    private async Task<UpdateCheckResult> GetUpdateCheckResultAsync(
        UpdateCheckTrigger trigger,
        CancellationToken cancellationToken)
    {
        string currentVersionDisplay = FoundryApplicationInfo.Version;
        Version? currentVersion = TryParseVersion(currentVersionDisplay);
        if (currentVersion is null)
        {
            _logger.LogWarning(
                "Unable to parse the current Foundry version for update checks. Trigger={Trigger}, CurrentVersion={CurrentVersion}",
                trigger,
                currentVersionDisplay);
            return UpdateCheckResult.Failed();
        }

        try
        {
            GitHubReleaseInfo latestRelease = await GetLatestReleaseAsync(cancellationToken).ConfigureAwait(false);
            Version? latestVersion = TryParseVersion(latestRelease.TagName);
            if (latestVersion is null)
            {
                _logger.LogWarning(
                    "Unable to parse the latest GitHub release tag for update checks. Trigger={Trigger}, TagName={TagName}",
                    trigger,
                    latestRelease.TagName);
                return UpdateCheckResult.Failed();
            }

            if (latestVersion <= currentVersion)
            {
                return UpdateCheckResult.UpToDate(NormalizeVersionDisplay(latestRelease.TagName));
            }

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
            _logger.LogWarning(ex, "Foundry failed to query the latest GitHub release. Trigger={Trigger}", trigger);
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

    private static bool IsVisualStudioDebugSession()
    {
#if DEBUG
        return Debugger.IsAttached;
#else
        return false;
#endif
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

    private enum UpdateCheckTrigger
    {
        Startup,
        Manual
    }

    private sealed record UpdateCheckResult(
        UpdateCheckStatus Status,
        ApplicationUpdateInfo? UpdateInfo = null,
        string? LatestVersionDisplay = null)
    {
        public static UpdateCheckResult UpdateAvailable(ApplicationUpdateInfo updateInfo)
        {
            return new(UpdateCheckStatus.UpdateAvailable, updateInfo);
        }

        public static UpdateCheckResult UpToDate(string latestVersionDisplay)
        {
            return new(UpdateCheckStatus.UpToDate, LatestVersionDisplay: latestVersionDisplay);
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
