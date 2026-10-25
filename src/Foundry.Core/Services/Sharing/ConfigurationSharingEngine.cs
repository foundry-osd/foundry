// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Security.Cryptography;
using Foundry.Core.Models.Configuration;
using Foundry.Core.Services.Secrets;
using Serilog;

namespace Foundry.Core.Services.Sharing;

/// <summary>
/// The configuration sharing state machine: the role of this PC and every decision about publishing,
/// following and what to report. The host (<see cref="ISharingHost"/>) owns timers, debounce, dispatching and notifications.
/// </summary>
/// <remarks>
/// <para>
/// Reference: the signing key, the content master key and the publication password live in the vault under
/// <see cref="SecretIds.SigningKey"/>, <see cref="SecretIds.MasterKey"/> and <see cref="SecretIds.PublicationPassword"/>. Only
/// <see cref="StartPublishingAsync"/> and <see cref="TakeOverAsync"/>, which the user starts, create keys: an automatic publish whose
/// keys are missing, or that runs while the vault is unavailable, reports <see cref="SharingStatus.PasswordsNeeded"/> and never creates
/// a new identity. <see cref="SharingState.LastPublishedSequence"/> is persisted after every publish and passed in every
/// <see cref="PublicationRequest"/>.
/// </para>
/// <para>
/// Operations that use the folder run one at a time. Publishes triggered by changes are coalesced: a change while a publish runs
/// queues exactly one follow-up publish, which reads the configuration again when it starts; each publish activates the configuration
/// it read when it started, with that configuration's content.
/// </para>
/// <para>
/// Follower: the reference key is pinned when the user follows, and every check verifies the current manifest against it. An applied
/// publication replaces the configuration through the host (per-PC values kept, the <c>CustomDrivers</c> section and default set
/// from the publication), its passwords overwrite the matching vault entries (sharing ids never), and its missing content is fetched
/// in the background. A key change is never trusted from the folder alone: the candidate key is the one the current manifest
/// authenticates under with the publication password, and it applies only after <see cref="AcceptNewPublisherAsync"/>.
/// </para>
/// <para>Nothing here logs a secret, password or key; only sequences, counts and typed statuses.</para>
/// </remarks>
public sealed class ConfigurationSharingEngine
{
    /// <summary>
    /// Consecutive fetches that find the same content still not available before <see cref="IsContentStalled"/> is set: ten checks,
    /// about ten minutes at the 60-second check interval, longer than a cloud folder needs to deliver a file or a reset needs to
    /// encrypt content again, so content that stays unavailable is most likely damaged in the folder.
    /// </summary>
    internal const int StalledContentFetchCount = 10;

    /// <summary>How often <see cref="WaitForContentAsync"/> looks again between fetch attempts.</summary>
    internal static readonly TimeSpan ContentWaitInterval = TimeSpan.FromMilliseconds(500);

    private const int MasterKeySize = 32;

    private readonly ISharingHost host;
    private readonly string statePath;
    private readonly IContentSource contents;
    private readonly ContentSync sync;
    private readonly TimeProvider time;
    private readonly SemaphoreSlim operation = new(1, 1);
    private readonly Lock gate = new();
    private SharingState state;
    private SharingStatus status = SharingStatus.Idle;
    private DateTimeOffset? lastSuccessUtc;
    private CancellationTokenSource roleCancellation = new();
    private TaskCompletionSource<SharingStatus>? activePublish;
    private bool publishPending;

    /// <summary>The publication password entered this session when the vault could not store it; it wins over the vault.</summary>
    private string? sessionPassword;

    /// <summary>Follower: the payload applied last, known once a check opened it during this session.</summary>
    private ConfigurationPackagePayload? appliedPayload;

    private Task fetchTask = Task.CompletedTask;
    private CancellationTokenSource? fetchCancellation;

    /// <summary>Whether a fetch is downloading; cleared before its outcome is reported, unlike the completion of <see cref="fetchTask"/>.</summary>
    private bool isFetching;

    /// <summary>Whether the next check fetches again: content was not available yet, as opposed to failing the same way every time.</summary>
    private bool retryFetch;

    private IReadOnlyList<PackageContent> failedContent = [];
    private HashSet<(SharedContentKind Kind, string Hash)>? unavailableContent;
    private int unchangedFetchCount;
    private bool contentStalled;

    /// <summary>Sequence of the manifest the previous check read as modified; a second read of it is reported.</summary>
    private long? tamperedSequence;

    /// <summary>Whether the running check read a modified manifest; otherwise it clears <see cref="tamperedSequence"/> when it ends.</summary>
    private bool tamperedThisCheck;

    private SharingPublisherCandidate? candidate;
    private SharingForeignPublication? foreignPublication;

    /// <summary>Creates the engine and reads the persisted role.</summary>
    /// <param name="host">The application side.</param>
    /// <param name="statePath">Path of <c>Configuration\sharing.json</c>.</param>
    /// <param name="contents">Opens local library content for publishes.</param>
    /// <param name="sync">Fetches published content into the local libraries on a follower.</param>
    /// <param name="time">Clock of <see cref="LastSuccessUtc"/>.</param>
    /// <exception cref="IOException">The state file exists but cannot be read.</exception>
    /// <exception cref="UnauthorizedAccessException">Access to the state file is denied.</exception>
    public ConfigurationSharingEngine(ISharingHost host, string statePath, IContentSource contents, ContentSync sync, TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentException.ThrowIfNullOrWhiteSpace(statePath);
        ArgumentNullException.ThrowIfNull(contents);
        ArgumentNullException.ThrowIfNull(sync);
        ArgumentNullException.ThrowIfNull(time);
        this.host = host;
        this.statePath = Path.GetFullPath(statePath);
        this.contents = contents;
        this.sync = sync;
        this.time = time;
        state = SharingStateStore.Load(this.statePath);
    }

    /// <summary>Raised when <see cref="Status"/> changes; may be raised on a background thread.</summary>
    public event EventHandler? StatusChanged;

    /// <summary>Raised with the content copy progress of a publish or a fetch; may be raised on a background thread.</summary>
    public event EventHandler<ContentProgress>? ContentProgressChanged;

    /// <summary>Gets the role of this PC.</summary>
    public SharingRole Role => State.Role;

    /// <summary>Gets the persisted sharing state.</summary>
    public SharingState State
    {
        get
        {
            lock (gate)
            {
                return state;
            }
        }
    }

    /// <summary>Gets the last reported status.</summary>
    public SharingStatus Status
    {
        get
        {
            lock (gate)
            {
                return status;
            }
        }
    }

    /// <summary>Gets when the last publish or check succeeded during this session.</summary>
    public DateTimeOffset? LastSuccessUtc
    {
        get
        {
            lock (gate)
            {
                return lastSuccessUtc;
            }
        }
    }

    /// <summary>Gets the key id of the pinned reference key of a follower, for "Accept new reference"; null otherwise.</summary>
    public string? PinnedKeyId => PinnedKeyIdOf(State);

    /// <summary>
    /// Gets the new reference a follower found (<see cref="SharingStatus.UnknownPublisher"/>), authenticated by the current manifest
    /// with the publication password; null when there is none.
    /// </summary>
    public SharingPublisherCandidate? Candidate
    {
        get
        {
            lock (gate)
            {
                return candidate;
            }
        }
    }

    /// <summary>
    /// Gets the publication of another key that made the last publish of this PC report <see cref="SharingStatus.UnknownPublisher"/>,
    /// for the "Take over" confirmation; null after a successful publish or when publishing stops. Its key id is read without the
    /// password and is not authenticated.
    /// </summary>
    public SharingForeignPublication? ForeignPublication
    {
        get
        {
            lock (gate)
            {
                return foreignPublication;
            }
        }
    }

    /// <summary>
    /// Gets the content of the applied publication that decrypted but could not be imported (it breaks its library's rules or does
    /// not match its hash); it is fetched again only when the reference publishes a new version.
    /// </summary>
    public IReadOnlyList<PackageContent> FailedContent
    {
        get
        {
            lock (gate)
            {
                return failedContent;
            }
        }
    }

    /// <summary>
    /// Gets whether the same content stayed not available for <see cref="StalledContentFetchCount"/> consecutive checks: it is
    /// probably damaged in the folder rather than still arriving. Status stays <see cref="SharingStatus.ContentDownloading"/> and
    /// the checks keep retrying.
    /// </summary>
    public bool IsContentStalled
    {
        get
        {
            lock (gate)
            {
                return contentStalled;
            }
        }
    }

    /// <summary>
    /// Gets whether a background content fetch is running. <see cref="SharingStatus.ContentDownloading"/> without a running fetch
    /// means the content is waiting for the next check (<see cref="IsContentStalled"/> when it stays unavailable) or, with
    /// <see cref="FailedContent"/> not empty, could not be imported: no download is active.
    /// </summary>
    public bool IsContentFetching
    {
        get
        {
            lock (gate)
            {
                return isFetching;
            }
        }
    }

    /// <summary>
    /// Gets whether a "Reset publication" is not complete: the new master key is stored but no publish has encrypted the content
    /// with it yet, so former password holders can still decrypt the content files. Persisted, so it survives a restart; cleared
    /// by the next successful publish.
    /// </summary>
    public bool RevocationPending => State.RevocationPending;

    private static ILogger Logger => Log.ForContext<ConfigurationSharingEngine>();

    /// <summary>
    /// "Publish to a folder": makes this PC the reference of <paramref name="folder"/> and publishes the current
    /// configuration. Missing reference keys are created and stored in the vault first. The folder is accepted, and the role
    /// persisted, when the publish succeeds or when the configuration is not ready yet (<see cref="SharingStatus.NotPublishedIncomplete"/>,
    /// published at the next change); otherwise nothing is persisted and the role stays <see cref="SharingRole.None"/>.
    /// </summary>
    /// <returns>
    /// <see cref="SharingStatus.Published"/>, <see cref="SharingStatus.NotPublishedIncomplete"/>,
    /// <see cref="SharingStatus.UnknownPublisher"/> (another key publishes there: offer "Take over"),
    /// <see cref="SharingStatus.FolderNotEmpty"/>, <see cref="SharingStatus.FolderUnavailable"/>, <see cref="SharingStatus.WrongPassword"/>
    /// (this PC published there with another password), or <see cref="SharingStatus.PasswordsNeeded"/> when the vault is unavailable.
    /// </returns>
    /// <exception cref="ArgumentException">The folder is empty or the password breaks <see cref="PassphrasePolicy"/>.</exception>
    /// <exception cref="InvalidOperationException">This PC already publishes or follows.</exception>
    public async Task<SharingStatus> StartPublishingAsync(string folder, string password, bool includePasswords, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folder);
        ValidatePassword(password);
        await operation.WaitAsync(ct);
        try
        {
            if (State.Role != SharingRole.None)
            {
                throw new InvalidOperationException("ConfigurationSharing.RoleActive");
            }

            if (!host.IsVaultAvailable)
            {
                return SetStatus(SharingStatus.PasswordsNeeded);
            }

            SharingState target = NewReferenceState(folder, includePasswords);
            SharingStatus result = await PublishWithPreparedKeysAsync(target, password, takeOver: false, ct);
            if (result == SharingStatus.NotPublishedIncomplete)
            {
                Adopt(target, password);
            }

            return result;
        }
        finally
        {
            operation.Release();
        }
    }

    /// <summary>
    /// "Take over": continues a folder another key publishes to, with this PC's key, after the user entered the current
    /// publication password; followers must accept the new reference once. Also the recovery of a reference whose keys were lost
    /// with the vault: it is the only operation that creates replacement keys while the vault is unreadable, because the user asked
    /// for a new identity (a vault that cannot be written fails it with <see cref="SharingStatus.PasswordsNeeded"/>). The role is
    /// persisted only when the publish succeeds.
    /// </summary>
    /// <returns>
    /// <see cref="SharingStatus.Published"/>; <see cref="SharingStatus.WrongPassword"/> when the password does not open the foreign
    /// manifest; otherwise the statuses of <see cref="StartPublishingAsync"/>.
    /// </returns>
    /// <exception cref="ArgumentException">The folder is empty or the password breaks <see cref="PassphrasePolicy"/>.</exception>
    /// <exception cref="InvalidOperationException">This PC follows a folder.</exception>
    public async Task<SharingStatus> TakeOverAsync(string folder, string currentPassword, bool includePasswords, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folder);
        ValidatePassword(currentPassword);
        await operation.WaitAsync(ct);
        try
        {
            SharingState current = State;
            if (current.Role == SharingRole.Follower)
            {
                throw new InvalidOperationException("ConfigurationSharing.RoleActive");
            }

            SharingState target = current.Role == SharingRole.Reference
                ? current with { Folder = folder, IncludePasswords = includePasswords }
                : NewReferenceState(folder, includePasswords);
            Logger.Information("Publication take over started.");
            return await PublishWithPreparedKeysAsync(target, currentPassword, takeOver: true, ct);
        }
        finally
        {
            operation.Release();
        }
    }

    /// <summary>
    /// Publishes the current configuration after a change (the host debounces changes) or a "Publish now". Does nothing without the
    /// reference role. A change while a publish runs queues exactly one follow-up publish; every caller waiting on the same run gets
    /// the status of its last publish. A password change or reset that was interrupted is completed first.
    /// </summary>
    /// <param name="ct">Cancels this caller's wait only; the run itself goes on, and only <see cref="StopAsync"/> cancels it.</param>
    /// <returns>
    /// The current status without the reference role; otherwise <see cref="SharingStatus.Published"/>,
    /// <see cref="SharingStatus.NotPublishedIncomplete"/>, <see cref="SharingStatus.PasswordsNeeded"/> (keys or password missing from
    /// the vault, or the vault unavailable), <see cref="SharingStatus.UnknownPublisher"/> (another PC took the folder over),
    /// <see cref="SharingStatus.WrongPassword"/>, <see cref="SharingStatus.FolderNotEmpty"/>, <see cref="SharingStatus.FolderUnavailable"/>,
    /// or <see cref="SharingStatus.Idle"/> when publishing stopped meanwhile.
    /// </returns>
    public Task<SharingStatus> OnConfigurationChangedAsync(CancellationToken ct)
    {
        TaskCompletionSource<SharingStatus> run;
        lock (gate)
        {
            if (activePublish is not null)
            {
                publishPending = true;
                return activePublish.Task.WaitAsync(ct);
            }

            run = activePublish = new TaskCompletionSource<SharingStatus>(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        _ = RunPublishesAsync(run);
        return run.Task.WaitAsync(ct);
    }

    /// <summary>
    /// "Change password": rewrites the current manifest under <paramref name="newPassword"/> in place; content is
    /// untouched, so a former password holder who kept the master key can still decrypt content files. The new password is stored
    /// first, with the old one kept as <see cref="SecretIds.PreviousPublicationPassword"/> until the manifest is rewritten, so an
    /// interrupted change is completed by the next publish.
    /// </summary>
    /// <returns><see cref="SharingStatus.Published"/>, <see cref="SharingStatus.PasswordsNeeded"/>, or the folder failure.</returns>
    /// <exception cref="ArgumentException">The new password breaks <see cref="PassphrasePolicy"/>.</exception>
    /// <exception cref="InvalidOperationException">This PC is not a reference.</exception>
    public Task<SharingStatus> ChangePasswordAsync(string newPassword, CancellationToken ct) => ReplacePasswordAsync(newPassword, reset: false, ct);

    /// <summary>
    /// "Reset publication", the complete revocation: rewrites the current manifest under
    /// <paramref name="newPassword"/> (<see cref="PublicationWriter.RewrapAsync"/>), then publishes with a new master key, which
    /// encrypts every content file again; followers enter the new password. The new password and master key are stored before the
    /// folder changes, so an interrupted reset is completed by the next change or Retry. While content is encrypted again, followers
    /// read it as not yet available.
    /// </summary>
    /// <returns>
    /// The status of the publish, or of the rewrite when it fails. Any result other than <see cref="SharingStatus.Published"/> means
    /// the revocation is not complete yet: <see cref="RevocationPending"/> stays set, also across restarts, until a publish succeeds.
    /// </returns>
    /// <exception cref="ArgumentException">The new password breaks <see cref="PassphrasePolicy"/>.</exception>
    /// <exception cref="InvalidOperationException">This PC is not a reference.</exception>
    public Task<SharingStatus> ResetPublicationAsync(string newPassword, CancellationToken ct) => ReplacePasswordAsync(newPassword, reset: true, ct);

    /// <summary>
    /// "Stop publishing": cancels a running publish, which leaves the previous version current, and makes this PC a
    /// normal PC. The folder and the vault keys stay as they are; <see cref="SharingState.LastPublishedSequence"/>, the folder name
    /// and <see cref="SharingState.RevocationPending"/> are kept, so publishing to the same folder again still owes a pending reset.
    /// A pending password change is dropped with its previous password.
    /// </summary>
    public async Task StopAsync()
    {
        CancellationTokenSource stopping;
        lock (gate)
        {
            if (state.Role != SharingRole.Reference)
            {
                return;
            }

            stopping = roleCancellation;
        }

        // Cancelled before waiting: a running publish holds the semaphore until it sees the cancellation.
        await stopping.CancelAsync();
        await operation.WaitAsync();
        try
        {
            SharingState current = State;
            if (current.Role == SharingRole.Reference)
            {
                TryWriteVault(new Dictionary<string, string>(StringComparer.Ordinal), [SecretIds.PreviousPublicationPassword]);
                // The folder is kept: publishing there again carries a pending revocation over (see NewReferenceState).
                Persist(current with { Role = SharingRole.None });
            }

            lock (gate)
            {
                // The cancelled source is replaced, never disposed: an overlapping stop may still cancel it.
                roleCancellation = new CancellationTokenSource();
                foreignPublication = null;
            }

            SetStatus(SharingStatus.Idle);
        }
        finally
        {
            operation.Release();
        }
    }

    /// <summary>
    /// "Follow a folder" preview: opens the current publication with <paramref name="password"/>, verified against the
    /// publisher key it embeds, for the reference name, date, sequence, passwords and download size. Nothing is stored.
    /// </summary>
    public async Task<PublicationReadResult> PreviewFollowAsync(string folder, string password, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folder);
        ArgumentNullException.ThrowIfNull(password);
        PublicationReadResult result = await PublicationReader.PreviewAsync(folder, password, ct);
        Logger.Information("Publication opened for a follow preview. Status={Status}, Sequence={Sequence}", result.Status, result.Sequence);
        return result;
    }

    /// <summary>
    /// Confirms "Follow a folder": stores the publication password, pins the reference key of <paramref name="preview"/> and applies
    /// its publication, which replaces the configuration (per-PC values kept). Missing content is then fetched in the background.
    /// </summary>
    /// <param name="folder">The folder <paramref name="preview"/> was read from.</param>
    /// <param name="password">The publication password the preview opened with.</param>
    /// <param name="preview">An <see cref="PublicationReadStatus.Ok"/> result of <see cref="PreviewFollowAsync"/>.</param>
    /// <param name="ct">Cancels the apply.</param>
    /// <returns>
    /// <see cref="SharingStatus.Updated"/>, <see cref="SharingStatus.ContentDownloading"/>, or <see cref="SharingStatus.PasswordsNeeded"/>
    /// when the configuration needs passwords the publication does not include.
    /// </returns>
    /// <exception cref="ArgumentException">The preview is not an opened, signed publication.</exception>
    /// <exception cref="InvalidOperationException">This PC already publishes or follows.</exception>
    public async Task<SharingStatus> FollowAsync(string folder, string password, PublicationReadResult preview, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folder);
        ArgumentNullException.ThrowIfNull(password);
        ArgumentNullException.ThrowIfNull(preview);
        if (preview is not { Status: PublicationReadStatus.Ok, Payload: { Publisher: { } publisher } payload }
            || preview.SignerKeyId != PublicationSigner.KeyId(publisher.PublicKey))
        {
            throw new ArgumentException("The follow preview is not an opened publication.", nameof(preview));
        }

        await operation.WaitAsync(ct);
        try
        {
            SharingState current = State;
            if (current.Role != SharingRole.None)
            {
                throw new InvalidOperationException("ConfigurationSharing.RoleActive");
            }

            StorePublicationPassword(password);
            SharingState target = new()
            {
                Role = SharingRole.Follower,
                Folder = folder,
                PinnedPublicKey = publisher.PublicKey,
                LastPublishedSequence = current.LastPublishedSequence
            };
            return await ApplyAsync(payload, target, updated: true, ct);
        }
        finally
        {
            operation.Release();
        }
    }

    /// <summary>
    /// Follower check, at startup and every 60 seconds: reads <c>current.json</c> and applies a newer publication signed by
    /// the pinned key. A manifest that has not arrived reports <see cref="SharingStatus.Checking"/>; one read as modified is retried
    /// once (a partly synced manifest reads the same) before <see cref="SharingStatus.Tampered"/> is reported; a manifest signed by
    /// another key reports <see cref="SharingStatus.UnknownPublisher"/> and applies nothing. Content still missing is fetched again in
    /// the background. Does nothing without the follower role.
    /// </summary>
    /// <returns>
    /// The current status without the follower role; otherwise <see cref="SharingStatus.UpToDate"/>, <see cref="SharingStatus.Updated"/>,
    /// <see cref="SharingStatus.ContentDownloading"/>, <see cref="SharingStatus.PasswordsNeeded"/>, <see cref="SharingStatus.Checking"/>,
    /// <see cref="SharingStatus.Tampered"/>, <see cref="SharingStatus.UnknownPublisher"/>, <see cref="SharingStatus.RollbackRejected"/>,
    /// <see cref="SharingStatus.WrongPassword"/>, <see cref="SharingStatus.FolderUnavailable"/> or <see cref="SharingStatus.Unsupported"/>.
    /// </returns>
    /// <exception cref="IOException">The host cannot save the configuration; the applied sequence is kept and the next check retries.</exception>
    public async Task<SharingStatus> CheckAsync(CancellationToken ct)
    {
        await operation.WaitAsync(ct);
        try
        {
            if (State.Role != SharingRole.Follower)
            {
                return Status;
            }

            if (PublicationPassword(host.ReadVault()) is not { } password)
            {
                return SetStatus(SharingStatus.PasswordsNeeded);
            }

            // After a restart the applied payload is unknown; when content is missing its manifest is opened again to fetch it.
            bool reopen = AppliedPayload is null && !host.AllContentAvailable(host.CurrentConfiguration);
            return await CheckCoreAsync(password, reopen, reapply: false, ct);
        }
        finally
        {
            operation.Release();
        }
    }

    /// <summary>
    /// Follower: the publication password entered again (after <see cref="SharingStatus.PasswordsNeeded"/> or
    /// <see cref="SharingStatus.WrongPassword"/>). The current publication is read with it and applied again, which also restores the
    /// passwords it includes; the password is stored only when it opens the publication.
    /// </summary>
    /// <returns>The status of the check made with the password; <see cref="SharingStatus.WrongPassword"/> leaves nothing stored.</returns>
    /// <exception cref="InvalidOperationException">This PC does not follow a folder.</exception>
    public async Task<SharingStatus> SetPublicationPasswordAsync(string password, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(password);
        await operation.WaitAsync(ct);
        try
        {
            if (State.Role != SharingRole.Follower)
            {
                throw new InvalidOperationException("ConfigurationSharing.NotFollower");
            }

            string? previous = sessionPassword;
            sessionPassword = password;
            SharingStatus result;
            try
            {
                result = await CheckCoreAsync(password, reopen: true, reapply: true, ct);
            }
            catch
            {
                sessionPassword = previous;
                throw;
            }

            bool opened = result is SharingStatus.Updated or SharingStatus.UpToDate or SharingStatus.ContentDownloading or SharingStatus.PasswordsNeeded
                || (result == SharingStatus.UnknownPublisher && Candidate is not null);
            if (opened)
            {
                StorePublicationPassword(password);
            }
            else
            {
                sessionPassword = previous;
            }

            return result;
        }
        finally
        {
            operation.Release();
        }
    }

    /// <summary>
    /// "Accept new reference": pins the key of <see cref="Candidate"/> and applies the current publication, only while the
    /// candidate is still the key the user reviewed: a check can replace the candidate while the confirmation is shown, and then
    /// nothing is pinned. The folder is read again; when its manifest is no longer the one authenticated under the candidate key,
    /// nothing is pinned and the result of a new check is returned (a different candidate must be confirmed again).
    /// </summary>
    /// <param name="expectedKeyId">Key id of the candidate the user confirmed.</param>
    /// <param name="ct">Cancels the wait and the apply.</param>
    /// <returns>
    /// The apply status; <see cref="SharingStatus.UnknownPublisher"/> when the candidate changed since it was shown (review
    /// <see cref="Candidate"/> again); <see cref="SharingStatus.RollbackRejected"/> when the new reference did not continue the
    /// sequence; or the check result.
    /// </returns>
    /// <exception cref="InvalidOperationException">This PC does not follow a folder or no new reference was found.</exception>
    public async Task<SharingStatus> AcceptNewPublisherAsync(string expectedKeyId, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedKeyId);
        await operation.WaitAsync(ct);
        try
        {
            SharingState current = State;
            SharingPublisherCandidate? accepted = Candidate;
            if (current.Role != SharingRole.Follower || accepted is null)
            {
                throw new InvalidOperationException("ConfigurationSharing.NoCandidate");
            }

            if (!string.Equals(accepted.KeyId, expectedKeyId, StringComparison.Ordinal))
            {
                Logger.Information("New reference not accepted: the candidate changed after it was shown.");
                return SharingStatus.UnknownPublisher;
            }

            if (PublicationPassword(host.ReadVault()) is not { } password)
            {
                return SetStatus(SharingStatus.PasswordsNeeded);
            }

            PublicationReadResult preview = await PublicationReader.PreviewAsync(current.Folder!, password, ct);
            if (preview is not { Status: PublicationReadStatus.Ok, Payload: { Publisher: { } publisher } payload } || preview.SignerKeyId != accepted.KeyId)
            {
                return await CheckCoreAsync(password, reopen: false, reapply: false, ct);
            }

            if (payload.Sequence <= current.AppliedSequence)
            {
                return SetStatus(SharingStatus.RollbackRejected);
            }

            Logger.Information("New reference accepted. Sequence={Sequence}", payload.Sequence);
            return await ApplyAsync(payload, current with { PinnedPublicKey = publisher.PublicKey }, updated: true, ct);
        }
        finally
        {
            operation.Release();
        }
    }

    /// <summary>
    /// "Stop following": stops the content fetch, keeps the configuration and makes it editable again; with
    /// <paramref name="forgetPassword"/> the publication password is removed from the vault.
    /// </summary>
    public async Task StopFollowingAsync(bool forgetPassword)
    {
        await operation.WaitAsync();
        try
        {
            SharingState current = State;
            if (current.Role != SharingRole.Follower)
            {
                return;
            }

            await CancelFetchAsync();
            host.ClearManaged();
            if (forgetPassword)
            {
                TryWriteVault(new Dictionary<string, string>(StringComparer.Ordinal), [SecretIds.PublicationPassword]);
            }

            sessionPassword = null;
            lock (gate)
            {
                appliedPayload = null;
                candidate = null;
                tamperedSequence = null;
                ResetContentTracking();
            }

            Persist(new SharingState { LastPublishedSequence = current.LastPublishedSequence });
            SetStatus(SharingStatus.Idle);
        }
        finally
        {
            operation.Release();
        }
    }

    /// <summary>
    /// Gets whether the content of the applied publication is still on its way to this follower: a fetch runs, or the last one found
    /// content not available yet (a cloud folder still syncing it) and the next check fetches again. False once the content is in the
    /// libraries, when some of it failed its import (<see cref="FailedContent"/>), or when it stalled (<see cref="IsContentStalled"/>).
    /// </summary>
    public bool IsContentArriving
    {
        get
        {
            lock (gate)
            {
                return IsContentArrivingLocked;
            }
        }
    }

    /// <summary>Gets whether the content of the applied publication is complete on this PC; media creation waits for <see cref="SharedContentSyncState.Synced"/>.</summary>
    public SharedContentSyncState ContentSyncState
    {
        get
        {
            lock (gate)
            {
                return SharedContentSync.Evaluate(state.Role, IsContentArrivingLocked, failedContent.Count, contentStalled);
            }
        }
    }

    /// <summary>Implements <see cref="IsContentArriving"/>; the caller holds <c>gate</c>.</summary>
    private bool IsContentArrivingLocked => state.Role == SharingRole.Follower
        && (isFetching || (status == SharingStatus.ContentDownloading && failedContent.Count == 0 && !contentStalled));

    /// <summary>
    /// Completes when the content of the applied publication has arrived, or stopped arriving: it waits over successive fetch attempts
    /// (one per check) while <see cref="IsContentArriving"/> holds, and returns at once otherwise. It also returns when the role or the
    /// applied sequence changes, since the content then belongs to another publication. Content still missing stays reported.
    /// </summary>
    /// <param name="ct">Cancels the wait; the fetches go on.</param>
    public async Task WaitForContentAsync(CancellationToken ct)
    {
        long applied = State.AppliedSequence;
        while (true)
        {
            await WaitForFetchAttemptAsync(ct);
            SharingState current = State;
            if (current.Role != SharingRole.Follower || current.AppliedSequence != applied || !IsContentArriving)
            {
                return;
            }

            // Between attempts: the next check starts the next fetch.
            await Task.Delay(ContentWaitInterval, time, ct);
        }
    }

    /// <summary>Completes when the running fetch attempt completes, at once when none runs.</summary>
    internal async Task WaitForFetchAttemptAsync(CancellationToken ct)
    {
        Task running;
        lock (gate)
        {
            running = fetchTask;
        }

        await running.WaitAsync(ct);
    }

    private ConfigurationPackagePayload? AppliedPayload
    {
        get
        {
            lock (gate)
            {
                return appliedPayload;
            }
        }
    }

    /// <summary>
    /// Reads the folder and acts on it. <paramref name="reopen"/> opens the applied manifest again (its sequence is accepted, a lower
    /// one is still a rollback); <paramref name="reapply"/> applies it again when it is the applied one.
    /// </summary>
    private async Task<SharingStatus> CheckCoreAsync(string password, bool reopen, bool reapply, CancellationToken cancellationToken)
    {
        lock (gate)
        {
            tamperedThisCheck = false;
        }

        SharingStatus result = await ReadAndActAsync(password, reopen, reapply, cancellationToken);

        // Cleared only when the whole check found no modified manifest, wherever it was found (the pinned read or the candidate preview).
        lock (gate)
        {
            if (!tamperedThisCheck)
            {
                tamperedSequence = null;
            }
        }

        return result;
    }

    private async Task<SharingStatus> ReadAndActAsync(string password, bool reopen, bool reapply, CancellationToken cancellationToken)
    {
        SharingState current = State;
        long applied = current.AppliedSequence;
        long floor = reopen ? Math.Max(0, applied - 1) : applied;
        PublicationReadResult read = await PublicationReader.ReadAsync(current.Folder!, password, current.PinnedPublicKey, floor, cancellationToken);
        switch (read.Status)
        {
            case PublicationReadStatus.Ok:
                ConfigurationPackagePayload payload = read.Payload!;
                if (payload.Sequence > applied || reapply)
                {
                    return await ApplyAsync(payload, current, updated: payload.Sequence > applied, cancellationToken);
                }

                lock (gate)
                {
                    appliedPayload = payload;
                    retryFetch = true;
                }

                return Settle(updated: false);
            case PublicationReadStatus.UpToDate or PublicationReadStatus.RollbackRejected:
                return read.Status == PublicationReadStatus.RollbackRejected || read.Sequence < applied
                    ? SetStatus(SharingStatus.RollbackRejected)
                    : Settle(updated: false);
            case PublicationReadStatus.Tampered:
                return ReportTampered(read.Sequence);
            case PublicationReadStatus.UnknownPublisher:
                return await FindCandidateAsync(current, password, cancellationToken);
            default:
                return SetStatus(MapRead(read.Status));
        }
    }

    /// <summary>
    /// The current manifest is signed by another key. The key id the reader reports is not authenticated, so the manifest is opened
    /// with the publication password and verified against the publisher key it embeds: only a key that authenticates its payload
    /// becomes the candidate, stored until <see cref="AcceptNewPublisherAsync"/>.
    /// </summary>
    private async Task<SharingStatus> FindCandidateAsync(SharingState current, string password, CancellationToken cancellationToken)
    {
        PublicationReadResult preview = await PublicationReader.PreviewAsync(current.Folder!, password, cancellationToken);
        if (preview is { Status: PublicationReadStatus.Ok, Payload.Publisher: { } publisher, SignerKeyId: { } keyId } && keyId != PinnedKeyIdOf(current))
        {
            lock (gate)
            {
                candidate = new SharingPublisherCandidate(keyId, publisher.PublicKey, publisher.Name, preview.Sequence);
            }

            return SetStatus(SharingStatus.UnknownPublisher);
        }

        lock (gate)
        {
            candidate = null;
        }

        return preview.Status switch
        {
            // The pointer moved back to a manifest of the pinned key between the two reads: the next check reads it.
            PublicationReadStatus.Ok => SetStatus(SharingStatus.Checking),
            PublicationReadStatus.Tampered => ReportTampered(preview.Sequence),
            _ => SetStatus(MapRead(preview.Status))
        };
    }

    /// <summary>Reports a manifest read as modified only when the previous check read the same manifest as modified.</summary>
    private SharingStatus ReportTampered(long sequence)
    {
        bool repeated;
        lock (gate)
        {
            repeated = tamperedSequence == sequence;
            tamperedSequence = sequence;
            tamperedThisCheck = true;
        }

        if (!repeated)
        {
            Logger.Debug("Publication manifest read as modified; it is read again at the next check before it is reported. Sequence={Sequence}", sequence);
            return SetStatus(SharingStatus.Checking);
        }

        return SetStatus(SharingStatus.Tampered);
    }

    /// <summary>
    /// Applies a verified publication through the host and persists <paramref name="target"/> with its sequence. Publication
    /// passwords overwrite the matching vault entries; sharing ids are never taken from a publication.
    /// </summary>
    private async Task<SharingStatus> ApplyAsync(ConfigurationPackagePayload payload, SharingState target, bool updated, CancellationToken cancellationToken)
    {
        await CancelFetchAsync();
        PackagePublisher publisher = payload.Publisher!;
        FoundryConfigurationDocument configuration = PortableConfiguration.ApplyLocal(payload.Configuration, host.CurrentConfiguration);
        Dictionary<string, string> secrets = (payload.Secrets ?? new Dictionary<string, string>())
            .Where(entry => !SecretIds.IsSharingInternal(entry.Key))
            .ToDictionary(entry => entry.Key, entry => entry.Value, StringComparer.Ordinal);
        await host.ApplyConfigurationAsync(configuration, secrets, publisher.Name, cancellationToken);
        Persist(target with
        {
            AppliedSequence = payload.Sequence,
            PublisherId = publisher.Id,
            PublisherName = publisher.Name,
            IncludePasswords = payload.Secrets is not null
        });
        lock (gate)
        {
            appliedPayload = payload;
            candidate = null;
            tamperedSequence = null;
            ResetContentTracking();
            retryFetch = true;
        }

        Logger.Information("Publication applied. Sequence={Sequence}, ContentCount={ContentCount}, SecretCount={SecretCount}",
            payload.Sequence, payload.Contents.Count, secrets.Count);
        return Settle(updated);
    }

    /// <summary>Reports the follower outcome of a successful read: missing passwords first, then missing content, then up to date.</summary>
    private SharingStatus Settle(bool updated)
    {
        MarkSuccess();
        lock (gate)
        {
            candidate = null;
        }

        bool passwordsMissing = ArePasswordsMissing();
        bool contentPending = EnsureContentFetch();
        return SetStatus(passwordsMissing ? SharingStatus.PasswordsNeeded
            : contentPending ? SharingStatus.ContentDownloading
            : updated ? SharingStatus.Updated
            : SharingStatus.UpToDate);
    }

    /// <summary>Whether the publication password, or a password the configuration needs to build media, is missing from the vault.</summary>
    private bool ArePasswordsMissing()
    {
        IReadOnlyDictionary<string, string> vault = host.ReadVault();
        return PublicationPassword(vault) is null || SecretMap.RequiredIds(host.CurrentConfiguration).Any(id => !vault.ContainsKey(id));
    }

    /// <summary>
    /// Returns whether content of the applied publication is missing locally, and starts a background fetch when one is due: not
    /// while one runs, and not when only content that failed its import remains (it fails the same way until a new version).
    /// </summary>
    private bool EnsureContentFetch()
    {
        ConfigurationPackagePayload? payload = AppliedPayload;
        if (payload is null)
        {
            return false;
        }

        lock (gate)
        {
            if (!fetchTask.IsCompleted)
            {
                return true;
            }
        }

        bool missing = sync.Missing(payload).Count > 0;
        lock (gate)
        {
            if (!missing)
            {
                ResetContentTracking();
                return false;
            }

            if (retryFetch)
            {
                CancellationTokenSource cancellation = new();
                string folder = state.Folder!;
                fetchCancellation = cancellation;
                isFetching = true;
                fetchTask = Task.Run(() => FetchAsync(folder, payload, cancellation.Token), CancellationToken.None);
            }

            return true;
        }
    }

    /// <summary>Fetches the missing content of <paramref name="payload"/> on a background thread; never touches the host.</summary>
    private async Task FetchAsync(string folder, ConfigurationPackagePayload payload, CancellationToken cancellationToken)
    {
        ContentSyncResult result;
        try
        {
            result = await sync.FetchAsync(folder, payload, new ProgressReporter(this), cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            lock (gate)
            {
                isFetching = false;
            }

            return;
        }
        catch (Exception exception)
        {
            Logger.Warning("Publication content fetch failed unexpectedly; it is retried at the next check. ExceptionType={ExceptionType}",
                exception.GetType().Name);
            result = new ContentSyncResult(0, [], payload.Contents);
        }

        CompleteFetch(payload, result);
    }

    /// <summary>
    /// Records the outcome of a fetch and always raises <see cref="StatusChanged"/>: the fetch ending changes
    /// <see cref="IsContentFetching"/> and <see cref="ContentSyncState"/> even when the status stays the same (for example
    /// <see cref="SharingStatus.PasswordsNeeded"/>), and the media creation gate follows that event.
    /// </summary>
    private void CompleteFetch(ConfigurationPackagePayload payload, ContentSyncResult result)
    {
        bool complete;
        bool stalled;
        lock (gate)
        {
            isFetching = false;
            if (!ReferenceEquals(payload, appliedPayload))
            {
                complete = false;
                stalled = false;
            }
            else
            {
                failedContent = result.Failed;
                retryFetch = result.NotYetAvailable.Count > 0;
                stalled = TrackUnavailableContent(result.NotYetAvailable);
                complete = result.Failed.Count == 0 && result.NotYetAvailable.Count == 0;
            }
        }

        if (stalled)
        {
            Logger.Warning("Publication content is still not available after repeated checks; it may be damaged in the folder. NotYetAvailableCount={NotYetAvailableCount}, CheckCount={CheckCount}",
                result.NotYetAvailable.Count, StalledContentFetchCount);
        }

        if (complete && Status == SharingStatus.ContentDownloading)
        {
            // The status changes, so SetStatus raises the event.
            SetStatus(SharingStatus.UpToDate);
        }
        else
        {
            StatusChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Counts consecutive fetches with the same unavailable content; returns true when that makes the content stalled.</summary>
    private bool TrackUnavailableContent(IReadOnlyList<PackageContent> notYetAvailable)
    {
        if (notYetAvailable.Count == 0)
        {
            unavailableContent = null;
            unchangedFetchCount = 0;
            contentStalled = false;
            return false;
        }

        HashSet<(SharedContentKind Kind, string Hash)> current = notYetAvailable.Select(content => (content.Kind, content.Hash)).ToHashSet();
        if (unavailableContent is not null && unavailableContent.SetEquals(current))
        {
            unchangedFetchCount++;
        }
        else
        {
            unavailableContent = current;
            unchangedFetchCount = 1;
            contentStalled = false;
        }

        if (contentStalled || unchangedFetchCount < StalledContentFetchCount)
        {
            return false;
        }

        contentStalled = true;
        return true;
    }

    /// <summary>Clears what is known about the content of the applied publication; call under <see cref="gate"/>.</summary>
    private void ResetContentTracking()
    {
        failedContent = [];
        retryFetch = false;
        unavailableContent = null;
        unchangedFetchCount = 0;
        contentStalled = false;
    }

    private async Task CancelFetchAsync()
    {
        Task running;
        lock (gate)
        {
            fetchCancellation?.Cancel();
            running = fetchTask;
        }

        await running;
        lock (gate)
        {
            fetchCancellation?.Dispose();
            fetchCancellation = null;
        }
    }

    private void StorePublicationPassword(string password)
    {
        Dictionary<string, string> entries = new(StringComparer.Ordinal) { [SecretIds.PublicationPassword] = password };
        sessionPassword = TryWriteVault(entries, [SecretIds.PreviousPublicationPassword]) ? null : password;
    }

    private string? PublicationPassword(IReadOnlyDictionary<string, string> vault) =>
        sessionPassword ?? vault.GetValueOrDefault(SecretIds.PublicationPassword);

    private static string? PinnedKeyIdOf(SharingState current) =>
        current.PinnedPublicKey is { Length: > 0 } key ? PublicationSigner.KeyId(key) : null;

    /// <summary>
    /// Maps a read outcome that needs no further handling. <see cref="CheckCoreAsync"/> handles Ok, UpToDate, Tampered and
    /// UnknownPublisher itself; their arms are only the plain meaning of each value.
    /// </summary>
    private static SharingStatus MapRead(PublicationReadStatus status) => status switch
    {
        PublicationReadStatus.Ok => SharingStatus.Checking,
        PublicationReadStatus.UpToDate => SharingStatus.UpToDate,

        // A cloud folder delivered the pointer before the manifest.
        PublicationReadStatus.NotYetAvailable => SharingStatus.Checking,
        PublicationReadStatus.FolderUnavailable or PublicationReadStatus.NotAPublication => SharingStatus.FolderUnavailable,
        PublicationReadStatus.WrongPassword => SharingStatus.WrongPassword,
        PublicationReadStatus.Tampered => SharingStatus.Tampered,
        PublicationReadStatus.UnknownPublisher => SharingStatus.UnknownPublisher,
        PublicationReadStatus.RollbackRejected => SharingStatus.RollbackRejected,
        PublicationReadStatus.Unsupported => SharingStatus.Unsupported,
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Unknown publication read status.")
    };

    /// <summary>
    /// Runs publishes until no change is pending. The run belongs to the engine, not to a caller: it is cancelled only by
    /// <see cref="StopAsync"/>, so a caller that stops waiting never drops the follow-up publish another caller queued.
    /// </summary>
    private async Task RunPublishesAsync(TaskCompletionSource<SharingStatus> run)
    {
        try
        {
            SharingStatus result;
            while (true)
            {
                lock (gate)
                {
                    publishPending = false;
                }

                result = await PublishAfterChangeAsync();
                lock (gate)
                {
                    if (!publishPending)
                    {
                        activePublish = null;
                        break;
                    }
                }
            }

            run.TrySetResult(result);
        }
        catch (Exception exception)
        {
            lock (gate)
            {
                activePublish = null;
            }

            run.TrySetException(exception);
        }
    }

    private async Task<SharingStatus> PublishAfterChangeAsync()
    {
        await operation.WaitAsync();
        try
        {
            SharingState current = State;
            if (current.Role != SharingRole.Reference)
            {
                return Status;
            }

            if (ReadReferenceSecrets() is not { } secrets)
            {
                return SetStatus(SharingStatus.PasswordsNeeded);
            }

            using (secrets.Signer)
            {
                if (secrets.PreviousPassword is { } previous)
                {
                    PublishStatus rewrapped = await CompletePasswordChangeAsync(current.Folder!, secrets.Signer, previous, secrets.Password, CancellationToken.None);
                    if (!IsRewrapComplete(rewrapped))
                    {
                        return SetStatus(Map(rewrapped));
                    }
                }

                return await PublishAsync(current, secrets.Signer, secrets.MasterKey, secrets.Password, takeOver: false, CancellationToken.None);
            }
        }
        finally
        {
            operation.Release();
        }
    }

    private async Task<SharingStatus> ReplacePasswordAsync(string newPassword, bool reset, CancellationToken cancellationToken)
    {
        ValidatePassword(newPassword);
        await operation.WaitAsync(cancellationToken);
        try
        {
            SharingState current = State;
            if (current.Role != SharingRole.Reference)
            {
                throw new InvalidOperationException("ConfigurationSharing.NotReference");
            }

            if (ReadReferenceSecrets() is not { } secrets)
            {
                return SetStatus(SharingStatus.PasswordsNeeded);
            }

            using (secrets.Signer)
            {
                string folder = current.Folder!;
                if (secrets.PreviousPassword is { } pending)
                {
                    PublishStatus completed = await CompletePasswordChangeAsync(folder, secrets.Signer, pending, secrets.Password, cancellationToken);
                    if (!IsRewrapComplete(completed))
                    {
                        return SetStatus(Map(completed));
                    }
                }

                Dictionary<string, string> entries = new(StringComparer.Ordinal)
                {
                    [SecretIds.PublicationPassword] = newPassword,
                    [SecretIds.PreviousPublicationPassword] = secrets.Password
                };
                byte[] masterKey = secrets.MasterKey;
                if (reset)
                {
                    masterKey = RandomNumberGenerator.GetBytes(MasterKeySize);
                    entries[SecretIds.MasterKey] = Convert.ToBase64String(masterKey);
                }

                if (!TryWriteVault(entries))
                {
                    return SetStatus(SharingStatus.PasswordsNeeded);
                }

                sessionPassword = null;
                if (reset)
                {
                    // Cleared by the next successful publish, which is the one that encrypts the content again.
                    Persist(current with { RevocationPending = true });
                    current = State;
                }

                PublishStatus rewrapped = await CompletePasswordChangeAsync(folder, secrets.Signer, secrets.Password, newPassword, cancellationToken);
                if (!IsRewrapComplete(rewrapped))
                {
                    Logger.Warning("Publication password change interrupted; it is completed at the next publish. Reset={Reset}, Status={Status}", reset, rewrapped);
                    return SetStatus(Map(rewrapped));
                }

                Logger.Information("Publication password changed. Reset={Reset}, ManifestRewritten={ManifestRewritten}", reset,
                    rewrapped == PublishStatus.Published);

                // Nothing published yet: the next version is the first one under the new password, so publish it now.
                return reset || rewrapped == PublishStatus.NothingToRewrap
                    ? await PublishAsync(current, secrets.Signer, masterKey, newPassword, takeOver: false, cancellationToken)
                    : SetStatus(SharingStatus.Published);
            }
        }
        finally
        {
            operation.Release();
        }
    }

    /// <summary>
    /// Rewrites the current manifest from <paramref name="previous"/> to <paramref name="password"/>. When the previous password no
    /// longer opens it, an interrupted attempt already rewrote it, which a rewrite under <paramref name="password"/> itself confirms.
    /// A folder without a manifest of this PC has nothing to rewrite, which completes the change as well. Once complete, the previous
    /// password is removed from the vault. An unexpected writer failure reads as <see cref="PublishStatus.FolderUnavailable"/>.
    /// </summary>
    private async Task<PublishStatus> CompletePasswordChangeAsync(string folder, PublicationSigningKey signer, string previous, string password,
        CancellationToken cancellationToken)
    {
        PublishStatus result;
        try
        {
            result = await PublicationWriter.RewrapAsync(folder, previous, password, signer, cancellationToken);
            if (result == PublishStatus.WrongPassword)
            {
                result = await PublicationWriter.RewrapAsync(folder, password, password, signer, cancellationToken);
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            Logger.Warning("Publication manifest rewrite failed unexpectedly. ExceptionType={ExceptionType}", exception.GetType().Name);
            return PublishStatus.FolderUnavailable;
        }

        if (IsRewrapComplete(result))
        {
            TryWriteVault(new Dictionary<string, string>(StringComparer.Ordinal), [SecretIds.PreviousPublicationPassword]);
        }

        return result;
    }

    private static bool IsRewrapComplete(PublishStatus status) => status is PublishStatus.Published or PublishStatus.NothingToRewrap;

    /// <summary>
    /// Publishes for <see cref="StartPublishingAsync"/> and <see cref="TakeOverAsync"/>: reads the reference keys from the vault and
    /// creates the missing ones, stores them before anything is signed, then publishes. A pending password change of an earlier
    /// folder is dropped. The password is stored once the folder is adopted.
    /// </summary>
    private async Task<SharingStatus> PublishWithPreparedKeysAsync(SharingState target, string password, bool takeOver, CancellationToken cancellationToken)
    {
        IReadOnlyDictionary<string, string> vault = host.ReadVault();
        Dictionary<string, string> created = new(StringComparer.Ordinal);
        PublicationSigningKey? signer = ImportSigner(vault);
        if (signer is null)
        {
            signer = PublicationSigningKey.Create();
            created[SecretIds.SigningKey] = Convert.ToBase64String(signer.ExportPkcs8());
        }

        using (signer)
        {
            byte[]? masterKey = ReadMasterKey(vault);
            if (masterKey is null)
            {
                masterKey = RandomNumberGenerator.GetBytes(MasterKeySize);
                created[SecretIds.MasterKey] = Convert.ToBase64String(masterKey);
            }

            if (!TryWriteVault(created, [SecretIds.PreviousPublicationPassword]))
            {
                return SetStatus(SharingStatus.PasswordsNeeded);
            }

            if (created.Count > 0)
            {
                Logger.Information("Publication keys created. SigningKeyCreated={SigningKeyCreated}, MasterKeyCreated={MasterKeyCreated}",
                    created.ContainsKey(SecretIds.SigningKey), created.ContainsKey(SecretIds.MasterKey));
            }

            SharingStatus result = await PublishAsync(target, signer, masterKey, password, takeOver, cancellationToken);
            if (result == SharingStatus.Published)
            {
                Adopt(State, password);
            }

            return result;
        }
    }

    /// <summary>
    /// Publishes the configuration read now, when it is ready to build and its content is available locally. On success the new
    /// sequence is persisted with <paramref name="target"/>, which adopts its role and folder.
    /// </summary>
    private async Task<SharingStatus> PublishAsync(SharingState target, PublicationSigningKey signer, byte[] masterKey, string password, bool takeOver,
        CancellationToken cancellationToken)
    {
        FoundryConfigurationDocument configuration = host.CurrentConfiguration;
        if (!host.IsReadyToBuild || !host.AllContentAvailable(configuration))
        {
            return SetStatus(SharingStatus.NotPublishedIncomplete);
        }

        FoundryConfigurationDocument portable = PortableConfiguration.Strip(configuration);
        IReadOnlyList<PackageContent> listed = PortableConfiguration.ListContents(portable);
        IReadOnlyDictionary<string, string>? secrets = target.IncludePasswords
            ? SecretMap.ToEntries(portable, SecretMap.FromEntries(portable, host.ReadVault()))
            : null;
        PublicationRequest request = new(target.Folder!, portable, listed, secrets, signer, masterKey, password, target.PublisherId,
            target.PublisherName ?? string.Empty, target.LastPublishedSequence, takeOver);

        SharingStatus before = Status;
        SetStatus(SharingStatus.Publishing);
        Logger.Information("Publication started. ContentCount={ContentCount}, SecretCount={SecretCount}, TakeOver={TakeOver}",
            listed.Count, secrets?.Count ?? 0, takeOver);
        CancellationToken stopping;
        lock (gate)
        {
            stopping = roleCancellation.Token;
        }

        PublishResult result;
        try
        {
            using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, stopping);
            result = await PublicationWriter.PublishAsync(request, contents, new ProgressReporter(this), linked.Token);
        }
        catch (InvalidDataException)
        {
            // The content source reports content missing or damaged in its library, or changed while it was encrypted.
            Logger.Warning("Publication content is missing or changed in its library; the previous version stays current.");
            return SetStatus(SharingStatus.NotPublishedIncomplete);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            Logger.Information("Publication canceled because publishing stopped; the previous version stays current.");
            return SetStatus(SharingStatus.Idle);
        }
        catch (OperationCanceledException)
        {
            SetStatus(before);
            throw;
        }
        catch (Exception exception)
        {
            // The writer or the content source failed in an unexpected way: the previous version stays current, Publish now publishes again.
            Logger.Warning("Publication failed unexpectedly; the previous version stays current. ExceptionType={ExceptionType}", exception.GetType().Name);
            return SetStatus(SharingStatus.FolderUnavailable);
        }

        lock (gate)
        {
            foreignPublication = result.Status == PublishStatus.ForeignPublisher ? new SharingForeignPublication(result.Sequence, result.ForeignKeyId) : null;
        }

        if (result.Status != PublishStatus.Published)
        {
            return SetStatus(Map(result.Status));
        }

        Persist(target with { LastPublishedSequence = result.Sequence, RevocationPending = false });
        MarkSuccess();
        Logger.Information("Publication completed. Sequence={Sequence}, ContentCount={ContentCount}, SecretCount={SecretCount}",
            result.Sequence, listed.Count, secrets?.Count ?? 0);
        return SetStatus(SharingStatus.Published);
    }

    /// <summary>Persists <paramref name="target"/> when it is not current yet and stores the publication password.</summary>
    private void Adopt(SharingState target, string password)
    {
        if (State != target)
        {
            Persist(target);
        }

        if (!TryWriteVault(new Dictionary<string, string>(StringComparer.Ordinal) { [SecretIds.PublicationPassword] = password }))
        {
            sessionPassword = password;
        }
        else
        {
            sessionPassword = null;
        }
    }

    private SharingState NewReferenceState(string folder, bool includePasswords)
    {
        SharingState current = State;
        return new SharingState
        {
            Role = SharingRole.Reference,
            Folder = folder,
            PublisherId = current.PublisherId == Guid.Empty ? Guid.NewGuid() : current.PublisherId,
            PublisherName = Environment.MachineName,
            IncludePasswords = includePasswords,
            LastPublishedSequence = current.LastPublishedSequence,

            // A reset that stopped before its publish still owes the re-encryption of that folder's content; another folder does not.
            RevocationPending = current.RevocationPending && IsSameFolder(current.Folder, folder)
        };
    }

    private static bool IsSameFolder(string? left, string right) =>
        left is not null && string.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(left)), Path.TrimEndingDirectorySeparator(Path.GetFullPath(right)),
            StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Reads the reference keys and password, or returns <see langword="null"/> when the vault is unavailable or one of them is
    /// missing or malformed: nothing is created here.
    /// </summary>
    private ReferenceSecrets? ReadReferenceSecrets()
    {
        if (!host.IsVaultAvailable)
        {
            return null;
        }

        IReadOnlyDictionary<string, string> vault = host.ReadVault();
        string? password = PublicationPassword(vault);
        byte[]? masterKey = ReadMasterKey(vault);
        if (password is null || masterKey is null || ImportSigner(vault) is not { } signer)
        {
            return null;
        }

        return new ReferenceSecrets(signer, masterKey, password, vault.GetValueOrDefault(SecretIds.PreviousPublicationPassword));
    }

    private static PublicationSigningKey? ImportSigner(IReadOnlyDictionary<string, string> vault)
    {
        if (!vault.TryGetValue(SecretIds.SigningKey, out string? encoded))
        {
            return null;
        }

        try
        {
            return PublicationSigningKey.Import(Convert.FromBase64String(encoded));
        }
        catch (Exception exception) when (exception is FormatException or CryptographicException)
        {
            Logger.Warning("The publication signing key in the vault is malformed.");
            return null;
        }
    }

    private static byte[]? ReadMasterKey(IReadOnlyDictionary<string, string> vault)
    {
        if (!vault.TryGetValue(SecretIds.MasterKey, out string? encoded))
        {
            return null;
        }

        try
        {
            byte[] key = Convert.FromBase64String(encoded);
            if (key.Length == MasterKeySize)
            {
                return key;
            }
        }
        catch (FormatException)
        {
        }

        Logger.Warning("The publication master key in the vault is malformed.");
        return null;
    }

    private bool TryWriteVault(IReadOnlyDictionary<string, string> entries, IReadOnlyCollection<string>? removeIds = null)
    {
        try
        {
            host.WriteVault(entries, removeIds);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or CryptographicException)
        {
            Logger.Warning("The secret vault could not be written for configuration sharing. ExceptionType={ExceptionType}", exception.GetType().Name);
            return false;
        }
    }

    private static void ValidatePassword(string password)
    {
        ArgumentNullException.ThrowIfNull(password);
        if (PassphrasePolicy.Validate(password) != PassphraseValidation.Valid)
        {
            throw new ArgumentException("The publication password does not satisfy the passphrase policy.", nameof(password));
        }
    }

    private static SharingStatus Map(PublishStatus status) => status switch
    {
        PublishStatus.Published => SharingStatus.Published,
        PublishStatus.ForeignPublisher => SharingStatus.UnknownPublisher,
        PublishStatus.WrongPassword => SharingStatus.WrongPassword,
        PublishStatus.FolderUnavailable => SharingStatus.FolderUnavailable,
        PublishStatus.FolderNotEmpty => SharingStatus.FolderNotEmpty,

        // Unreachable in practice, since only authenticated manifests count toward the sequence; the folder cannot take
        // another version, so the recovery is the same as for a folder that is not empty: a new folder (or a reset).
        PublishStatus.SequenceExhausted => SharingStatus.FolderNotEmpty,

        // Callers treat it as a completed password change before mapping; nothing is wrong with the folder.
        PublishStatus.NothingToRewrap => SharingStatus.Published,
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Unknown publish status.")
    };

    /// <summary>Persists the state; a failed write is logged and retried by the next persist, the state in memory stays current.</summary>
    private void Persist(SharingState next)
    {
        SharingRole previousRole;
        lock (gate)
        {
            previousRole = state.Role;
            state = next;
        }

        try
        {
            SharingStateStore.Save(statePath, next);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Logger.Warning(exception, "The configuration sharing state could not be saved; it is saved again at the next change.");
        }

        if (previousRole != next.Role)
        {
            Logger.Information("Configuration sharing role changed. Role={Role}, PreviousRole={PreviousRole}", next.Role, previousRole);
        }
    }

    private void MarkSuccess()
    {
        lock (gate)
        {
            lastSuccessUtc = time.GetUtcNow();
        }
    }

    private SharingStatus SetStatus(SharingStatus value)
    {
        bool changed;
        SharingRole role;
        lock (gate)
        {
            changed = status != value;
            status = value;
            role = state.Role;
        }

        if (changed)
        {
            LogStatus(role, value);
            StatusChanged?.Invoke(this, EventArgs.Empty);
        }

        return value;
    }

    /// <summary>Logs entering a failure status once, with its typed reason; successes are logged where they happen.</summary>
    private static void LogStatus(SharingRole role, SharingStatus value)
    {
        switch (value)
        {
            case SharingStatus.NotPublishedIncomplete:
                Logger.Information("Publication waits for a configuration that is ready to build with all its content available.");
                break;
            case SharingStatus.FolderUnavailable or SharingStatus.FolderNotEmpty or SharingStatus.WrongPassword or SharingStatus.Tampered
                or SharingStatus.UnknownPublisher or SharingStatus.RollbackRejected or SharingStatus.Unsupported or SharingStatus.PasswordsNeeded:
                Logger.Warning("Configuration sharing reports a problem. Role={Role}, Status={Status}", role, value);
                break;
        }
    }

    /// <summary>Reference keys and passwords read from the vault; the caller disposes <see cref="Signer"/>.</summary>
    private sealed record ReferenceSecrets(PublicationSigningKey Signer, byte[] MasterKey, string Password, string? PreviousPassword);

    /// <summary>Forwards content copy progress to <see cref="ContentProgressChanged"/> on the reporting thread.</summary>
    private sealed class ProgressReporter(ConfigurationSharingEngine engine) : IProgress<ContentProgress>
    {
        public void Report(ContentProgress value) => engine.ContentProgressChanged?.Invoke(engine, value);
    }
}
