// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Diagnostics;
using System.Globalization;
using Foundry.Core.Models.Configuration;
using Foundry.Core.Services.Application;
using Foundry.Core.Services.Drivers;
using Foundry.Core.Services.Images;
using Foundry.Core.Services.Media;
using Foundry.Core.Services.NetworkCache;
using Foundry.Core.Services.Packages;
using Foundry.Core.Services.Sharing;
using Foundry.Core.Services.WinPe;
using Foundry.Services.Adk;
using Foundry.Services.Configuration;
using Foundry.Services.Localization;
using Foundry.Services.Operations;
using Foundry.Services.Settings;
using Foundry.Services.Shell;
using Foundry.Telemetry;
using Foundry.Utilities.Networking;
using Serilog;
using Serilog.Context;

namespace Foundry.Services.NetworkCache;

/// <summary>
/// Publishes imported content and the signed index to the network cache shares, shared by media creation and the Publish
/// command. Pure rules (records, sequences, binding, telemetry values) stay in Foundry.Core.
/// </summary>
internal sealed class NetworkCachePublicationService(
    IFoundryConfigurationStateService configuration,
    INetworkCacheSigningService signing,
    INetworkCachePublicationStateService publicationState,
    IAppSettingsService appSettings,
    CustomImageLibraryService customImageLibrary,
    ContentPackageLibraryService postInstallPackages,
    CustomDriverSetLibrary customDriverSetLibrary,
    IOperationProgressService operationProgress,
    IShellNavigationGuardService navigationGuard,
    IAdkService adkService,
    ITelemetryService telemetry,
    IExternalProcessLauncher processLauncher,
    IApplicationLocalizationService localization,
    ISmbConnector smbConnector,
    ILogger logger) : INetworkCachePublicationService
{
    private const int PreparationPercent = 10;
    private readonly NetworkCacheContentPublisher publisher = new(smbConnector);
    private readonly NetworkCacheShareAccess shareAccess = new(smbConnector);
    private readonly ILogger log = logger.ForContext<NetworkCachePublicationService>();

    /// <inheritdoc />
    public async Task<NetworkCacheIndexBinding?> ResolveIndexBindingAsync(CancellationToken cancellationToken)
    {
        NetworkCacheSettings settings = configuration.Current.NetworkCache;
        if (!NetworkCacheContentPublisher.IsActive(settings)) return null;
        // The guaranteed creation point: a publishing PC has its key before the media bind to it.
        signing.EnsureSigningKey();
        if (signing.TrustedPublicKey is not { } trustedKey)
        {
            log.Warning("Network cache index binding is unavailable: this PC has no trusted publisher key.");
            return null;
        }

        IReadOnlyDictionary<string, string> passwords = configuration.MediaInputs.NetworkCachePasswords;
        // All shares are read at once, so dead shares cost one timeout instead of one each. RunAsync collects each password
        // synchronously, on this UI thread, before its pool work starts.
        Task<NetworkCacheShareAccessResult<long>>[] reads = settings.Shares
            .Select(share => shareAccess.RunAsync(
                settings,
                share,
                accountId => passwords.TryGetValue(accountId, out string? password) ? password.ToCharArray() : null,
                (contentRoot, token) => NetworkCacheIndexStore.ReadSequenceAsync(contentRoot, trustedKey, token),
                cancellationToken))
            .ToArray();
        NetworkCacheShareAccessResult<long>[] results = await Task.WhenAll(reads);
        long minimumSequence = 0;
        int unreadShareCount = 0;
        for (int index = 0; index < results.Length; index++)
        {
            NetworkCacheShare share = settings.Shares[index];
            NetworkCacheShareAccessResult<long> result = results[index];
            if (result.Status == NetworkCacheShareAccessStatus.Completed)
            {
                minimumSequence = Math.Max(minimumSequence, result.Value);
                continue;
            }

            unreadShareCount++;
            log.Warning(
                result.Error,
                "Network cache index sequence could not be read; the share counts as sequence 0. SharePath={SharePath}, Status={Status}, ConnectStatus={ConnectStatus}, Win32Error={Win32Error}",
                share.Path,
                result.Status,
                result.Connection?.Status,
                result.Connection?.Win32Error);
        }

        log.Information(
            "Network cache index binding resolved. KeyId={KeyId}, MinimumSequence={MinimumSequence}, ShareCount={ShareCount}, UnreadShareCount={UnreadShareCount}",
            PublicationSigner.KeyId(trustedKey),
            minimumSequence,
            settings.Shares.Count,
            unreadShareCount);
        return new NetworkCacheIndexBinding(trustedKey, minimumSequence);
    }

    /// <inheritdoc />
    public async Task<NetworkCachePublicationOutcome> PublishContentAsync(
        BuildSnapshot snapshot,
        NetworkCacheContentLeases content,
        NetworkCacheContentSections sections,
        long minimumSequence,
        Action<int, string> report,
        CancellationToken cancellationToken)
    {
        FoundryConfigurationDocument published = snapshot.Configuration;
        NetworkCacheSettings settings = published.NetworkCache;
        if (!NetworkCacheContentPublisher.IsActive(settings)) return NetworkCachePublicationOutcome.None;

        bool publishesIndex = signing.CanPublishIndex;
        log.Information("Network cache content publication started. ShareCount={ShareCount}, PublishesIndex={PublishesIndex}", settings.Shares.Count, publishesIndex);
        Stopwatch stopwatch = Stopwatch.StartNew();
        IReadOnlyList<NetworkCacheContentPublishResult> results;
        PublicationSigningKey? signer = null;
        // Built here, on the caller's UI thread, so share progress reported from pool threads reaches report on that thread.
        var progress = new ShareProgressRelay(localization, report);
        try
        {
            NetworkCacheIndexPublication? index = null;
            if (publishesIndex)
            {
                signer = signing.GetOrCreateSigningKey();
                index = new NetworkCacheIndexPublication(signer, sections, NetworkCachePublicationRecords.SequenceFloors(
                    settings.Shares, NetworkCachePublicationRecords.Valid(appSettings.Current.NetworkCache.Publications), minimumSequence));
            }

            results = await publisher.PublishAsync(settings, snapshot.GetNetworkCachePasswordCopy,
                (contentRoot, token) => PublishLeasesAsync(content, contentRoot, CreateShareProgress(settings, content, contentRoot, progress), token),
                index, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            log.Warning(exception, "Network cache content publication failed. DurationMs={DurationMs}", stopwatch.ElapsedMilliseconds);
            results = settings.Shares
                .Select(share => new NetworkCacheContentPublishResult(share, NetworkCacheContentPublishStatus.Failed, null, exception))
                .ToArray();
            publicationState.Record(published, results);
            return new NetworkCachePublicationOutcome(results);
        }
        finally
        {
            // The caller shows its next state once this method returns; a report still queued must not overwrite it.
            progress.Close();
            signer?.Dispose();
        }

        publicationState.Record(published, results);
        NetworkCacheContentPublishResult[] skipped = results.Where(result => result.Status != NetworkCacheContentPublishStatus.Published).ToArray();
        foreach (NetworkCacheContentPublishResult result in skipped)
        {
            log.Warning(
                result.Error,
                "Network cache share did not receive the imported content. SharePath={SharePath}, Status={Status}, ConnectStatus={ConnectStatus}, Win32Error={Win32Error}",
                result.Share.Path,
                result.Status,
                result.Connection?.Status,
                result.Connection?.Win32Error);
        }

        log.Information(
            "Network cache content publication completed. PublishedCount={PublishedCount}, SkippedCount={SkippedCount}, IndexSequences={IndexSequences}, DurationMs={DurationMs}",
            results.Count - skipped.Length,
            skipped.Length,
            results.Select(result => $"{result.Share.Path}={result.IndexSequence?.ToString(CultureInfo.InvariantCulture) ?? "none"}").ToArray(),
            stopwatch.ElapsedMilliseconds);
        return new NetworkCachePublicationOutcome(results);
    }

    /// <inheritdoc />
    public async Task PublishAsync()
    {
        FoundryConfigurationDocument current = configuration.Current;
        if (operationProgress.State.IsRunning ||
            navigationGuard.State is not (ShellNavigationState.Ready or ShellNavigationState.AdkBlocked) ||
            !NetworkCacheContentPublisher.IsActive(current.NetworkCache) || !signing.CanPublishIndex)
        {
            return;
        }

        using var cancellation = new CancellationTokenSource();
        CancellationToken cancellationToken = cancellation.Token;
        string operationId = Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture);
        using IDisposable operationIdScope = LogContext.PushProperty("OperationId", operationId);
        using IDisposable workflowScope = LogContext.PushProperty("Workflow", "network_cache_publication");
        string status = localization.GetString("StartMedia.Operation.PublishingToNetworkCache");
        operationProgress.Start(OperationKind.NetworkCachePublication, Title("Running"), status, cancellation.Cancel);
        navigationGuard.SetState(ShellNavigationState.OperationRunning);
        log.Information("Network cache publication started. ShareCount={ShareCount}", current.NetworkCache.Shares.Count);
        Stopwatch stopwatch = Stopwatch.StartNew();
        NetworkCachePublicationOutcome outcome = NetworkCachePublicationOutcome.None;
        // An attempt that ends in error before the shares were processed is a failed publication; a cancelled one stays none.
        string publishResult = TelemetryNetworkCachePublishResults.None;
        BuildSnapshot? snapshot = null;
        NetworkCacheContentLeases? content = null;
        try
        {
            if (!signing.EnsureSigningKey() || !signing.CanSignIndex)
            {
                // Without a key the index cannot be signed: name that cause instead of failing every share.
                publishResult = TelemetryNetworkCachePublishResults.Failed;
                operationProgress.Finish(OperationOutcome.Error, Title("Failed"), JoinParagraphs(
                    localization.GetString("NetworkCache.PublishKeysUnavailableMessage"),
                    localization.GetString("StartMedia.Operation.SeeLog")), OpenLogFolderAction());
                log.Warning("Network cache publication stopped: this PC has no signing key that can be read or created. DurationMs={DurationMs}", stopwatch.ElapsedMilliseconds);
                return;
            }

            snapshot = await configuration.CaptureBuildSnapshotAsync(
                Path.Combine(Constants.UserRootDirectoryPath, "BuildSnapshots"), cancellationToken: cancellationToken);
            content = await PrepareContentAsync(snapshot.Configuration, status, cancellationToken);
            NetworkCacheContentSections sections = NetworkCacheBootConfiguration.BindContentSections(
                snapshot.Configuration, content.CustomImages, content.PostInstallation, content.CustomDrivers);
            outcome = await PublishContentAsync(snapshot, content, sections, minimumSequence: 0,
                (percent, detail) => operationProgress.Report(PreparationPercent + (percent * (100 - PreparationPercent) / 100), status, percent, detail),
                cancellationToken);
            publishResult = outcome.TelemetryResult;
            // Once the shares were processed, their results are the outcome, even when Cancel was selected meanwhile.
            FinishWithOutcome(outcome);
            log.Information("Network cache publication completed. Result={Result}, DurationMs={DurationMs}", outcome.TelemetryResult, stopwatch.ElapsedMilliseconds);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            operationProgress.Finish(OperationOutcome.Cancelled, Title("Cancelled"), localization.GetString("NetworkCache.NotPublishedMessage"));
            log.Information("Network cache publication cancelled. DurationMs={DurationMs}", stopwatch.ElapsedMilliseconds);
        }
        catch (Exception exception)
        {
            if (outcome.Results.Count == 0) publishResult = TelemetryNetworkCachePublishResults.Failed;
            operationProgress.Finish(OperationOutcome.Error, Title("Failed"), JoinParagraphs(
                localization.GetString("NetworkCache.PublishFailedMessage"),
                localization.FormatString("StartMedia.Operation.TechnicalDetailsFormat", exception.Message.Trim()),
                localization.GetString("StartMedia.Operation.SeeLog")), OpenLogFolderAction());
            log.Error(exception, "Network cache publication failed. DurationMs={DurationMs}", stopwatch.ElapsedMilliseconds);
        }
        finally
        {
            Release(content);
            try
            {
                snapshot?.Dispose();
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                log.Warning(exception, "Private publication sources could not be fully removed.");
            }

            await TrackAsync(operationId, publishResult, current.NetworkCache.Shares.Count);
            navigationGuard.SetState(adkService.CurrentStatus.CanCreateMedia ? ShellNavigationState.Ready : ShellNavigationState.AdkBlocked);
            operationProgress.Reset();
        }
    }

    /// <summary>Prepares the imported content with the calls media preparation uses; nothing of a WinPE workspace is built.</summary>
    private async Task<NetworkCacheContentLeases> PrepareContentAsync(FoundryConfigurationDocument document, string status, CancellationToken cancellationToken)
    {
        WinPeCustomImageMediaLease? customImages = null;
        WinPeContentPackageMediaLease? postInstallation = null;
        try
        {
            if (document.CustomImages.IsEnabled)
            {
                operationProgress.Report(0, status, null, localization.GetString("CustomImages.StageVerifying"));
                customImages = await new WinPeCustomImageMediaService().PrepareAsync(customImageLibrary, document.CustomImages, cancellationToken);
            }

            operationProgress.Report(PreparationPercent / 2, status, null, localization.GetString("StartMedia.Operation.PreparingPostInstallation"));
            postInstallation = await new WinPePreOobeMediaService().PrepareAsync(postInstallPackages, document.PreOobe, cancellationToken);
            WinPeContentPackageMediaLease? customDrivers = null;
            if (document.CustomDrivers.IsEnabled)
            {
                operationProgress.Report(PreparationPercent, status, null, localization.GetString("CustomDrivers.StageVerifying"));
                customDrivers = await WinPeCustomDriverSetMediaService.PrepareAsync(customDriverSetLibrary, document.CustomDrivers, cancellationToken);
            }

            return new NetworkCacheContentLeases(customImages, postInstallation, customDrivers);
        }
        catch
        {
            postInstallation?.Dispose();
            customImages?.Dispose();
            throw;
        }
    }

    /// <summary>Releases the leases in the reverse order of their preparation, each one even when an earlier release fails.</summary>
    private static void Release(NetworkCacheContentLeases? content)
    {
        if (content is null) return;
        try
        {
            content.CustomDrivers?.Dispose();
        }
        finally
        {
            try
            {
                content.PostInstallation.Dispose();
            }
            finally
            {
                content.CustomImages?.Dispose();
            }
        }
    }

    /// <summary>Every share published is a success; any share left without the latest changes is named, so the user retries.</summary>
    private void FinishWithOutcome(NetworkCachePublicationOutcome outcome)
    {
        IReadOnlyList<string> skipped = outcome.SkippedSharePaths;
        if (skipped.Count == 0)
        {
            operationProgress.Finish(OperationOutcome.Success, Title("Succeeded"), localization.GetString("NetworkCache.PublishSucceededMessage"));
            return;
        }

        operationProgress.Finish(OperationOutcome.Error, Title("Failed"), JoinParagraphs(
            localization.FormatString("NetworkCache.NotPublishedShareFormat", string.Join(", ", skipped)),
            localization.GetString("StartMedia.Operation.SeeLog")), OpenLogFolderAction());
    }

    private async Task TrackAsync(string operationId, string publishResult, int shareCount)
    {
        try
        {
            await telemetry.TrackAsync(TelemetryEvents.OsdNetworkCachePublished, new Dictionary<string, object?>
            {
                ["operation_id"] = operationId,
                ["network_cache_publish_result"] = publishResult,
                ["network_cache_share_count"] = Math.Clamp(shareCount, 0, 1000)
            }, CancellationToken.None);
        }
        catch (Exception exception)
        {
            // Telemetry never replaces the publication outcome.
            log.Debug(exception, "Network cache publication telemetry could not be queued.");
        }
    }

    private OperationAction OpenLogFolderAction() =>
        new(localization.GetString("StartMedia.Operation.OpenLogFolder"), () => _ = OpenLogFolderAsync());

    private async Task OpenLogFolderAsync()
    {
        try
        {
            await processLauncher.OpenFolderAsync(Path.GetDirectoryName(Constants.LogFilePath)!);
        }
        catch (Exception exception)
        {
            log.Warning(exception, "The log folder could not be opened.");
        }
    }

    private string Title(string state) => localization.GetString($"StartMedia.Operation.Title.NetworkCachePublish{state}");

    private static string JoinParagraphs(params string[] paragraphs) => string.Join(Environment.NewLine + Environment.NewLine, paragraphs);

    /// <summary>Publishes every prepared lease under one share content root with the media layout; each publisher writes its manifest last.</summary>
    private static async Task PublishLeasesAsync(NetworkCacheContentLeases content, string contentRoot, Func<long, IProgress<WinPeMediaProgress>> progressFrom,
        CancellationToken cancellationToken)
    {
        long offset = 0;
        if (content.CustomImages is { } customImages)
        {
            await new WinPeCustomImageMediaService().PublishToNetworkCacheAsync(customImages, contentRoot, cancellationToken, progressFrom(offset)).ConfigureAwait(false);
            offset += FileBytes(customImages.Files.Select(file => file.Length));
        }

        await new WinPeContentPackageMediaService(ContentPackageKind.PostInstallation)
            .PublishToNetworkCacheAsync(content.PostInstallation, contentRoot, cancellationToken, progressFrom(offset)).ConfigureAwait(false);
        offset += FileBytes(content.PostInstallation.Files.Select(file => file.Length));
        if (content.CustomDrivers is { } customDrivers)
        {
            await new WinPeContentPackageMediaService(ContentPackageKind.Drivers)
                .PublishToNetworkCacheAsync(customDrivers, contentRoot, cancellationToken, progressFrom(offset)).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Returns, for one share, the progress of each kind of content given the bytes of the kinds published before it. The
    /// percentage covers the publication across all the shares. Each progress forwards synchronously, on the copying thread, to
    /// <paramref name="relay"/>, which keeps the order of the reports and moves them to the UI thread.
    /// </summary>
    private static Func<long, IProgress<WinPeMediaProgress>> CreateShareProgress(NetworkCacheSettings settings, NetworkCacheContentLeases content,
        string contentRoot, ShareProgressRelay relay)
    {
        int shareCount = settings.Shares.Count;
        int shareIndex = Math.Max(0, settings.Shares.ToList().FindIndex(share =>
            string.Equals(NetworkCacheLayout.ContentRoot(share.Path), contentRoot, StringComparison.OrdinalIgnoreCase)));
        string sharePath = settings.Shares[shareIndex].Path;
        long shareBytes = FileBytes(content.CustomImages?.Files.Select(file => file.Length) ?? [])
            + FileBytes(content.PostInstallation.Files.Select(file => file.Length))
            + FileBytes(content.CustomDrivers?.Files.Select(file => file.Length) ?? []);
        return offset => new ContentProgress(value =>
        {
            long done = Math.Min(shareBytes, offset + (value.BytesCompleted ?? 0));
            double share = shareBytes == 0 ? value.Percent / 100.0 : (double)done / shareBytes;
            int percent = (int)Math.Clamp((shareIndex + share) * 100 / shareCount, 0, 100);
            relay.Report(new SharePublicationProgress(percent, sharePath, shareIndex, shareCount, done, shareBytes));
        });
    }

    private static long FileBytes(IEnumerable<long> lengths) => lengths.Aggregate(0L, (total, length) => checked(total + length));

    /// <summary>The progress of the publication on one share, as the copying thread computed it.</summary>
    private readonly record struct SharePublicationProgress(int Percent, string SharePath, int ShareIndex, int ShareCount, long BytesDone, long ShareBytes);

    /// <summary>Forwards each report synchronously, so the reports of one copy keep their order.</summary>
    private sealed class ContentProgress(Action<WinPeMediaProgress> handler) : IProgress<WinPeMediaProgress>
    {
        public void Report(WinPeMediaProgress value) => handler(value);
    }

    /// <summary>
    /// Moves share progress reported on pool threads to the thread that created the relay, the UI thread, in the order it was
    /// reported, and builds the detail line there. Reports that run after <see cref="Close"/> are dropped, so a late one never
    /// overwrites the state the caller shows once the publication returned.
    /// </summary>
    private sealed class ShareProgressRelay
    {
        private readonly IProgress<SharePublicationProgress> progress;
        private volatile bool isClosed;

        /// <summary>Creates the relay; it must be created on the UI thread, whose synchronization context receives the reports.</summary>
        public ShareProgressRelay(IApplicationLocalizationService localization, Action<int, string> report)
        {
            // Progress<T> captures the synchronization context of the thread that creates it and posts every report to it.
            progress = new Progress<SharePublicationProgress>(value =>
            {
                if (isClosed) return;
                string detail = localization.FormatString("StartMedia.Operation.PublishingToNetworkCacheShareFormat", value.SharePath, value.ShareIndex + 1, value.ShareCount);
                if (value.ShareBytes > 0)
                {
                    detail = localization.FormatString("StartMedia.Operation.DetailWithBytesFormat", detail,
                        localization.FormatString("StartMedia.Operation.ByteProgressFormat",
                            ByteSizeFormatter.Format(localization, (ulong)value.BytesDone), ByteSizeFormatter.Format(localization, (ulong)value.ShareBytes)));
                }

                report(value.Percent, detail);
            });
        }

        public void Report(SharePublicationProgress value)
        {
            if (!isClosed) progress.Report(value);
        }

        /// <summary>Drops every report from now on, including those already queued to the UI thread.</summary>
        public void Close() => isClosed = true;
    }
}
