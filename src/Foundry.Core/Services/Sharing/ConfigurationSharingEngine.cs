// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Security.Cryptography;
using Foundry.Core.Models.Configuration;
using Foundry.Core.Services.Secrets;
using Serilog;

namespace Foundry.Core.Services.Sharing;

/// <summary>
/// The configuration sharing state machine (spec 6.1, 6.2 and 9): the role of this PC and every decision about publishing,
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
/// <para>Nothing here logs a secret, password or key; only sequences, counts and typed statuses.</para>
/// </remarks>
public sealed class ConfigurationSharingEngine
{
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

    /// <summary>Gets the publication password entered this session when the vault could not store it; it wins over the vault.</summary>
    private string? sessionPassword;

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

    private static ILogger Logger => Log.ForContext<ConfigurationSharingEngine>();

    /// <summary>
    /// "Publish to a folder" (spec 6.1): makes this PC the reference of <paramref name="folder"/> and publishes the current
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
    /// "Take over" (spec 6.1): continues a folder another key publishes to, with this PC's key, after the user entered the current
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
    /// Publishes the current configuration after a change (the host debounces changes) or a "Retry". Does nothing without the
    /// reference role. A change while a publish runs queues exactly one follow-up publish; every caller waiting on the same run gets
    /// the status of its last publish. A password change or reset that was interrupted is completed first.
    /// </summary>
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
                return activePublish.Task;
            }

            run = activePublish = new TaskCompletionSource<SharingStatus>(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        _ = RunPublishesAsync(run, ct);
        return run.Task;
    }

    /// <summary>
    /// "Change password" (spec 6.1 and 7): rewrites the current manifest under <paramref name="newPassword"/> in place; content is
    /// untouched, so a former password holder who kept the master key can still decrypt content files. The new password is stored
    /// first, with the old one kept as <see cref="SecretIds.PreviousPublicationPassword"/> until the manifest is rewritten, so an
    /// interrupted change is completed by the next publish.
    /// </summary>
    /// <returns><see cref="SharingStatus.Published"/>, <see cref="SharingStatus.PasswordsNeeded"/>, or the folder failure.</returns>
    /// <exception cref="ArgumentException">The new password breaks <see cref="PassphrasePolicy"/>.</exception>
    /// <exception cref="InvalidOperationException">This PC is not a reference.</exception>
    public Task<SharingStatus> ChangePasswordAsync(string newPassword, CancellationToken ct) => ReplacePasswordAsync(newPassword, reset: false, ct);

    /// <summary>
    /// "Reset publication" (spec 6.1 and 7), the complete revocation: rewrites the current manifest under
    /// <paramref name="newPassword"/> (<see cref="PublicationWriter.RewrapAsync"/>), then publishes with a new master key, which
    /// encrypts every content file again; followers enter the new password. The new password and master key are stored before the
    /// folder changes, so an interrupted reset is completed by the next change or Retry. While content is encrypted again, followers
    /// read it as not yet available.
    /// </summary>
    /// <returns>The status of the publish, or of the rewrite when it fails.</returns>
    /// <exception cref="ArgumentException">The new password breaks <see cref="PassphrasePolicy"/>.</exception>
    /// <exception cref="InvalidOperationException">This PC is not a reference.</exception>
    public Task<SharingStatus> ResetPublicationAsync(string newPassword, CancellationToken ct) => ReplacePasswordAsync(newPassword, reset: true, ct);

    /// <summary>
    /// "Stop publishing" (spec 6.1): cancels a running publish, which leaves the previous version current, and makes this PC a
    /// normal PC. The folder and the vault keys stay as they are; <see cref="SharingState.LastPublishedSequence"/> is kept.
    /// </summary>
    public async Task StopAsync()
    {
        if (State.Role != SharingRole.Reference)
        {
            return;
        }

        await roleCancellation.CancelAsync();
        await operation.WaitAsync();
        try
        {
            ResetRoleCancellation();
            SharingState current = State;
            if (current.Role == SharingRole.Reference)
            {
                Persist(current with { Role = SharingRole.None, Folder = null });
            }

            SetStatus(SharingStatus.Idle);
        }
        finally
        {
            operation.Release();
        }
    }

    private async Task RunPublishesAsync(TaskCompletionSource<SharingStatus> run, CancellationToken cancellationToken)
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

                result = await PublishAfterChangeAsync(cancellationToken);
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

            if (exception is OperationCanceledException)
            {
                run.TrySetCanceled(cancellationToken);
            }
            else
            {
                run.TrySetException(exception);
            }
        }
    }

    private async Task<SharingStatus> PublishAfterChangeAsync(CancellationToken cancellationToken)
    {
        await operation.WaitAsync(cancellationToken);
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
                    PublishStatus rewrapped = await CompletePasswordChangeAsync(current.Folder!, secrets.Signer, previous, secrets.Password, cancellationToken);
                    if (rewrapped != PublishStatus.Published)
                    {
                        return SetStatus(Map(rewrapped));
                    }
                }

                return await PublishAsync(current, secrets.Signer, secrets.MasterKey, secrets.Password, takeOver: false, cancellationToken);
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
                    if (completed != PublishStatus.Published)
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
                PublishStatus rewrapped = await CompletePasswordChangeAsync(folder, secrets.Signer, secrets.Password, newPassword, cancellationToken);
                if (rewrapped != PublishStatus.Published)
                {
                    Logger.Warning("Publication password change interrupted; it is completed at the next publish. Reset={Reset}, Status={Status}", reset, rewrapped);
                    return SetStatus(Map(rewrapped));
                }

                Logger.Information("Publication password changed. Reset={Reset}", reset);
                return reset
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
    /// On success the previous password is removed from the vault.
    /// </summary>
    private async Task<PublishStatus> CompletePasswordChangeAsync(string folder, PublicationSigningKey signer, string previous, string password,
        CancellationToken cancellationToken)
    {
        PublishStatus result = await PublicationWriter.RewrapAsync(folder, previous, password, signer, cancellationToken);
        if (result == PublishStatus.WrongPassword)
        {
            result = await PublicationWriter.RewrapAsync(folder, password, password, signer, cancellationToken);
        }

        if (result == PublishStatus.Published)
        {
            TryWriteVault(new Dictionary<string, string>(StringComparer.Ordinal), [SecretIds.PreviousPublicationPassword]);
        }

        return result;
    }

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
        PublishResult result;
        try
        {
            using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, roleCancellation.Token);
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

        if (result.Status != PublishStatus.Published)
        {
            return SetStatus(Map(result.Status));
        }

        Persist(target with { LastPublishedSequence = result.Sequence });
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
            LastPublishedSequence = current.LastPublishedSequence
        };
    }

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
        string? password = sessionPassword ?? vault.GetValueOrDefault(SecretIds.PublicationPassword);
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

        // FolderNotEmpty and SequenceExhausted: this folder cannot hold the publication; another folder is needed.
        _ => SharingStatus.FolderNotEmpty
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

    private void ResetRoleCancellation()
    {
        roleCancellation.Dispose();
        roleCancellation = new CancellationTokenSource();
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
