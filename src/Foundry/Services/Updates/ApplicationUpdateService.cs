// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Diagnostics;
using Foundry.Core.Services.Application;
using Foundry.Services.Settings;
using Serilog;
using Velopack;
using Velopack.Sources;

namespace Foundry.Services.Updates;

/// <summary>
/// Owns serialized update preparation and retains a prepared target until application handoff.
/// </summary>
internal sealed class ApplicationUpdateService(
    IAppSettingsService appSettingsService,
    IAppDispatcher dispatcher,
    IApplicationUpdateStateService updateStateService,
    ILogger logger) : IApplicationUpdateService
{
    private readonly ILogger logger = logger.ForContext<ApplicationUpdateService>();
    private readonly SemaphoreSlim operationGate = new(1, 1);
    private readonly object stateLock = new();
    private readonly object handoffLock = new();
    private CancellationTokenSource shutdownCancellation = new();
    private ApplicationUpdateCheckResult currentResult = new(ApplicationUpdateStatus.Ready, "Update service is ready.");
    private UpdateSelection? selection;
    private UpdateSelection? shutdownSelection;
    private long operationGeneration;
    private bool initialized;
    private bool shuttingDown;
    private bool prepared;
    private bool handoffScheduled;

    /// <inheritdoc />
    public Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        long generation;
        lock (stateLock)
        {
            if (initialized || shuttingDown)
            {
                return Task.CompletedTask;
            }

            initialized = true;
            generation = operationGeneration;
        }

        UpdateSettings settings = appSettingsService.Current.Updates;
        logger.Information(
            "Update service initialized. CheckOnStartup={CheckOnStartup}, Channel={Channel}, FeedSource={FeedSource}",
            settings.CheckOnStartup,
            settings.Channel,
            GetFeedUrlLogValue(settings.FeedUrl));

        _ = Task.Run(() => RunStartupPreparationAsync(generation), CancellationToken.None);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<ApplicationUpdateCheckResult> CheckForUpdatesAsync(CancellationToken cancellationToken = default)
    {
        return RunSerializedAsync(operation => CheckCoreAsync(operation, isStartupCheck: false), cancellationToken);
    }

    /// <inheritdoc />
    public Task DownloadUpdateAsync(CancellationToken cancellationToken = default)
    {
        return RunSerializedAsync(DownloadCoreAsync, cancellationToken);
    }

    /// <inheritdoc />
    public bool TrySchedulePreparedUpdate(bool restart)
    {
        // Only handoff callers wait here; shutdown fencing uses the separate state lock.
        lock (handoffLock)
        {
            UpdateSelection? target;
            long generation;
            lock (stateLock)
            {
                target = shuttingDown ? shutdownSelection : prepared ? selection : null;
                if (target is null)
                {
                    return false;
                }

                if (handoffScheduled)
                {
                    return true;
                }

                generation = operationGeneration;
            }

            // Applying a package force-stops every process under the install directory. A silent apply is therefore
            // skipped while another instance is running and the update stays prepared; an explicit restart request
            // stays the user's decision.
            if (!restart && RunningInstanceProbe.IsAnotherInstanceRunning())
            {
                logger.Information(
                    "Prepared Foundry update not scheduled because another Foundry instance is running. Version={Version}",
                    target.Version);
                return false;
            }

            try
            {
                logger.Information("Scheduling prepared Foundry update. Version={Version}, Restart={Restart}", target.Version, restart);
                target.Manager.WaitExitThenApplyUpdates(target.Asset, silent: !restart, restart: restart);
                lock (stateLock)
                {
                    handoffScheduled = true;
                }

                return true;
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Prepared Foundry update handoff failed. Version={Version}, Restart={Restart}", target.Version, restart);
                ApplicationUpdateCheckResult failure = CreateSelectionResult(
                    target,
                    ApplicationUpdateStatus.ReadyToApply,
                    "The prepared update could not be applied. Try applying it again.",
                    downloadProgress: 100,
                    failureMessage: ex.Message);
                lock (stateLock)
                {
                    currentResult = failure;
                }

                QueuePublication(failure, generation);
                throw;
            }
        }
    }

    /// <inheritdoc />
    public void BeginShutdown()
    {
        CancellationTokenSource cancellation;
        lock (stateLock)
        {
            if (shuttingDown)
            {
                return;
            }

            shutdownSelection = prepared ? selection : null;
            shuttingDown = true;
            operationGeneration++;
            cancellation = shutdownCancellation;
        }

        // Cancellation never waits for the SDK operation gate or its queued dispatcher callbacks.
        try
        {
            cancellation.Cancel();
        }
        finally
        {
            cancellation.Dispose();
        }
    }

    /// <inheritdoc />
    public void CancelShutdown()
    {
        ApplicationUpdateCheckResult result;
        long generation;
        lock (stateLock)
        {
            if (!shuttingDown)
            {
                return;
            }

            shutdownCancellation = new CancellationTokenSource();
            shutdownSelection = null;
            shuttingDown = false;
            generation = operationGeneration;
            result = currentResult.IsBusy
                ? selection is null
                    ? new ApplicationUpdateCheckResult(ApplicationUpdateStatus.Ready, "Update service is ready.")
                    : CreateSelectionResult(selection, ApplicationUpdateStatus.UpdateAvailable, $"{FoundryApplicationInfo.AppName} {selection.Version} is available.")
                : currentResult;
            currentResult = result;
        }

        QueuePublication(result, generation);
    }

    private async Task<ApplicationUpdateCheckResult> RunSerializedAsync(
        Func<UpdateOperation, Task<ApplicationUpdateCheckResult>> action,
        CancellationToken cancellationToken,
        long? expectedGeneration = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        UpdateOperation operation;
        lock (stateLock)
        {
            if (shuttingDown || expectedGeneration is not null && expectedGeneration != operationGeneration)
            {
                return currentResult;
            }

            operation = new UpdateOperation(
                operationGeneration,
                CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, shutdownCancellation.Token));
        }

        using (operation.Cancellation)
        {
            bool entered = false;
            ApplicationUpdateCheckResult? previousResult = null;
            try
            {
                await operationGate.WaitAsync(operation.Token);
                entered = true;
                operation.Token.ThrowIfCancellationRequested();
                lock (stateLock)
                {
                    previousResult = currentResult;
                }

                return await action(operation);
            }
            catch (OperationCanceledException) when (operation.Token.IsCancellationRequested)
            {
                if (previousResult is not null)
                {
                    PublishResult(previousResult, operation.Generation);
                }

                cancellationToken.ThrowIfCancellationRequested();
                return GetCurrentResult();
            }
            finally
            {
                if (entered)
                {
                    operationGate.Release();
                }
            }
        }
    }

    private async Task<ApplicationUpdateCheckResult> CheckCoreAsync(UpdateOperation operation, bool isStartupCheck)
    {
        lock (stateLock)
        {
            if (prepared)
            {
                return currentResult;
            }
        }

        if (Debugger.IsAttached)
        {
            return PublishResult(new ApplicationUpdateCheckResult(
                ApplicationUpdateStatus.SkippedInDebug,
                "Update check skipped because a debugger is attached."), operation.Generation);
        }

        Stopwatch stopwatch = Stopwatch.StartNew();
        try
        {
            UpdateSettings settings = appSettingsService.Current.Updates;
            string feedUrl = settings.FeedUrl;
            string channel = settings.Channel;
            bool checkOnStartup = settings.CheckOnStartup;
            UpdateManager manager = CreateUpdateManager(feedUrl, channel);
            if (!manager.IsInstalled)
            {
                return CompleteCheck(new ApplicationUpdateCheckResult(
                    ApplicationUpdateStatus.NotInstalled,
                    "Update check skipped because Foundry is not running from a Velopack installation."), operation);
            }

            if (TryRecoverPreparedUpdate(manager, operation, out ApplicationUpdateCheckResult? recovered))
            {
                return recovered!;
            }

            if (isStartupCheck && !checkOnStartup)
            {
                return GetCurrentResult();
            }

            UpdateSelection? knownTarget;
            lock (stateLock)
            {
                knownTarget = selection;
            }

            ApplicationUpdateCheckResult checking = knownTarget is null
                ? new ApplicationUpdateCheckResult(ApplicationUpdateStatus.Checking, "Checking for Foundry updates.")
                : CreateSelectionResult(knownTarget, ApplicationUpdateStatus.Checking, "Checking for Foundry updates.");
            PublishResult(checking, operation.Generation);
            logger.Information(
                "Checking for Foundry updates. IsStartupCheck={IsStartupCheck}, SourceKind={SourceKind}, FeedSource={FeedSource}, Channel={Channel}",
                isStartupCheck,
                ResolveUpdateSourceKind(feedUrl),
                GetFeedUrlLogValue(feedUrl),
                channel);

            // The pinned SDK check has no cancellation parameter; retain serialization until it completes.
            Velopack.UpdateInfo? updateInfo = await manager.CheckForUpdatesAsync();
            operation.Token.ThrowIfCancellationRequested();
            if (updateInfo is null)
            {
                logger.Information("Foundry update check completed without an update. IsStartupCheck={IsStartupCheck}, ElapsedMilliseconds={ElapsedMilliseconds}",
                    isStartupCheck, stopwatch.ElapsedMilliseconds);
                return PublishSelectionResult(null, new ApplicationUpdateCheckResult(
                    ApplicationUpdateStatus.NoUpdate,
                    $"{FoundryApplicationInfo.AppName} is up to date."), operation, checkedAt: DateTimeOffset.Now);
            }

            UpdateSelection target = CreateSelection(manager, updateInfo, updateInfo.TargetFullRelease);
            logger.Information("Foundry update available. Version={Version}, FileName={FileName}, IsStartupCheck={IsStartupCheck}, ElapsedMilliseconds={ElapsedMilliseconds}",
                target.Version, target.Asset.FileName, isStartupCheck, stopwatch.ElapsedMilliseconds);
            return PublishSelectionResult(target, CreateSelectionResult(target, ApplicationUpdateStatus.UpdateAvailable,
                $"{FoundryApplicationInfo.AppName} {target.Version} is available."), operation, checkedAt: DateTimeOffset.Now);
        }
        catch (OperationCanceledException) when (operation.Token.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.Error(ex, "Foundry update check failed. IsStartupCheck={IsStartupCheck}, ElapsedMilliseconds={ElapsedMilliseconds}", isStartupCheck, stopwatch.ElapsedMilliseconds);
            UpdateSelection? target;
            lock (stateLock)
            {
                target = selection;
            }

            ApplicationUpdateCheckResult result = target is null
                ? new ApplicationUpdateCheckResult(ApplicationUpdateStatus.Failed, ex.Message, FailureMessage: ex.Message)
                : CreateSelectionResult(target, ApplicationUpdateStatus.UpdateAvailable,
                    $"{FoundryApplicationInfo.AppName} {target.Version} is available.", failureMessage: ex.Message);
            return CompleteCheck(result, operation);
        }
    }

    private async Task<ApplicationUpdateCheckResult> DownloadCoreAsync(UpdateOperation operation)
    {
        UpdateSelection? target;
        lock (stateLock)
        {
            if (prepared)
            {
                return currentResult;
            }

            target = selection;
        }

        if (target is null)
        {
            return PublishResult(new ApplicationUpdateCheckResult(
                ApplicationUpdateStatus.NoUpdate,
                "No update is ready to download. Check for updates first."), operation.Generation);
        }

        try
        {
            PublishResult(CreateSelectionResult(target, ApplicationUpdateStatus.Downloading, "Downloading Foundry update."), operation.Generation);
            logger.Information("Downloading Foundry update. Version={Version}", target.Version);
            await target.Manager.DownloadUpdatesAsync(
                target.Plan!,
                progress => PublishDownloadProgress(target, progress, operation),
                operation.Token);
            operation.Token.ThrowIfCancellationRequested();
            logger.Information("Foundry update prepared. Version={Version}", target.Version);
            return PublishSelectionResult(target, CreateSelectionResult(target, ApplicationUpdateStatus.ReadyToApply,
                "Update downloaded. Apply it now or close Foundry to apply it automatically.", downloadProgress: 100), operation, isPrepared: true);
        }
        catch (OperationCanceledException) when (operation.Token.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.Error(ex, "Foundry update download failed. Version={Version}", target.Version);
            return PublishResult(CreateSelectionResult(target, ApplicationUpdateStatus.Failed,
                "The update could not be downloaded. Try downloading it again.", failureMessage: ex.Message), operation.Generation);
        }
    }

    private bool TryRecoverPreparedUpdate(
        UpdateManager manager,
        UpdateOperation operation,
        out ApplicationUpdateCheckResult? result)
    {
        result = null;
        VelopackAsset? asset = manager.UpdatePendingRestart;
        if (asset is null)
        {
            return false;
        }

        // This is the SDK's local preparation lookup, not an independent package hash verification.
        UpdateSelection target = CreateSelection(manager, null, asset);
        logger.Information("Recovered prepared Foundry update. Version={Version}", target.Version);
        result = PublishSelectionResult(target, CreateSelectionResult(target, ApplicationUpdateStatus.ReadyToApply,
            "A downloaded update is ready to apply.", downloadProgress: 100), operation, isPrepared: true);
        return true;
    }

    private void PublishDownloadProgress(UpdateSelection target, int progress, UpdateOperation operation)
    {
        if (operation.Token.IsCancellationRequested)
        {
            return;
        }

        ApplicationUpdateCheckResult result = CreateSelectionResult(target, ApplicationUpdateStatus.Downloading,
            "Downloading Foundry update.", downloadProgress: Math.Clamp(progress, 0, 100));
        lock (stateLock)
        {
            if (!IsLive(operation.Generation) || prepared || !ReferenceEquals(selection, target)
                || currentResult.Status != ApplicationUpdateStatus.Downloading)
            {
                return;
            }

            currentResult = result;
        }

        QueuePublication(result, operation.Generation);
    }

    private ApplicationUpdateCheckResult PublishSelectionResult(
        UpdateSelection? target,
        ApplicationUpdateCheckResult result,
        UpdateOperation operation,
        bool isPrepared = false,
        DateTimeOffset? checkedAt = null)
    {
        operation.Token.ThrowIfCancellationRequested();
        lock (stateLock)
        {
            if (!IsLive(operation.Generation))
            {
                return currentResult;
            }

            selection = target;
            prepared = isPrepared;
            currentResult = result;
        }

        QueuePublication(result, operation.Generation, checkedAt);
        return result;
    }

    private ApplicationUpdateCheckResult CompleteCheck(ApplicationUpdateCheckResult result, UpdateOperation operation)
    {
        operation.Token.ThrowIfCancellationRequested();
        return PublishResult(result, operation.Generation, checkedAt: DateTimeOffset.Now);
    }

    private ApplicationUpdateCheckResult PublishResult(ApplicationUpdateCheckResult result, long generation, DateTimeOffset? checkedAt = null)
    {
        lock (stateLock)
        {
            if (!IsLive(generation))
            {
                return currentResult;
            }

            currentResult = result;
        }

        QueuePublication(result, generation, checkedAt);
        return result;
    }

    private void QueuePublication(ApplicationUpdateCheckResult result, long generation, DateTimeOffset? checkedAt = null)
    {
        dispatcher.TryEnqueue(() =>
        {
            lock (stateLock)
            {
                if (!IsLive(generation))
                {
                    return;
                }

                if (checkedAt is not null)
                {
                    appSettingsService.Current.Updates.LastCheckedAt = checkedAt;
                    try
                    {
                        appSettingsService.Save();
                    }
                    catch (Exception ex)
                    {
                        logger.Warning(ex, "Failed to save the terminal update check timestamp.");
                    }
                }

                if (ReferenceEquals(currentResult, result))
                {
                    updateStateService.Publish(result);
                }
            }
        });
    }

    private bool IsLive(long generation)
    {
        return !shuttingDown && operationGeneration == generation;
    }

    private ApplicationUpdateCheckResult GetCurrentResult()
    {
        lock (stateLock)
        {
            return currentResult;
        }
    }

    private async Task RunStartupPreparationAsync(long generation)
    {
        try
        {
            ApplicationUpdateCheckResult result = await RunSerializedAsync(
                operation => CheckCoreAsync(operation, isStartupCheck: true), CancellationToken.None, generation);
            if (result.HasKnownUpdate && !result.IsReadyToApply && appSettingsService.Current.Updates.CheckOnStartup)
            {
                await RunSerializedAsync(DownloadCoreAsync, CancellationToken.None, generation);
            }
        }
        catch (Exception ex)
        {
            logger.Error(ex, "Startup update preparation failed.");
        }
    }

    private UpdateManager CreateUpdateManager(string feedUrl, string channel)
    {
        // Keep the installation's default architecture channel; settings only control GitHub prerelease visibility.
        UpdateOptions options = new();
        if (!IsGitHubRepositoryUrl(feedUrl))
        {
            return new UpdateManager(feedUrl, options, locator: null);
        }

        GithubSource source = new(feedUrl, accessToken: string.Empty,
            prerelease: IsPrereleaseChannel(channel), downloader: new HttpClientFileDownloader());
        return new UpdateManager(source, options, locator: null);
    }

    private static UpdateSelection CreateSelection(UpdateManager manager, Velopack.UpdateInfo? plan, VelopackAsset asset)
    {
        return new UpdateSelection(manager, plan, asset,
            FormatDisplayVersion(asset.Version?.ToString()));
    }

    private static ApplicationUpdateCheckResult CreateSelectionResult(
        UpdateSelection target,
        ApplicationUpdateStatus status,
        string message,
        int downloadProgress = 0,
        string? failureMessage = null)
    {
        return new ApplicationUpdateCheckResult(status, message, target.Version, downloadProgress, failureMessage);
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
        return string.IsNullOrWhiteSpace(packageVersion)
            ? "unknown"
            : packageVersion.Trim().Replace("-build.", ".", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Captures the SDK manager, download plan, and target so downloads and handoff use the same selection.
    /// </summary>
    private sealed record UpdateSelection(
        UpdateManager Manager,
        Velopack.UpdateInfo? Plan,
        VelopackAsset Asset,
        string Version);

    /// <summary>
    /// Associates a serialized request with its shutdown generation and linked cancellation lifetime.
    /// </summary>
    private sealed record UpdateOperation(long Generation, CancellationTokenSource Cancellation)
    {
        public CancellationToken Token { get; } = Cancellation.Token;
    }
}
