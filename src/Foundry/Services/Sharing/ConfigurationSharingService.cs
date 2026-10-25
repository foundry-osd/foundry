// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Security.Cryptography;
using Foundry.Core.Models.Configuration;
using Foundry.Core.Services.Application;
using Foundry.Core.Services.Configuration;
using Foundry.Core.Services.Drivers;
using Foundry.Core.Services.Files;
using Foundry.Core.Services.Images;
using Foundry.Core.Services.Packages;
using Foundry.Core.Services.Secrets;
using Foundry.Core.Services.Sharing;
using Foundry.Services.Autopilot;
using Foundry.Services.Configuration;
using Foundry.Services.Secrets;
using Serilog;

namespace Foundry.Services.Sharing;

/// <summary>
/// The WinUI host of <see cref="ConfigurationSharingEngine"/> (spec 4.2): it implements <see cref="ISharingHost"/> over the
/// configuration state service and the single vault writer, publishes a reference 5 seconds after the last configuration or password
/// change, and checks a followed folder at startup and every 60 seconds; the sharing card shows the outcome.
/// </summary>
/// <remarks>
/// Every member runs on the UI thread, which owns the configuration and the secret state services; the engine calls the host back on
/// the thread its methods are called on, and its background events are marshalled to the UI thread before <see cref="Changed"/> is
/// raised. A change that alters nothing published (a per-PC value, a readiness refresh, a restart) is not published again: the
/// fingerprint of the last publish (<see cref="PublicationFingerprint"/>) is kept in the vault. Passwords, keys and fingerprints are
/// never logged.
/// </remarks>
public sealed class ConfigurationSharingService : ISharingHost, IDisposable
{
    private static readonly TimeSpan PublishDelay = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan CheckInterval = TimeSpan.FromSeconds(60);

    private readonly IFoundryConfigurationStateService configuration;
    private readonly ISecretVaultBridge vault;
    private readonly AuthoringFileLibrary files;
    private readonly ContentPackageLibraryService packages;
    private readonly CustomDriverSetLibrary driverSets;
    private readonly CustomImageLibraryService images;
    private readonly ConfigurationReplacementGate replacementGate;
    private readonly IDeploymentProtectionSecretStateService protection;
    private readonly INetworkSecretStateService network;
    private readonly IOobeAccountSecretStateService oobe;
    private readonly IDomainJoinSecretStateService domainJoin;
    private readonly IAutopilotHardwareHashSessionState autopilot;
    private readonly IAppDispatcher dispatcher;
    private readonly ILogger logger;
    private readonly Timer publishTimer;
    private ContentSync? sync;
    private ConfigurationSharingEngine? engine;
    private CancellationTokenSource? followerChecks;
    private volatile ContentProgress? progress;
    private int isProgressUpdateQueued;
    private bool isRefreshingReadiness;
    private bool isSourceChangedDuringRefresh;
    private bool isNextRoundFollowUp;
    private bool wasContentDownloading;
    private bool isStarted;
    private bool isDisposed;

    public ConfigurationSharingService(
        IFoundryConfigurationStateService configuration,
        ISecretVaultBridge vault,
        AuthoringFileLibrary files,
        ContentPackageLibraryService packages,
        CustomDriverSetLibrary driverSets,
        CustomImageLibraryService images,
        ConfigurationReplacementGate replacementGate,
        IDeploymentProtectionSecretStateService protection,
        INetworkSecretStateService network,
        IOobeAccountSecretStateService oobe,
        IDomainJoinSecretStateService domainJoin,
        IAutopilotHardwareHashSessionState autopilot,
        IAppDispatcher dispatcher,
        ILogger logger)
    {
        this.configuration = configuration;
        this.vault = vault;
        this.files = files;
        this.packages = packages;
        this.driverSets = driverSets;
        this.images = images;
        this.replacementGate = replacementGate;
        this.protection = protection;
        this.network = network;
        this.oobe = oobe;
        this.domainJoin = domainJoin;
        this.autopilot = autopilot;
        this.dispatcher = dispatcher;
        this.logger = logger.ForContext<ConfigurationSharingService>();
        publishTimer = new Timer(OnPublishTimerElapsed);
    }

    /// <summary>Occurs on the UI thread when the role, the status, the content state or the content progress changes.</summary>
    public event EventHandler? Changed;

    /// <summary>Gets whether sharing could start: false when the sharing state file could not be read during this session.</summary>
    public bool IsAvailable => engine is not null;

    /// <summary>Gets the engine; check <see cref="IsAvailable"/> first.</summary>
    public ConfigurationSharingEngine Engine => engine ?? throw new InvalidOperationException("ConfigurationSharing.Unavailable");

    /// <summary>Gets the role of this PC; <see cref="SharingRole.None"/> while sharing is unavailable.</summary>
    public SharingRole Role => engine?.Role ?? SharingRole.None;

    /// <summary>Gets the last content copy progress of a publish or a fetch, or null when none ran this session.</summary>
    public ContentProgress? Progress => progress;

    /// <summary>
    /// Gets whether a follower is still fetching the content of the applied publication or waits for it, so media built now would
    /// miss it.
    /// </summary>
    public bool IsContentPending => engine is { Role: SharingRole.Follower } current
        && (current.IsContentFetching || current.Status == SharingStatus.ContentDownloading);

    /// <summary>
    /// Gets whether the content of the applied publication is still arriving on a follower: a fetch runs, or the next checks keep
    /// fetching it and none of it failed or stalled. Readiness then reports the missing content as downloading, and a media build may
    /// start: it waits until the content arrived before it freezes the configuration.
    /// </summary>
    public bool IsContentDownloading => engine?.IsContentArriving == true;

    /// <summary>
    /// Gets whether the shared content of the applied publication is complete on this PC. This is the single media creation gate:
    /// every media action (ISO, USB, USB without cache, PXE) is blocked until it is <see cref="SharedContentSyncState.Synced"/>, and
    /// <see cref="Changed"/> is raised when it changes, so callers re-evaluate without a restart.
    /// </summary>
    public SharedContentSyncState ContentSyncState => engine?.ContentSyncState ?? SharedContentSyncState.Synced;

    /// <inheritdoc />
    public FoundryConfigurationDocument CurrentConfiguration => configuration.Current;

    /// <inheritdoc />
    public bool IsReadyToBuild
    {
        get
        {
            NetworkMediaReadinessEvaluation networkReadiness = configuration.NetworkMediaReadiness;
            return configuration.IsDeployConfigurationReady
                && networkReadiness.IsNetworkConfigurationReady
                && networkReadiness.IsConnectProvisioningReady
                && networkReadiness.AreRequiredSecretsReady
                && configuration.IsDomainJoinConfigurationReady
                && (!configuration.IsAutopilotEnabled || configuration.IsAutopilotConfigurationReady);
        }
    }

    /// <inheritdoc />
    public bool IsVaultAvailable => vault.Status != VaultStatus.Unreadable;

    /// <inheritdoc />
    public IReadOnlyDictionary<string, string> ReadVault() => vault.ReadEntries();

    /// <inheritdoc />
    public void WriteVault(IReadOnlyDictionary<string, string> entries, IReadOnlyCollection<string>? removeIds = null) =>
        vault.WriteEntries(entries, removeIds);

    /// <inheritdoc />
    public async Task ApplyConfigurationAsync(FoundryConfigurationDocument document, IReadOnlyDictionary<string, string> vaultEntries, string managedBy,
        CancellationToken cancellationToken)
    {
        using (await replacementGate.EnterAsync(cancellationToken))
        {
            // Managed first: the pages reload on the replacement, and none may save a normalization over the applied publication.
            bool wasManaged = configuration.IsManaged;
            configuration.SetManaged(managedBy);
            try
            {
                configuration.Replace(document, static () => { });
            }
            catch
            {
                if (!wasManaged) configuration.ClearManaged();
                throw;
            }

            if (vaultEntries.Count > 0)
            {
                try
                {
                    vault.WriteEntries(vaultEntries);
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or CryptographicException)
                {
                    // The engine reports the passwords that are still missing.
                    logger.Warning(exception, "Published passwords could not be saved to the secret vault. SecretCount={SecretCount}", vaultEntries.Count);
                }
            }
        }

        await configuration.RefreshCustomDriversReadinessAsync();
    }

    /// <inheritdoc />
    public void ClearManaged() => configuration.ClearManaged();

    /// <inheritdoc />
    public bool AllContentAvailable(FoundryConfigurationDocument document)
    {
        // ContentSync applies the library availability checks to every item ListContents names, driver sets included.
        ConfigurationPackagePayload listed = new()
        {
            Kind = PackageKind.Publication,
            Configuration = document,
            Contents = PortableConfiguration.ListContents(document)
        };
        return sync is null || sync.Missing(listed).Count == 0;
    }

    /// <summary>
    /// Reads the sharing role and marks a follower's configuration managed. Call once on the UI thread, after the secret vault
    /// bridge is initialized and before any configuration page is shown; it never throws.
    /// </summary>
    public void Initialize()
    {
        if (engine is not null) return;
        string temporaryRoot = Constants.SharingTemporaryDirectoryPath;
        sync = new ContentSync(files, packages, driverSets, images, Path.Combine(temporaryRoot, "Fetch"));
        LocalContentSource source = new(files, packages, driverSets, images, Path.Combine(temporaryRoot, "Publish"));
        try
        {
            engine = new ConfigurationSharingEngine(this, Constants.SharingStatePath, source, sync, TimeProvider.System);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            logger.Error(exception, "Configuration sharing state could not be read; publishing and following are unavailable during this session.");
            return;
        }

        engine.StatusChanged += OnEngineStatusChanged;
        engine.ContentProgressChanged += OnEngineContentProgressChanged;
        if (engine.Role == SharingRole.Follower)
        {
            configuration.SetManaged(engine.State.PublisherName ?? string.Empty);
        }

        logger.Information("Configuration sharing initialized. Role={Role}", engine.Role);
    }

    /// <summary>
    /// Starts the automatic work of the role: a reference publishes what changed since its last publish; a follower checks its folder
    /// now and every 60 seconds. Call once on the UI thread after the main window is shown.
    /// </summary>
    public void Start()
    {
        if (isStarted || engine is null) return;
        isStarted = true;
        configuration.StateChanged += OnPublishedSourceChanged;
        protection.Changed += OnPublishedSourceChanged;
        network.Changed += OnPublishedSourceChanged;
        oobe.Changed += OnPublishedSourceChanged;
        domainJoin.Changed += OnPublishedSourceChanged;
        autopilot.BootMediaCertificatePasswordChanged += OnPublishedSourceChanged;
        if (engine.Role == SharingRole.Reference) SchedulePublish();
        else if (engine.Role == SharingRole.Follower) StartFollowerChecks();
    }

    /// <summary>"Publish to a folder": makes this PC the reference of <paramref name="folder"/> and publishes now.</summary>
    public Task<SharingStatus> StartPublishingAsync(string folder, string password, bool includePasswords) =>
        RunPublishActionAsync(() => Fingerprint(folder, includePasswords),
            () => Engine.StartPublishingAsync(folder, password, includePasswords, CancellationToken.None));

    /// <summary>"Take over": continues <paramref name="folder"/> with this PC's key after the current password opened it.</summary>
    public Task<SharingStatus> TakeOverAsync(string folder, string currentPassword, bool includePasswords) =>
        RunPublishActionAsync(() => Fingerprint(folder, includePasswords),
            () => Engine.TakeOverAsync(folder, currentPassword, includePasswords, CancellationToken.None));

    /// <summary>"Retry": publishes the current configuration now, even when it did not change.</summary>
    public Task<SharingStatus> RetryPublishAsync() =>
        RunPublishActionAsync(() => Fingerprint(Engine.State), () => Engine.OnConfigurationChangedAsync(CancellationToken.None));

    /// <summary>"Change password": rewrites the current manifest under <paramref name="newPassword"/>.</summary>
    public Task<SharingStatus> ChangePasswordAsync(string newPassword) =>
        RunPublishActionAsync(() => Fingerprint(Engine.State), () => Engine.ChangePasswordAsync(newPassword, CancellationToken.None));

    /// <summary>"Reset publication": new password and master key, content encrypted again.</summary>
    public Task<SharingStatus> ResetPublicationAsync(string newPassword) =>
        RunPublishActionAsync(() => Fingerprint(Engine.State), () => Engine.ResetPublicationAsync(newPassword, CancellationToken.None));

    /// <summary>
    /// "Stop publishing": the folder stays as it is and this PC becomes a normal PC. The fingerprint of the last publish is removed,
    /// so publishing again later decides from what the folder holds then.
    /// </summary>
    public async Task StopPublishingAsync()
    {
        publishTimer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        await Engine.StopAsync();
        try
        {
            vault.WriteEntries(new Dictionary<string, string>(StringComparer.Ordinal), new[] { SecretIds.PublishedFingerprint });
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or CryptographicException)
        {
            logger.Warning("The fingerprint of the last publish could not be removed. ExceptionType={ExceptionType}", exception.GetType().Name);
        }

        RaiseChanged();
    }

    /// <summary>Opens the current publication of <paramref name="folder"/> for the "Follow a folder" preview.</summary>
    public Task<PublicationReadResult> PreviewFollowAsync(string folder, string password) =>
        Engine.PreviewFollowAsync(folder, password, CancellationToken.None);

    /// <summary>Returns the bytes this PC must download for the previewed publication: the listed content its libraries lack.</summary>
    public long EstimateDownloadBytes(ConfigurationPackagePayload payload) =>
        sync?.Missing(payload).DistinctBy(content => content.Hash, StringComparer.OrdinalIgnoreCase).Sum(content => Math.Max(content.Length, 0)) ?? 0;

    /// <summary>Confirms "Follow a folder" and starts the periodic checks.</summary>
    public async Task<SharingStatus> FollowAsync(string folder, string password, PublicationReadResult preview)
    {
        SharingStatus status = await Engine.FollowAsync(folder, password, preview, CancellationToken.None);
        if (Engine.Role == SharingRole.Follower) StartFollowerChecks(checkNow: false);
        RaiseChanged();
        return status;
    }

    /// <summary>Follower: checks the folder now.</summary>
    public Task<SharingStatus> CheckNowAsync() => CheckAndNotifyAsync(CancellationToken.None);

    /// <summary>Follower: the publication password entered again.</summary>
    public async Task<SharingStatus> SetPublicationPasswordAsync(string password)
    {
        SharingStatus status = await Engine.SetPublicationPasswordAsync(password, CancellationToken.None);
        RaiseChanged();
        return status;
    }

    /// <summary>Follower: "Accept new reference" for the candidate key the user reviewed.</summary>
    public async Task<SharingStatus> AcceptNewPublisherAsync(string expectedKeyId)
    {
        SharingStatus status = await Engine.AcceptNewPublisherAsync(expectedKeyId, CancellationToken.None);
        RaiseChanged();
        return status;
    }

    /// <summary>"Stop following": stops the checks and makes the configuration editable again.</summary>
    public async Task StopFollowingAsync(bool forgetPassword)
    {
        StopFollowerChecks();
        await Engine.StopFollowingAsync(forgetPassword);
        RaiseChanged();
    }

    /// <summary>Completes when the running content fetch of a follower completes; at once without one.</summary>
    public Task WaitForContentAsync(CancellationToken cancellationToken) =>
        engine is null ? Task.CompletedTask : engine.WaitForContentAsync(cancellationToken);

    public void Dispose()
    {
        if (isDisposed) return;
        isDisposed = true;
        publishTimer.Dispose();
        StopFollowerChecks();
        if (isStarted)
        {
            configuration.StateChanged -= OnPublishedSourceChanged;
            protection.Changed -= OnPublishedSourceChanged;
            network.Changed -= OnPublishedSourceChanged;
            oobe.Changed -= OnPublishedSourceChanged;
            domainJoin.Changed -= OnPublishedSourceChanged;
            autopilot.BootMediaCertificatePasswordChanged -= OnPublishedSourceChanged;
        }

        if (engine is not null)
        {
            engine.StatusChanged -= OnEngineStatusChanged;
            engine.ContentProgressChanged -= OnEngineContentProgressChanged;
        }
    }

    private void OnPublishedSourceChanged(object? sender, EventArgs e)
    {
        if (isDisposed || engine?.Role != SharingRole.Reference) return;

        // The readiness refresh raises the same events; whatever arrives meanwhile gets one follow-up round once the round ends.
        if (isRefreshingReadiness)
        {
            isSourceChangedDuringRefresh = true;
            return;
        }

        SchedulePublish();
    }

    /// <summary>Starts the 5-second delay before the next publish round; a follow-up round never schedules another follow-up.</summary>
    private void SchedulePublish(bool isFollowUp = false)
    {
        isNextRoundFollowUp = isFollowUp;
        publishTimer.Change(PublishDelay, Timeout.InfiniteTimeSpan);
    }

    private void OnPublishTimerElapsed(object? state)
    {
        try
        {
            dispatcher.TryEnqueue(() =>
            {
                if (isDisposed) return;
                bool isFollowUp = isNextRoundFollowUp;
                isNextRoundFollowUp = false;
                _ = PublishIfChangedAsync(isFollowUp);
            });
        }
        catch (Exception exception)
        {
            logger.Warning(exception, "Configuration publish could not be scheduled; it runs at the next change.");
        }
    }

    /// <summary>Publishes after a change, unless what would be published is what the last publish carried.</summary>
    private async Task PublishIfChangedAsync(bool isFollowUp)
    {
        try
        {
            if (engine is not { Role: SharingRole.Reference } reference) return;
            string? before = Fingerprint(reference.State);
            if (before is not null && !IsPublishOwed(reference) && string.Equals(before, StoredFingerprint(), StringComparison.Ordinal))
            {
                return;
            }

            bool isChangedDuringRefresh = await RefreshReadinessAsync();

            // Taken after the refresh, just before the publish reads the configuration, so it describes what the publish carries.
            string? fingerprint = Fingerprint(reference.State);
            SharingStatus status = await reference.OnConfigurationChangedAsync(CancellationToken.None);
            CompletePublishRound(status, fingerprint);
            if (isChangedDuringRefresh && !isFollowUp) SchedulePublish(isFollowUp: true);
        }
        catch (Exception exception)
        {
            // Nothing escapes the dispatcher callback: a failed publish is retried at the next change or Retry.
            logger.Warning(exception, "Automatic configuration publish failed; it is retried at the next change.");
        }
    }

    /// <summary>
    /// Runs a publish the user asked for: readiness first, then the fingerprint of what the action publishes, taken just before it
    /// runs, so an edit made during the publish is never recorded as published.
    /// </summary>
    private async Task<SharingStatus> RunPublishActionAsync(Func<string?> fingerprintOf, Func<Task<SharingStatus>> action)
    {
        bool isChangedDuringRefresh = await RefreshReadinessAsync();
        string? fingerprint = fingerprintOf();
        SharingStatus status = await action();
        CompletePublishRound(status, fingerprint);
        if (isChangedDuringRefresh && engine?.Role == SharingRole.Reference) SchedulePublish(isFollowUp: true);
        RaiseChanged();
        return status;
    }

    /// <summary>
    /// Records a successful publish under the fingerprint taken before it, and schedules a new round when the configuration or its
    /// passwords changed while the publish ran.
    /// </summary>
    private void CompletePublishRound(SharingStatus status, string? fingerprint)
    {
        if (fingerprint is null || engine is not { Role: SharingRole.Reference } reference) return;
        if (status == SharingStatus.Published) RecordPublish(fingerprint);
        if (!string.Equals(Fingerprint(reference.State), fingerprint, StringComparison.Ordinal)) SchedulePublish();
    }

    /// <summary>
    /// Refreshes the readiness the configuration state computes from library checks (driver sets, answer files), so the publish
    /// decision does not depend on whether a page refreshed it this session. Returns whether a configuration or password change
    /// event arrived meanwhile (the refresh raises them too, so they cannot be told apart).
    /// </summary>
    private async Task<bool> RefreshReadinessAsync()
    {
        isRefreshingReadiness = true;
        isSourceChangedDuringRefresh = false;
        try
        {
            await configuration.RefreshCustomDriversReadinessAsync();
            if (configuration.Current.Unattend.IsEnabled) await configuration.RefreshUnattendSourcesAsync();
            return isSourceChangedDuringRefresh;
        }
        finally
        {
            isRefreshingReadiness = false;
        }
    }

    /// <summary>Keeps the fingerprint of a successful publish in the vault, so an unchanged configuration is not published again.</summary>
    private void RecordPublish(string fingerprint)
    {
        try
        {
            vault.WriteEntries(new Dictionary<string, string>(StringComparer.Ordinal) { [SecretIds.PublishedFingerprint] = fingerprint });
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or CryptographicException)
        {
            // Without it the next launch publishes the same configuration once more; nothing else depends on it.
            logger.Warning("The fingerprint of the last publish could not be saved. ExceptionType={ExceptionType}", exception.GetType().Name);
        }
    }

    private string? Fingerprint(SharingState state) => state.Folder is { Length: > 0 } folder ? Fingerprint(folder, state.IncludePasswords) : null;

    private string Fingerprint(string folder, bool includePasswords) =>
        PublicationFingerprint.Compute(folder, includePasswords, configuration.Current, vault.ReadEntries());

    private string? StoredFingerprint() => vault.ReadEntries().GetValueOrDefault(SecretIds.PublishedFingerprint);

    /// <summary>
    /// Whether a publish is due whatever the configuration: the last one did not succeed, a reset still has to encrypt the content
    /// again, or a password change still has to rewrite the manifest.
    /// </summary>
    private bool IsPublishOwed(ConfigurationSharingEngine reference) =>
        reference.Status is not (SharingStatus.Published or SharingStatus.Idle)
        || reference.RevocationPending
        || vault.ReadEntries().ContainsKey(SecretIds.PreviousPublicationPassword);

    private void StartFollowerChecks(bool checkNow = true)
    {
        StopFollowerChecks();
        followerChecks = new CancellationTokenSource();
        _ = RunFollowerChecksAsync(checkNow, followerChecks.Token);
    }

    private void StopFollowerChecks()
    {
        followerChecks?.Cancel();
        followerChecks?.Dispose();
        followerChecks = null;
    }

    /// <summary>Checks at once (startup) and then every 60 seconds on the UI thread, until stopped.</summary>
    private async Task RunFollowerChecksAsync(bool checkNow, CancellationToken cancellationToken)
    {
        try
        {
            if (checkNow) await CheckAndNotifyAsync(cancellationToken);
            using PeriodicTimer timer = new(CheckInterval);
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                if (engine?.Role != SharingRole.Follower) return;
                await CheckAndNotifyAsync(cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    /// <summary>Runs one follower check and raises <see cref="Changed"/>; a failed check is retried at the next interval.</summary>
    private async Task<SharingStatus> CheckAndNotifyAsync(CancellationToken cancellationToken)
    {
        if (engine is not { } follower) return SharingStatus.Idle;
        SharingStatus status;
        try
        {
            status = await follower.CheckAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.Warning(exception, "Configuration check of the followed folder failed; it is retried at the next interval.");
            return follower.Status;
        }

        // A fetch can end without a status change; the check notices it.
        NotifyChanged();

        return status;
    }

    private void OnEngineStatusChanged(object? sender, EventArgs e) => RaiseChanged();

    private void OnEngineContentProgressChanged(object? sender, ContentProgress value)
    {
        // Raised on the fetch or publish thread: at most one UI update is queued at a time.
        progress = value;
        if (Interlocked.Exchange(ref isProgressUpdateQueued, 1) == 1) return;
        dispatcher.TryEnqueue(() =>
        {
            Volatile.Write(ref isProgressUpdateQueued, 0);
            NotifyChanged();
        });
    }

    private void RaiseChanged()
    {
        if (isDisposed) return;
        dispatcher.TryEnqueue(NotifyChanged);
    }

    /// <summary>
    /// Raises <see cref="Changed"/> on the UI thread. When a follower's download ends, the library readiness the configuration state
    /// caches is refreshed, so pages and media creation see the fetched content, or the content that failed, at once.
    /// </summary>
    private void NotifyChanged()
    {
        if (isDisposed) return;
        bool isDownloading = IsContentDownloading;
        if (wasContentDownloading && !isDownloading) _ = RefreshLibraryReadinessAsync();
        wasContentDownloading = isDownloading;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private async Task RefreshLibraryReadinessAsync()
    {
        try
        {
            configuration.RefreshPostInstallationReadiness();
            configuration.RefreshCustomImageReadiness();
            await configuration.RefreshCustomDriversReadinessAsync();
        }
        catch (Exception exception)
        {
            logger.Warning(exception, "Library readiness could not be refreshed after the shared content download.");
        }
    }
}
