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

/// <summary>Raised when a follower applied a newer publication of its reference ("Configuration updated").</summary>
/// <param name="ReferenceName">Display name of the reference PC.</param>
/// <param name="Sequence">Sequence of the applied publication.</param>
public sealed record ConfigurationUpdatedEventArgs(string ReferenceName, long Sequence);

/// <summary>
/// The WinUI host of <see cref="ConfigurationSharingEngine"/> (spec 4.2): it implements <see cref="ISharingHost"/> over the
/// configuration state service and the single vault writer, publishes a reference 5 seconds after the last configuration or password
/// change, checks a followed folder at startup and every 60 seconds, and raises the "Configuration updated" notification.
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

    /// <summary>Occurs on the UI thread when a follower applied a newer publication of its reference.</summary>
    public event EventHandler<ConfigurationUpdatedEventArgs>? ConfigurationUpdated;

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
            configuration.Replace(document, static () => { });
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

        configuration.SetManaged(managedBy);
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
    public async Task<SharingStatus> StartPublishingAsync(string folder, string password, bool includePasswords)
    {
        await RefreshReadinessAsync();
        SharingStatus status = await Engine.StartPublishingAsync(folder, password, includePasswords, CancellationToken.None);
        RecordPublish(status);
        RaiseChanged();
        return status;
    }

    /// <summary>"Take over": continues <paramref name="folder"/> with this PC's key after the current password opened it.</summary>
    public async Task<SharingStatus> TakeOverAsync(string folder, string currentPassword, bool includePasswords)
    {
        await RefreshReadinessAsync();
        SharingStatus status = await Engine.TakeOverAsync(folder, currentPassword, includePasswords, CancellationToken.None);
        RecordPublish(status);
        RaiseChanged();
        return status;
    }

    /// <summary>"Retry": publishes the current configuration now, even when it did not change.</summary>
    public async Task<SharingStatus> RetryPublishAsync()
    {
        await RefreshReadinessAsync();
        SharingStatus status = await Engine.OnConfigurationChangedAsync(CancellationToken.None);
        RecordPublish(status);
        return status;
    }

    /// <summary>"Change password": rewrites the current manifest under <paramref name="newPassword"/>.</summary>
    public async Task<SharingStatus> ChangePasswordAsync(string newPassword)
    {
        await RefreshReadinessAsync();
        SharingStatus status = await Engine.ChangePasswordAsync(newPassword, CancellationToken.None);
        RecordPublish(status);
        RaiseChanged();
        return status;
    }

    /// <summary>"Reset publication": new password and master key, content encrypted again.</summary>
    public async Task<SharingStatus> ResetPublicationAsync(string newPassword)
    {
        await RefreshReadinessAsync();
        SharingStatus status = await Engine.ResetPublicationAsync(newPassword, CancellationToken.None);
        RecordPublish(status);
        RaiseChanged();
        return status;
    }

    /// <summary>"Stop publishing": the folder stays as it is and this PC becomes a normal PC.</summary>
    public async Task StopPublishingAsync()
    {
        publishTimer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        await Engine.StopAsync();
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

    /// <summary>Follower: "Accept new reference".</summary>
    public async Task<SharingStatus> AcceptNewPublisherAsync()
    {
        SharingStatus status = await Engine.AcceptNewPublisherAsync(CancellationToken.None);
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
        if (isDisposed || isRefreshingReadiness || engine?.Role != SharingRole.Reference) return;
        SchedulePublish();
    }

    private void SchedulePublish() => publishTimer.Change(PublishDelay, Timeout.InfiniteTimeSpan);

    private void OnPublishTimerElapsed(object? state)
    {
        try
        {
            dispatcher.TryEnqueue(() =>
            {
                if (!isDisposed) _ = PublishIfChangedAsync();
            });
        }
        catch (Exception exception)
        {
            logger.Warning(exception, "Configuration publish could not be scheduled; it runs at the next change.");
        }
    }

    /// <summary>Publishes after a change, unless what would be published is what the last publish carried.</summary>
    private async Task PublishIfChangedAsync()
    {
        try
        {
            if (engine is not { Role: SharingRole.Reference } reference) return;
            string? fingerprint = Fingerprint(reference.State);
            if (fingerprint is not null && !IsPublishOwed(reference) && string.Equals(fingerprint, StoredFingerprint(), StringComparison.Ordinal))
            {
                return;
            }

            await RefreshReadinessAsync();
            SharingStatus status = await reference.OnConfigurationChangedAsync(CancellationToken.None);
            RecordPublish(status, fingerprint);

            // A change that arrived while the publish read its configuration is published by the next round.
            if (status == SharingStatus.Published && !string.Equals(Fingerprint(reference.State), fingerprint, StringComparison.Ordinal)) SchedulePublish();
        }
        catch (Exception exception)
        {
            // Nothing escapes the dispatcher callback: a failed publish is retried at the next change or Retry.
            logger.Warning(exception, "Automatic configuration publish failed; it is retried at the next change.");
        }
    }

    /// <summary>
    /// Refreshes the readiness the configuration state computes from library checks (driver sets, answer files), with its change
    /// events ignored, so the publish decision does not depend on whether a page refreshed it this session.
    /// </summary>
    private async Task RefreshReadinessAsync()
    {
        isRefreshingReadiness = true;
        try
        {
            await configuration.RefreshCustomDriversReadinessAsync();
            if (configuration.Current.Unattend.IsEnabled) await configuration.RefreshUnattendSourcesAsync();
        }
        finally
        {
            isRefreshingReadiness = false;
        }
    }

    /// <summary>Keeps the fingerprint of a successful publish in the vault, so an unchanged configuration is not published again.</summary>
    private void RecordPublish(SharingStatus status, string? fingerprint = null)
    {
        if (status != SharingStatus.Published || engine is not { Role: SharingRole.Reference } reference) return;
        fingerprint ??= Fingerprint(reference.State);
        if (fingerprint is null) return;
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

    private string? Fingerprint(SharingState state) => state.Folder is { Length: > 0 } folder
        ? PublicationFingerprint.Compute(folder, state.IncludePasswords, configuration.Current, vault.ReadEntries())
        : null;

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

    /// <summary>Runs one follower check and notifies when it applied a newer publication; a failed check is retried at the next interval.</summary>
    private async Task<SharingStatus> CheckAndNotifyAsync(CancellationToken cancellationToken)
    {
        if (engine is not { } follower) return SharingStatus.Idle;
        long before = follower.State.AppliedSequence;
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

        SharingState after = follower.State;
        if (after.Role == SharingRole.Follower && after.AppliedSequence > before && before > 0)
        {
            ConfigurationUpdated?.Invoke(this, new ConfigurationUpdatedEventArgs(after.PublisherName ?? string.Empty, after.AppliedSequence));
        }

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
            Changed?.Invoke(this, EventArgs.Empty);
        });
    }

    private void RaiseChanged()
    {
        if (isDisposed) return;
        dispatcher.TryEnqueue(() => Changed?.Invoke(this, EventArgs.Empty));
    }
}
