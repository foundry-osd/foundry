// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Security.Cryptography;
using Foundry.Core.Models.Profiles;
using Foundry.Core.Services.Application;
using Foundry.Core.Services.Profiles;
using Foundry.Services.Shell;

namespace Foundry.Services.Configuration;

/// <summary>Coordinates local profile commits, activation, and background synchronization on the desktop dispatcher.</summary>
public sealed partial class DeploymentProfileCoordinator : IDisposable
{
    private static readonly Serilog.ILogger Logger = Serilog.Log.ForContext<DeploymentProfileCoordinator>();
    private readonly LocalDeploymentProfileRepository local;
    private readonly IDeploymentProfilePackageService packages;
    private readonly DeploymentProfileSessionService session;
    private readonly IFoundryConfigurationStateService configuration;
    private readonly IAppDispatcher dispatcher;
    private readonly IShellNavigationGuardService navigationGuard;
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly CancellationTokenSource lifetime = new();
    private CancellationTokenSource? debounce;
    private byte[]? sessionSharedKey;
    private bool applying;
    private bool initialized;
    private bool automaticSynchronizationStarted;
    private long editVersion;
    private long lastSharedEditVersion;
    private long persistedEditVersion;
    private long lastEditTick;
    private Foundry.Core.Models.Configuration.FoundryConfigurationDocument? observedConfiguration;
    private int activationSuspensions;
    private ShellNavigationState suspendedNavigationState;
    private bool ownsNavigationGuard;

    public bool IsSettingsOpen { get; set; }

    /// <summary>Defers automatic activation while the Settings card is collecting an operator decision.</summary>
    public IDisposable SuspendActivation()
    {
        if (activationSuspensions > 0) throw new InvalidOperationException("Another profile interaction is already active.");
        if (activationSuspensions == 0)
        {
            EnsureCanActivate();
            suspendedNavigationState = navigationGuard.State;
            ownsNavigationGuard = true;
            navigationGuard.SetState(ShellNavigationState.InteractionPending);
        }
        activationSuspensions++;
        return new ActivationSuspension(this);
    }

    public DeploymentProfileCoordinator(LocalDeploymentProfileRepository local, IDeploymentProfilePackageService packages,
        DeploymentProfileSessionService session, IFoundryConfigurationStateService configuration,
        IAppDispatcher dispatcher, IShellNavigationGuardService navigationGuard,
        INetworkSecretStateService network, IDeploymentProtectionSecretStateService deployment, IOobeAccountSecretStateService accounts)
    {
        this.local = local;
        this.packages = packages;
        this.session = session;
        this.configuration = configuration;
        observedConfiguration = configuration.Current;
        this.dispatcher = dispatcher;
        this.navigationGuard = navigationGuard;
        configuration.StateChanged += OnEdited;
        network.Changed += OnEdited;
        deployment.Changed += OnEdited;
        accounts.Changed += OnEdited;
    }

    public event EventHandler? Changed;
    /// <summary>Updates synchronization feedback without rebuilding the profile selector.</summary>
    public event EventHandler? SynchronizationStateChanged;
    public LocalProfileDescriptor? Active { get; private set; }
    public IReadOnlyList<LocalProfileDescriptor> Profiles { get; private set; } = [];
    public string StatusKey { get; private set; } = "Profiles.Ready";
    public bool HasConflict { get; private set; }
    /// <summary>A shared deletion requires an independent copy, rather than ordinary conflict resolution.</summary>
    public bool IsSharedProfileDeleted { get; private set; }
    /// <summary>Indicates an active synchronization attempt, including automatic checks.</summary>
    public bool IsSynchronizing { get; private set; }
    /// <summary>Includes edits awaiting local autosave as well as unpublished saved changes.</summary>
    public bool HasPendingSynchronizationChanges => Active?.Enrollment is { } enrollment &&
        (enrollment.IsDirty || enrollment.PendingOperationId is not null || enrollment.PendingConnectionFile || HasSharedEditsSince(persistedEditVersion));

    /// <summary>Restores only this Windows user's selected local profile, preserving locked or incompatible data.</summary>
    public async Task InitializeAsync()
    {
        if (initialized) return;
        initialized = true;
        Logger.Information("Profile initialization started.");
        try
        {
            await Task.Run(CleanupAbandonedStagingDirectories);
            Profiles = await Task.Run(local.List);
            Guid? selected = await Task.Run(local.GetActive);
            if (selected is Guid id)
            {
                await ActivateAsync(id);
            }
            else if (Profiles.Count == 0)
            {
                await SaveAsCopyAsync("Default");
            }
        }
        catch (Exception ex) when (IsProfileFailure(ex))
        {
            SetFailure(ex);
        }

        Logger.Information("Profile initialization finished. ProfileCount={ProfileCount}, LocalProfileId={LocalProfileId}, StatusKey={StatusKey}", Profiles.Count, Active?.LocalId, StatusKey);
    }

    public async Task SaveAsync(bool? rememberSecrets = null)
    {
        await gate.WaitAsync(lifetime.Token);
        try
        {
            await SaveCurrentAsync(rememberSecrets);
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>Flushes the latest draft before the window closes, leaving an incomplete checkpoint unchanged.</summary>
    public async Task<bool> FlushBeforeCloseAsync()
    {
        debounce?.Cancel();
        if (!await gate.WaitAsync(TimeSpan.FromSeconds(5)))
        {
            Logger.Warning("Profile save before closing timed out while waiting for another operation.");
            return false;
        }
        try
        {
            if (Active is not null && editVersion != persistedEditVersion) await SaveCurrentAsync();
            return editVersion == persistedEditVersion;
        }
        catch (Exception ex) when (IsProfileFailure(ex)) { SetFailure(ex); return false; }
        finally { gate.Release(); }
    }

    public async Task SaveAsCopyAsync(string displayName)
    {
        EnsureCanActivate();
        await gate.WaitAsync(lifetime.Token);
        try
        {
            if (Active is not null && editVersion != persistedEditVersion)
            {
                try { await SaveCurrentAsync(); }
                catch (IncompleteProfileCheckpointException)
                {
                    Logger.Warning("Creating a new copy while the remembered checkpoint remains incomplete. LocalProfileId={LocalProfileId}", Active.LocalId);
                }
            }
            Guid id = Guid.NewGuid();
            long version = editVersion;
            bool remember = Active?.RememberSecrets ?? true;
            DeploymentProfileDocument profile = await session.CaptureAsync(id, displayName, remember);
            try
            {
                EnsureUnchanged(version);
                // A new profile can retain available values while its first draft is incomplete.
                // Later saves preserve this checkpoint until all required inputs are complete.
                LocalProfileDescriptor descriptor = local.Save(id, profile, remember, null);
                local.SetActive(id);
                Active = descriptor;
                persistedEditVersion = editVersion;
                ClearSharedKey();
                ClearConflict();
                await RefreshAsync();
            }
            finally
            {
                DeploymentProfileSecretBinding.Clear(profile);
            }
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task ActivateAsync(Guid localId)
    {
        EnsureCanActivate();
        await gate.WaitAsync(lifetime.Token);
        try
        {
            if (Active is not null && editVersion != persistedEditVersion) await SaveCurrentAsync();
            LocalProfileSnapshot snapshot = await Task.Run(() => local.Read(localId));
            try
            {
                await ActivateDocumentAsync(snapshot.Profile, snapshot.Descriptor);
                await Task.Run(() => local.SetActive(localId));
            }
            finally
            {
                DeploymentProfileSecretBinding.Clear(snapshot.Profile);
            }

            await RefreshAsync();
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task ForgetAsync()
    {
        EnsureCanActivate();
        await gate.WaitAsync(lifetime.Token);
        try
        {
            LocalProfileDescriptor current = RequireActive();
            DeploymentProfileDocument profile = new()
            {
                ProfileId = current.ProfileId,
                DisplayName = current.DisplayName,
                Configuration = configuration.Current
            };
            LocalProfileEnrollment? enrollment = current.Enrollment is null ? null : current.Enrollment with
            {
                RememberSharedKey = false,
                IsEnabled = false,
                PendingOperationId = null,
                PendingConnectionFile = false,
                IsDirty = true
            };
            Active = local.Save(current.LocalId, profile, false, current.Revision, enrollment);
            CleanupLocalTransferFiles(current);
            debounce?.Cancel();
            ClearSharedKey();
            try
            {
                applying = true;
                session.Activate(profile, profile.Configuration);
                persistedEditVersion = editVersion;
            }
            finally
            {
                session.ClearSensitiveState();
                applying = false;
                ClearStagingDirectories();
                DeploymentProfileSecretBinding.Clear(profile);
            }

            ClearConflict();
            ClearStagingDirectories();
            await RefreshAsync();
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task RenameAsync(string displayName)
    {
        await gate.WaitAsync(lifetime.Token);
        try
        {
            LocalProfileDescriptor current = RequireActive();
            long version = editVersion;
            DeploymentProfileDocument profile = await session.CaptureAsync(current.ProfileId, displayName, current.RememberSecrets);
            try
            {
                if (current.RememberSecrets) EnsureCompleteCheckpoint(profile);
                LocalProfileEnrollment? enrollment = current.Enrollment is null ? null : current.Enrollment with { IsDirty = true };
                Active = await Task.Run(() => local.Save(current.LocalId, profile, current.RememberSecrets, current.Revision, enrollment));
                persistedEditVersion = version;
                await RefreshAsync();
            }
            finally { DeploymentProfileSecretBinding.Clear(profile); }
        }
        finally { gate.Release(); }
    }

    public async Task DeleteAsync(bool deleteShared = false)
    {
        EnsureCanActivate();
        await gate.WaitAsync(lifetime.Token);
        try
        {
            LocalProfileDescriptor current = RequireActive();
            if (deleteShared && current.Enrollment is { } enrollment)
            {
                byte[] key = await GetSharedKeyAsync();
                try
                {
                    using SharedProfileRepository remote = OpenRemote(enrollment, key);
                    Guid expected = enrollment.KnownRevisionId ?? throw new InvalidOperationException("Synchronize the profile before deleting it.");
                    SharedProfileRepositoryResult loaded = await remote.LoadAsync(expected, lifetime.Token);
                    RequireSuccess(loaded);
                    if (loaded.Snapshot?.Head.IsTombstone != true)
                        RequireSuccess(await remote.TombstoneAsync(expected, Guid.NewGuid(), lifetime.Token));
                }
                finally { CryptographicOperations.ZeroMemory(key); }
            }
            bool cleanupPending = await Task.Run(() => local.Delete(current.LocalId, current.Revision));
            CleanupLocalTransferFiles(current);
            debounce?.Cancel();
            applying = true;
            Active = null;
            ClearSharedKey();
            session.ClearSensitiveState();
            ClearStagingDirectories();
            var empty = new DeploymentProfileDocument
            {
                ProfileId = Guid.NewGuid(),
                DisplayName = "Default",
                Configuration = new()
                {
                    General = new() { IsoOutputPath = configuration.Current.General.IsoOutputPath },
                    Telemetry = configuration.Current.Telemetry
                }
            };
            session.Activate(empty, empty.Configuration);
            persistedEditVersion = editVersion;
            ClearConflict();
            await RefreshAsync();
            if (cleanupPending)
            {
                StatusKey = "Profiles.CleanupPending";
                Changed?.Invoke(this, EventArgs.Empty);
            }
        }
        finally { applying = false; gate.Release(); }
    }

    public async Task ExportAsync(string path, string passphrase, bool includeSecrets)
    {
        LocalProfileDescriptor current = RequireActive();
        DeploymentProfileDocument profile = await session.CaptureAsync(current.ProfileId, current.DisplayName, includeSecrets);
        try
        {
            byte[] bytes = await Task.Run(() => packages.Export(profile, passphrase.AsSpan()));
            await WriteAtomicAsync(path, bytes);
        }
        finally
        {
            DeploymentProfileSecretBinding.Clear(profile);
        }
    }

    /// <summary>Decrypts and validates before the UI offers activation; imported data cannot change the current profile.</summary>
    public async Task<DeploymentProfileDocument> PreviewImportAsync(string path, string passphrase)
    {
        byte[] bytes = await ReadBoundedAsync(path, 16 * 1024 * 1024 + 118);
        return await Task.Run(() => packages.Import(bytes, passphrase.AsSpan()));
    }

    public async Task ImportAsCopyAsync(DeploymentProfileDocument imported, bool rememberSecrets)
    {
        EnsureCanActivate();
        await gate.WaitAsync(lifetime.Token);
        try
        {
            if (Active is not null && editVersion != persistedEditVersion) await SaveCurrentAsync();
            long version = editVersion;
            DeploymentProfileDocument profile = imported with
            {
                ProfileId = Guid.NewGuid(),
                Secrets = new DeploymentProfileSecrets
                {
                    Entries = imported.Secrets.Entries.Where(secret => secret.Purpose != ProfileSecretPurpose.SharedProfileKey).ToArray()
                }
            };
            string directory = CreateStagingDirectory();
            var materialized = await Task.Run(() => DeploymentProfileAssetService.Materialize(profile, directory));
            materialized = materialized with
            {
                General = materialized.General with { IsoOutputPath = configuration.Current.General.IsoOutputPath },
                Telemetry = configuration.Current.Telemetry
            };
            profile = profile with { Configuration = materialized };
            if (rememberSecrets) EnsureCompleteCheckpoint(profile);
            EnsureUnchanged(version);
            LocalProfileDescriptor descriptor = local.Save(Guid.NewGuid(), profile, rememberSecrets, null);
            applying = true;
            session.Activate(profile, materialized);
            Active = descriptor;
            persistedEditVersion = editVersion;
            local.SetActive(descriptor.LocalId);
            ClearSharedKey();
            ClearConflict();
            await RefreshAsync();
        }
        finally
        {
            applying = false;
            gate.Release();
        }
    }

    private async Task SaveCurrentAsync(bool? rememberSecrets = null, LocalProfileEnrollment? enrollment = null, byte[]? sharedKey = null,
        long? comparedEditVersion = null, string? displayName = null, bool clearEnrollment = false)
    {
        LocalProfileDescriptor current = RequireActive();
        long version = editVersion;
        bool remember = rememberSecrets ?? current.RememberSecrets;
        Logger.Debug("Local profile checkpoint started. LocalProfileId={LocalProfileId}, RememberSecrets={RememberSecrets}, EditVersion={EditVersion}", current.LocalId, remember, version);
        DeploymentProfileDocument profile = await session.CaptureAsync(current.ProfileId, displayName ?? current.DisplayName, remember);
        try
        {
            if (remember) EnsureCompleteCheckpoint(profile);
            LocalProfileEnrollment? updated = clearEnrollment ? null : enrollment ?? current.Enrollment;
            if (updated is not null && (enrollment is null && HasSharedEditsSince(persistedEditVersion) ||
                comparedEditVersion is long compared && HasSharedEditsSince(compared)))
                updated = updated with { IsDirty = true };
            Active = await Task.Run(() => local.Save(current.LocalId, profile, remember, current.Revision, updated,
                updated?.RememberSharedKey == true ? sharedKey : null));
            persistedEditVersion = version;
            await RefreshAsync();
            Logger.Debug("Local profile checkpoint completed. LocalProfileId={LocalProfileId}, Revision={Revision}, CleanupPending={CleanupPending}", Active?.LocalId, Active?.Revision, Active?.CleanupPending);
        }
        finally
        {
            DeploymentProfileSecretBinding.Clear(profile);
        }
    }

    private async Task ActivateDocumentAsync(DeploymentProfileDocument profile, LocalProfileDescriptor descriptor)
    {
        EnsureCanActivate();
        long version = editVersion;
        var document = profile.Configuration;
        if (profile.Assets.Any(asset => asset.State == ProfileValueState.Present))
        {
            string directory = CreateStagingDirectory();
            document = await Task.Run(() => DeploymentProfileAssetService.Materialize(profile, directory));
        }

        document = document with { Telemetry = configuration.Current.Telemetry };
        EnsureUnchanged(version);
        applying = true;
        try
        {
            session.Activate(profile, document);
            Active = descriptor;
            persistedEditVersion = editVersion;
            ClearSharedKey();
            ClearConflict();
        }
        finally
        {
            applying = false;
        }
    }

    private void OnEdited(object? sender, EventArgs e)
    {
        bool affectsSharedContent = true;
        if (ReferenceEquals(sender, configuration))
        {
            if (ReferenceEquals(observedConfiguration, configuration.Current)) return;
            affectsSharedContent = observedConfiguration is null || HasSharedConfigurationChanges(observedConfiguration, configuration.Current);
            observedConfiguration = configuration.Current;
        }
        if (applying || Active is null || !initialized) return;
        editVersion++;
        if (affectsSharedContent) lastSharedEditVersion = editVersion;
        SynchronizationStateChanged?.Invoke(this, EventArgs.Empty);
        lastEditTick = Environment.TickCount64;
        debounce?.Cancel();
        debounce?.Dispose();
        debounce = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        _ = AutosaveAsync(debounce.Token);
    }

    private bool HasSharedEditsSince(long version) => lastSharedEditVersion > version;

    // Replaced sections can carry session-only secret changes even when their persisted values are equal.
    private static bool HasSharedConfigurationChanges(
        Foundry.Core.Models.Configuration.FoundryConfigurationDocument previous,
        Foundry.Core.Models.Configuration.FoundryConfigurationDocument current) =>
        previous.SchemaVersion != current.SchemaVersion ||
        !ReferenceEquals(previous.Network, current.Network) ||
        !ReferenceEquals(previous.OperatingSystemSelection, current.OperatingSystemSelection) ||
        !ReferenceEquals(previous.Localization, current.Localization) ||
        !ReferenceEquals(previous.Customization, current.Customization) ||
        !ReferenceEquals(previous.Unattend, current.Unattend) ||
        !ReferenceEquals(previous.Autopilot, current.Autopilot) ||
        previous.General with
        {
            IsoOutputPath = current.General.IsoOutputPath,
            CustomDriverDirectoryPath = current.General.CustomDriverDirectoryPath
        } != current.General;

    private async Task AutosaveAsync(CancellationToken token)
    {
        try
        {
            await Task.Delay(750, token);
            if (token.IsCancellationRequested) return;
            await gate.WaitAsync(token);
            try
            {
                if (Active is null || editVersion == persistedEditVersion) return;
                await SaveCurrentAsync();
            }
            finally
            {
                gate.Release();
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) when (IsProfileFailure(ex)) { SetFailure(ex); }
    }

    private async Task RefreshAsync()
    {
        Profiles = await Task.Run(local.List);
        if (Active is { } current)
        {
            Active = Profiles.FirstOrDefault(profile => profile.LocalId == current.LocalId && profile.Revision == current.Revision) ?? current;
        }
        StatusKey = IsSharedProfileDeleted ? "Profiles.DeletedRemote" : HasConflict ? "Profiles.Conflict" :
            Active?.CleanupPending == true ? "Profiles.CleanupPending" : "Profiles.Ready";
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private LocalProfileDescriptor RequireActive() => Active ?? throw new InvalidOperationException("No local profile is selected.");
    private void EnsureUnchanged(long version)
    {
        if (editVersion != version) throw new InvalidOperationException("The active draft changed. Retry the profile action.");
        EnsureCanActivate();
    }
    private void EnsureCanActivate()
    {
        if (navigationGuard.State == ShellNavigationState.OperationRunning ||
            navigationGuard.State == ShellNavigationState.InteractionPending && !ownsNavigationGuard)
            throw new InvalidOperationException("Profile activation waits until the current operation finishes.");
    }

    private static bool IsProfileFailure(Exception exception) => exception is IOException or InvalidDataException or UnauthorizedAccessException or
        System.ComponentModel.Win32Exception or CryptographicException or ArgumentException or InvalidOperationException or NotSupportedException or
        System.Text.Json.JsonException or FormatException or System.Runtime.InteropServices.COMException;

    internal void SetFailure(Exception exception, [System.Runtime.CompilerServices.CallerMemberName] string operation = "")
    {
        Serilog.Events.LogEventLevel level = exception is LocalProfileLockedException or IncompleteProfileCheckpointException or SharedProfileOperationException
            ? Serilog.Events.LogEventLevel.Warning : Serilog.Events.LogEventLevel.Error;
        Logger.Write(level, exception, "Profile operation failed. Operation={Operation}, LocalProfileId={LocalProfileId}, SharedStatus={SharedStatus}",
            operation, Active?.LocalId, (exception as SharedProfileOperationException)?.Status);
        StatusKey = exception is SharedProfileOperationException shared ? RemoteStatusKey(shared.Status) :
            exception is LocalProfileLockedException ? "Profiles.Locked" :
            exception is IncompleteProfileCheckpointException ? "Profiles.Incomplete" :
            exception is NotSupportedException ? "Profiles.Unsupported" : "Profiles.Failed";
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void ClearSharedKey()
    {
        if (sessionSharedKey is not null) CryptographicOperations.ZeroMemory(sessionSharedKey);
        sessionSharedKey = null;
    }

    private static void EnsureCompleteCheckpoint(DeploymentProfileDocument profile)
    {
        if (profile.Secrets.Entries.Any(secret => secret.State is ProfileValueState.Unavailable or ProfileValueState.Omitted) ||
            profile.Assets.Any(asset => asset.State is ProfileValueState.Unavailable or ProfileValueState.Omitted))
        {
            Logger.Warning("Profile checkpoint is incomplete. UnavailableSecrets={UnavailableSecrets}, UnavailableAssets={UnavailableAssets}",
                profile.Secrets.Entries.Count(secret => secret.State is ProfileValueState.Unavailable or ProfileValueState.Omitted),
                profile.Assets.Count(asset => asset.State is ProfileValueState.Unavailable or ProfileValueState.Omitted));
            throw new IncompleteProfileCheckpointException();
        }
    }

    private static Task<byte[]> ReadBoundedAsync(string path, int maximum) => Task.Run(async () =>
    {
        using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.Asynchronous);
        if (stream.Length > maximum) throw new InvalidDataException("The profile file exceeds the supported size.");
        byte[] bytes = new byte[checked((int)stream.Length)];
        await stream.ReadExactlyAsync(bytes);
        return bytes;
    });

    private static Task WriteAtomicAsync(string path, byte[] bytes) => Task.Run(async () =>
    {
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (FileStream stream = new(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, FileOptions.Asynchronous))
            {
                await stream.WriteAsync(bytes);
                stream.Flush(true);
            }
            File.Move(temporary, path, true);
        }
        finally { File.Delete(temporary); }
    });

    public void Dispose()
    {
        lifetime.Cancel();
        debounce?.Cancel();
        debounce?.Dispose();
        ClearSharedKey();
        ClearStagingDirectories(releaseFailedLeases: true);
        lifetime.Dispose();
    }

    private sealed class ActivationSuspension(DeploymentProfileCoordinator owner) : IDisposable
    {
        private bool disposed;
        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            owner.activationSuspensions--;
            if (owner.activationSuspensions == 0 && owner.ownsNavigationGuard)
            {
                owner.ownsNavigationGuard = false;
                owner.navigationGuard.SetState(owner.suspendedNavigationState);
            }
        }
    }
}

/// <summary>Preserves the last complete remembered revision while the operator finishes a secret-bearing draft.</summary>
public sealed class IncompleteProfileCheckpointException() : InvalidOperationException("Complete required passwords and source files before remembering or publishing secrets.");
