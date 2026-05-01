using System.Diagnostics;
using Foundry.Core.Services.Application;
using Foundry.Services.Settings;
using Serilog;
using Velopack;
using Velopack.Sources;

namespace Foundry.Services.Updates;

internal sealed class ApplicationUpdateService(
    IAppSettingsService appSettingsService,
    IApplicationLifetimeService applicationLifetimeService,
    ILogger logger) : IApplicationUpdateService
{
    private Velopack.UpdateInfo? pendingUpdate;
    private UpdateManager? pendingUpdateManager;

    public Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        logger.Information(
            "Update service initialized. CheckOnStartup={CheckOnStartup}, Channel={Channel}, FeedUrl={FeedUrl}",
            appSettingsService.Current.Updates.CheckOnStartup,
            appSettingsService.Current.Updates.Channel,
            appSettingsService.Current.Updates.FeedUrl);

        if (appSettingsService.Current.Updates.CheckOnStartup)
        {
            _ = Task.Run(() => RunStartupCheckAsync(CancellationToken.None), CancellationToken.None);
        }

        return Task.CompletedTask;
    }

    public async Task<ApplicationUpdateCheckResult> CheckForUpdatesAsync(
        bool isStartupCheck = false,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (Debugger.IsAttached)
        {
            const string message = "Update check skipped because a debugger is attached.";
            logger.Information(message);
            return new ApplicationUpdateCheckResult(ApplicationUpdateStatus.SkippedInDebug, message);
        }

        Stopwatch totalStopwatch = Stopwatch.StartNew();

        try
        {
            string feedUrl = appSettingsService.Current.Updates.FeedUrl;
            string sourceKind = ResolveUpdateSourceKind(feedUrl);

            Stopwatch sourceStopwatch = Stopwatch.StartNew();
            UpdateManager updateManager = CreateUpdateManager();
            sourceStopwatch.Stop();

            logger.Information(
                "Foundry update source created. IsStartupCheck={IsStartupCheck}, SourceKind={SourceKind}, FeedUrl={FeedUrl}, ConfiguredChannel={ConfiguredChannel}, ElapsedMilliseconds={ElapsedMilliseconds}",
                isStartupCheck,
                sourceKind,
                feedUrl,
                appSettingsService.Current.Updates.Channel,
                sourceStopwatch.ElapsedMilliseconds);

            if (!updateManager.IsInstalled)
            {
                totalStopwatch.Stop();
                const string message = "Update check skipped because Foundry is not running from a Velopack installation.";
                logger.Information(
                    "{Message} IsStartupCheck={IsStartupCheck}, ElapsedMilliseconds={ElapsedMilliseconds}",
                    message,
                    isStartupCheck,
                    totalStopwatch.ElapsedMilliseconds);
                return new ApplicationUpdateCheckResult(ApplicationUpdateStatus.NotInstalled, message);
            }

            logger.Information(
                "Checking for Foundry updates. IsStartupCheck={IsStartupCheck}, SourceKind={SourceKind}, FeedUrl={FeedUrl}",
                isStartupCheck,
                sourceKind,
                feedUrl);

            Stopwatch checkStopwatch = Stopwatch.StartNew();
            Velopack.UpdateInfo? updateInfo = await updateManager.CheckForUpdatesAsync();
            checkStopwatch.Stop();
            totalStopwatch.Stop();

            logger.Information(
                "Foundry update check request completed. IsStartupCheck={IsStartupCheck}, ElapsedMilliseconds={ElapsedMilliseconds}",
                isStartupCheck,
                checkStopwatch.ElapsedMilliseconds);

            if (updateInfo is null)
            {
                const string message = "Foundry is up to date.";
                logger.Information(
                    "{Message} IsStartupCheck={IsStartupCheck}, ElapsedMilliseconds={ElapsedMilliseconds}",
                    message,
                    isStartupCheck,
                    totalStopwatch.ElapsedMilliseconds);
                ClearPendingUpdate();
                return new ApplicationUpdateCheckResult(ApplicationUpdateStatus.NoUpdate, message);
            }

            pendingUpdate = updateInfo;
            pendingUpdateManager = updateManager;

            VelopackAsset targetRelease = updateInfo.TargetFullRelease;
            string version = targetRelease.Version?.ToString() ?? "unknown";
            string releaseNotes = ResolveReleaseNotes(targetRelease);
            string updateMessage = $"Foundry {version} is available.";

            logger.Information(
                "Foundry update available. Version={Version}, FileName={FileName}, Size={Size}, IsStartupCheck={IsStartupCheck}, ElapsedMilliseconds={ElapsedMilliseconds}",
                version,
                targetRelease.FileName,
                targetRelease.Size,
                isStartupCheck,
                totalStopwatch.ElapsedMilliseconds);

            return new ApplicationUpdateCheckResult(
                ApplicationUpdateStatus.UpdateAvailable,
                updateMessage,
                version,
                releaseNotes);
        }
        catch (Exception ex)
        {
            totalStopwatch.Stop();
            logger.Error(
                ex,
                "Foundry update check failed. IsStartupCheck={IsStartupCheck}, ElapsedMilliseconds={ElapsedMilliseconds}",
                isStartupCheck,
                totalStopwatch.ElapsedMilliseconds);
            return new ApplicationUpdateCheckResult(ApplicationUpdateStatus.Failed, ex.Message);
        }
    }

    public async Task<ApplicationUpdateDownloadResult> DownloadUpdateAsync(
        IProgress<int>? progress = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (pendingUpdate is null || pendingUpdateManager is null)
        {
            const string message = "No update is ready to download. Check for updates first.";
            logger.Warning(message);
            return new ApplicationUpdateDownloadResult(ApplicationUpdateStatus.NoUpdate, message);
        }

        try
        {
            logger.Information(
                "Downloading Foundry update. Version={Version}",
                pendingUpdate.TargetFullRelease.Version);

            Action<int>? progressHandler = progress is null ? null : progress.Report;

            await pendingUpdateManager.DownloadUpdatesAsync(
                pendingUpdate,
                progressHandler,
                cancellationToken);

            const string message = "Update downloaded. Foundry will restart to apply it.";
            logger.Information(message);
            return new ApplicationUpdateDownloadResult(ApplicationUpdateStatus.ReadyToRestart, message);
        }
        catch (Exception ex)
        {
            logger.Error(ex, "Foundry update download failed.");
            return new ApplicationUpdateDownloadResult(ApplicationUpdateStatus.Failed, ex.Message);
        }
    }

    public void ApplyUpdateAndRestart()
    {
        if (pendingUpdate is null || pendingUpdateManager is null)
        {
            logger.Warning("Apply update requested without a pending update.");
            return;
        }

        logger.Information("Applying Foundry update and restarting. Version={Version}", pendingUpdate.TargetFullRelease.Version);
        pendingUpdateManager.WaitExitThenApplyUpdates(pendingUpdate.TargetFullRelease, silent: false, restart: true);
        applicationLifetimeService.Shutdown();
    }

    private async Task RunStartupCheckAsync(CancellationToken cancellationToken)
    {
        try
        {
            ApplicationUpdateCheckResult result = await CheckForUpdatesAsync(isStartupCheck: true, cancellationToken);
            logger.Information("Startup update check completed. Status={Status}, Message={Message}", result.Status, result.Message);
        }
        catch (Exception ex)
        {
            logger.Error(ex, "Startup update check failed.");
        }
    }

    private UpdateManager CreateUpdateManager()
    {
        UpdateOptions options = new();

        string feedUrl = appSettingsService.Current.Updates.FeedUrl;
        if (!IsGitHubRepositoryUrl(feedUrl))
        {
            return new UpdateManager(feedUrl, options, locator: null);
        }

        GithubSource source = new(
            feedUrl,
            accessToken: string.Empty,
            prerelease: IsPrereleaseChannel(appSettingsService.Current.Updates.Channel),
            downloader: new HttpClientFileDownloader());

        return new UpdateManager(source, options, locator: null);
    }

    private static bool IsGitHubRepositoryUrl(string feedUrl)
    {
        return Uri.TryCreate(feedUrl, UriKind.Absolute, out Uri? uri)
            && string.Equals(uri.Host, "github.com", StringComparison.OrdinalIgnoreCase)
            && uri.AbsolutePath.Count(character => character == '/') == 2;
    }

    private static bool IsPrereleaseChannel(string? channel)
    {
        return string.Equals(channel, "beta", StringComparison.OrdinalIgnoreCase)
            || string.Equals(channel, "preview", StringComparison.OrdinalIgnoreCase)
            || string.Equals(channel, "prerelease", StringComparison.OrdinalIgnoreCase);
    }

    private static string ResolveUpdateSourceKind(string feedUrl)
    {
        return IsGitHubRepositoryUrl(feedUrl) ? "GitHubReleases" : "SimpleWeb";
    }

    private static string ResolveReleaseNotes(VelopackAsset targetRelease)
    {
        if (!string.IsNullOrWhiteSpace(targetRelease.NotesMarkdown))
        {
            return targetRelease.NotesMarkdown;
        }

        if (!string.IsNullOrWhiteSpace(targetRelease.NotesHTML))
        {
            return targetRelease.NotesHTML;
        }

        return "No release notes were provided for this update.";
    }

    private void ClearPendingUpdate()
    {
        pendingUpdate = null;
        pendingUpdateManager = null;
    }
}
