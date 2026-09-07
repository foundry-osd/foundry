// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Diagnostics;
using Foundry.Core.Services.Application;
using Foundry.Core.Services.Media;
using Foundry.Services.Adk;
using Foundry.Services.Settings;
using Serilog;
using Velopack;
using Velopack.Sources;

namespace Foundry.Services.Updates;

/// <summary>Owns each immutable update release from discovery through verified download and restart handoff.</summary>
internal sealed partial class ApplicationUpdateService(
    IAppSettingsService appSettingsService,
    IApplicationLifetimeService applicationLifetimeService,
    IApplicationUpdateStateService updateStateService,
    MediaOperationCoordinator mediaCoordinator,
    IAdkService adkService,
    ILogger logger) : IApplicationUpdateService, IDisposable
{
    private readonly ILogger logger = logger.ForContext<ApplicationUpdateService>();
    private readonly SemaphoreSlim transition = new(1, 1);
    private readonly object lifetimeGate = new();
    private readonly CancellationTokenSource lifetime = new();
    private ApplicationUpdateOperation? availableOperation;
    private ApplicationUpdateOperation? downloadedOperation;
    private Task? startupCheck;
    private Task? shutdown;
    private TaskCompletionSource drained = CompletedSource();
    private int activeOperations;
    private bool stopping;

    public Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (lifetimeGate)
        {
            if (stopping || startupCheck is not null || !appSettingsService.Current.Updates.CheckOnStartup)
                return Task.CompletedTask;
            startupCheck = Task.Run(RunStartupCheckAsync);
        }
        return Task.CompletedTask;
    }

    public async Task<ApplicationUpdateCheckResult> CheckForUpdatesAsync(bool isStartupCheck = false,
        CancellationToken cancellationToken = default)
    {
        BeginOperation();
        try
        {
            using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token, cancellationToken);
            await transition.WaitAsync(cancellation.Token).ConfigureAwait(false);
            try
            {
                cancellation.Token.ThrowIfCancellationRequested();
                if (downloadedOperation is not null)
                    return PublishCheckResult(AvailableResult(downloadedOperation));
                if (Debugger.IsAttached)
                    return PublishCheckResult(new(ApplicationUpdateStatus.SkippedInDebug, "Update check skipped because a debugger is attached."));

                string feedUrl = appSettingsService.Current.Updates.FeedUrl.Trim();
                string channel = appSettingsService.Current.Updates.Channel;
                using var metadataDeadline = CancellationTokenSource.CreateLinkedTokenSource(cancellation.Token);
                metadataDeadline.CancelAfter(TimeSpan.FromSeconds(15));
                try
                {
                    var downloader = new ApplicationUpdateFileDownloader(lifetime.Token, metadataDeadline.Token);
                    UpdateManager manager = CreateUpdateManager(feedUrl, channel, downloader);
                    logger.Information("Checking for Foundry updates. Startup={Startup}, FeedSource={FeedSource}, Channel={Channel}",
                        isStartupCheck, GetFeedUrlLogValue(feedUrl), channel);
                    if (!manager.IsInstalled)
                    {
                        availableOperation = null;
                        return PublishCheckResult(new(ApplicationUpdateStatus.NotInstalled, "Foundry is not running from a Velopack installation."));
                    }
                    UpdateInfo? update = await manager.CheckForUpdatesAsync().ConfigureAwait(false);
                    metadataDeadline.Token.ThrowIfCancellationRequested();
                    availableOperation = update is null ? null : new(Guid.NewGuid(), manager, update, feedUrl, channel);
                    return PublishCheckResult(availableOperation is null
                        ? new(ApplicationUpdateStatus.NoUpdate, $"{FoundryApplicationInfo.AppName} is up to date.")
                        : AvailableResult(availableOperation));
                }
                catch (OperationCanceledException) when (!cancellation.IsCancellationRequested)
                {
                    return PublishCheckResult(new(ApplicationUpdateStatus.Failed, "The update feed check timed out."));
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception error)
                {
                    logger.Error(error, "Foundry update check failed.");
                    return PublishCheckResult(new(ApplicationUpdateStatus.Failed, "The update feed could not be checked."));
                }
            }
            finally { transition.Release(); }
        }
        finally { EndOperation(); }
    }

    public async Task<ApplicationUpdateDownloadResult> DownloadUpdateAsync(IProgress<int>? progress = null,
        CancellationToken cancellationToken = default)
    {
        BeginOperation();
        try
        {
            using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token, cancellationToken);
            await transition.WaitAsync(cancellation.Token).ConfigureAwait(false);
            try
            {
                cancellation.Token.ThrowIfCancellationRequested();
                if (downloadedOperation is not null) return DownloadedResult(downloadedOperation);
                ApplicationUpdateOperation? operation = availableOperation;
                if (operation is null) return new(ApplicationUpdateStatus.NoUpdate, "Check for updates before downloading.");
                try
                {
                    await operation.Manager.DownloadUpdatesAsync(operation.Update, progress is null ? null : progress.Report,
                        cancellation.Token).ConfigureAwait(false);
                    cancellation.Token.ThrowIfCancellationRequested();
                    downloadedOperation = operation;
                    return DownloadedResult(operation);
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception error)
                {
                    logger.Error(error, "Foundry update download failed. OperationId={OperationId}", operation.Id);
                    return new(ApplicationUpdateStatus.Failed, "The update could not be downloaded.");
                }
            }
            finally { transition.Release(); }
        }
        finally { EndOperation(); }
    }

    public void ApplyUpdateAndRestart(Guid operationId)
    {
        lock (lifetimeGate)
            if (stopping) throw new InvalidOperationException("The update service is shutting down.");
        if (!transition.Wait(0)) throw new InvalidOperationException("An update operation is still in progress.");
        try
        {
            ApplicationUpdateOperation operation = downloadedOperation is { } ready && ready.Id == operationId
                ? ready : throw new InvalidOperationException("The requested update has not been downloaded successfully.");
            if (mediaCoordinator.IsRunning || mediaCoordinator.RecoveryDiagnostic is not null ||
                adkService.ActiveOperation is { IsCompleted: false } || adkService.HasUncertainOwnership)
                throw new InvalidOperationException("Complete native media or ADK cleanup before applying an application update.");
            using MediaOperationLease lease = MediaOperationLease.Acquire(Constants.WinPeWorkspaceDirectoryPath, "ApplicationUpdate");
            lease.DeleteOwnedWorkspace();
            logger.Information("Applying downloaded update. OperationId={OperationId}, Version={Version}", operation.Id,
                operation.Update.TargetFullRelease.Version);
            operation.Manager.WaitExitThenApplyUpdates(operation.Update.TargetFullRelease, silent: false, restart: true);
            applicationLifetimeService.Shutdown();
        }
        finally { transition.Release(); }
    }

    public Task ShutdownAsync(CancellationToken cancellationToken = default)
    {
        Task completion;
        lock (lifetimeGate)
        {
            stopping = true;
            shutdown ??= WaitForShutdownAsync(StopAndDrainAsync(drained.Task, startupCheck));
            completion = shutdown;
        }
        return cancellationToken.CanBeCanceled ? completion.WaitAsync(cancellationToken) : completion;
    }

    public void Dispose() => ShutdownAsync().GetAwaiter().GetResult();

    private async Task StopAndDrainAsync(Task operations, Task? startup)
    {
        try
        {
            await lifetime.CancelAsync().ConfigureAwait(false);
            await operations.ConfigureAwait(false);
            if (startup is not null) await startup.ConfigureAwait(false);
        }
        catch (Exception error) { logger.Debug(error, "Update shutdown completed with an optional operation failure."); }
        finally { lifetime.Dispose(); }
    }

    private static async Task WaitForShutdownAsync(Task completion)
    {
        try { await completion.WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false); }
        catch (TimeoutException) { /* Actual completion remains owned and observed by StopAndDrainAsync. */ }
    }

    private void BeginOperation()
    {
        lock (lifetimeGate)
        {
            if (stopping) throw new OperationCanceledException("The update service is shutting down.");
            if (activeOperations++ == 0) drained = new(TaskCreationOptions.RunContinuationsAsynchronously);
        }
    }

    private void EndOperation()
    {
        lock (lifetimeGate) if (--activeOperations == 0) drained.TrySetResult();
    }

    private async Task RunStartupCheckAsync()
    {
        try { await CheckForUpdatesAsync(isStartupCheck: true).ConfigureAwait(false); }
        catch (OperationCanceledException) { logger.Debug("Startup update check canceled."); }
        catch (Exception error) { logger.Error(error, "Startup update check failed."); }
    }

    private static UpdateManager CreateUpdateManager(string feedUrl, string channel, ApplicationUpdateFileDownloader downloader)
    {
        UpdateOptions options = new();
        if (ApplicationUpdateSourceClassifier.IsGitHubRepositoryUrl(feedUrl))
            return new UpdateManager(new GithubSource(feedUrl.TrimEnd('/'), string.Empty,
                ApplicationUpdateSourceClassifier.IsPrereleaseChannel(channel), downloader), options, locator: null);
        if (Uri.TryCreate(feedUrl, UriKind.Absolute, out Uri? uri) && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
            return new UpdateManager(new SimpleWebSource(uri, downloader), options, locator: null);
        return new UpdateManager(feedUrl, options, locator: null);
    }

    private ApplicationUpdateCheckResult PublishCheckResult(ApplicationUpdateCheckResult result)
    {
        // LastCheckedAt records the last attempt, including unsuccessful checks.
        appSettingsService.Current.Updates.LastCheckedAt = DateTimeOffset.Now;
        try { appSettingsService.Save(); }
        catch (Exception error)
        {
            logger.Warning(error, "The last update check time could not be saved.");
            result = result with { SettingsSaveFailed = true };
        }
        updateStateService.Publish(result);
        return result;
    }

    private static ApplicationUpdateCheckResult AvailableResult(ApplicationUpdateOperation operation)
    {
        string version = FormatDisplayVersion(operation.Update.TargetFullRelease.Version?.ToString());
        return new(ApplicationUpdateStatus.UpdateAvailable, $"{FoundryApplicationInfo.AppName} {version} is available.", version);
    }

    private static ApplicationUpdateDownloadResult DownloadedResult(ApplicationUpdateOperation operation) =>
        new(ApplicationUpdateStatus.ReadyToRestart, "Update downloaded. Foundry will restart to apply it.", operation.Id);

    private static TaskCompletionSource CompletedSource()
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        completion.SetResult();
        return completion;
    }

    private static string GetFeedUrlLogValue(string feedUrl)
    {
        if (!Uri.TryCreate(feedUrl, UriKind.Absolute, out Uri? uri))
        {
            return feedUrl;
        }

        if (uri.IsFile)
        {
            return uri.LocalPath;
        }

        UriBuilder builder = new(uri)
        {
            UserName = string.Empty,
            Password = string.Empty,
            Query = string.Empty,
            Fragment = string.Empty
        };

        return builder.Uri.ToString().TrimEnd('/');
    }

    private static string FormatDisplayVersion(string? packageVersion)
    {
        if (string.IsNullOrWhiteSpace(packageVersion))
        {
            return "unknown";
        }

        return packageVersion.Trim().Replace("-build.", ".", StringComparison.OrdinalIgnoreCase);
    }

}
