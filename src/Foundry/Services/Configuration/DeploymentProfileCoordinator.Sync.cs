// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Foundry.Core.Models.Profiles;
using Foundry.Core.Services.Profiles;
using Foundry.Services.Shell;
using Foundry.Utilities.Security;

namespace Foundry.Services.Configuration;

public sealed partial class DeploymentProfileCoordinator
{
    private Guid? conflictRevision;

    public async Task CreateSharedAsync(string rootPath, bool includeSecrets, bool rememberKey)
    {
        EnsureCanActivate();
        ValidateSharedPath(rootPath);
        await gate.WaitAsync(lifetime.Token);
        try
        {
            LocalProfileDescriptor current = RequireActive();
            byte[] key = RandomNumberGenerator.GetBytes(32);
            try
            {
                LocalProfileEnrollment enrollment = new()
                {
                    RootPath = rootPath,
                    RepositoryId = Guid.NewGuid(),
                    ProfileId = current.ProfileId,
                    KeyEpoch = 1,
                    IsDirty = true,
                    IsEnabled = true,
                    IncludeSecrets = includeSecrets,
                    RememberSharedKey = rememberKey
                };
                using SharedProfileRepository remote = OpenRemote(enrollment, key);
                RequireSuccess(await remote.InitializeAsync(lifetime.Token));
                await SaveCurrentAsync(enrollment: enrollment, sharedKey: key);
                ClearSharedKey();
                sessionSharedKey = key.ToArray();
            }
            finally { CryptographicOperations.ZeroMemory(key); }
        }
        finally { gate.Release(); }

        await SynchronizeAsync();
    }

    /// <summary>Exports the shared key only into an explicitly requested passphrase-protected recovery package.</summary>
    public async Task ExportRecoveryAsync(string path, string passphrase)
    {
        LocalProfileDescriptor current = RequireActive();
        LocalProfileEnrollment enrollment = current.Enrollment ?? throw new InvalidOperationException("The profile is not shared.");
        string absolute = Path.GetFullPath(path);
        string sharedRoot = Path.GetFullPath(enrollment.RootPath).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (absolute.StartsWith(sharedRoot, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Store the recovery package outside the shared repository.");
        byte[] key = await GetSharedKeyAsync();
        DeploymentProfileDocument profile = await session.CaptureAsync(current.ProfileId, current.DisplayName, enrollment.IncludeSecrets);
        try
        {
            var invitation = new SharedInvitation(1, enrollment.RepositoryId, current.ProfileId, enrollment.KeyEpoch, enrollment.IncludeSecrets, Convert.ToBase64String(key));
            DeploymentProfileSecret recovery = new()
            {
                Purpose = ProfileSecretPurpose.SharedProfileKey,
                Identity = "shared-enrollment",
                State = ProfileValueState.Present,
                Value = JsonSerializer.SerializeToUtf8Bytes(invitation)
            };
            profile = profile with { Secrets = new DeploymentProfileSecrets { Entries = profile.Secrets.Entries.Append(recovery).ToArray() } };
            await WriteAtomicAsync(path, await Task.Run(() => packages.Export(profile, passphrase.AsSpan())));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
            DeploymentProfileSecretBinding.Clear(profile);
        }
    }

    public async Task JoinSharedAsync(DeploymentProfileDocument invitationProfile, string rootPath, bool rememberSecrets, bool rememberKey)
    {
        EnsureCanActivate();
        ValidateSharedPath(rootPath);
        DeploymentProfileSecret record = invitationProfile.Secrets.Entries.Single(secret => secret.Purpose == ProfileSecretPurpose.SharedProfileKey);
        SharedInvitation invitation = JsonSerializer.Deserialize<SharedInvitation>(record.Value!) ?? throw new InvalidDataException("Invalid recovery package.");
        if (invitation.FormatVersion != 1 || invitation.RepositoryId == Guid.Empty || invitation.ProfileId != invitationProfile.ProfileId || invitation.KeyEpoch < 1)
            throw new InvalidDataException("Invalid recovery identity.");
        byte[] key = Convert.FromBase64String(invitation.Key);
        if (key.Length != 32) throw new InvalidDataException("Invalid recovery key.");
        await gate.WaitAsync(lifetime.Token);
        try
        {
            if (Active is not null && editVersion != persistedEditVersion) await SaveCurrentAsync();
            long version = editVersion;
            LocalProfileEnrollment enrollment = new()
            {
                RootPath = rootPath,
                RepositoryId = invitation.RepositoryId,
                ProfileId = invitation.ProfileId,
                KeyEpoch = invitation.KeyEpoch,
                RememberSharedKey = rememberKey,
                IsEnabled = true,
                IncludeSecrets = invitation.IncludeSecrets
            };
            using SharedProfileRepository remote = OpenRemote(enrollment, key);
            SharedProfileRepositoryResult result = await remote.LoadAsync(cancellationToken: lifetime.Token);
            RequireSuccess(result);
            SharedProfileSnapshot head = result.Snapshot ?? throw new InvalidDataException("The shared profile has no revision.");
            if (head.Head.IsTombstone) throw new InvalidDataException("The shared profile was deleted.");
            DeploymentProfileDocument profile = packages.Decrypt(head.EncryptedPayload, key, ProfilePackagePurpose.SharedRevision, SharedContext(enrollment));
            try
            {
                if (profile.ProfileId != enrollment.ProfileId) throw new InvalidDataException("The shared profile identity does not match.");
                enrollment = enrollment with { KnownRevisionId = head.Head.RevisionId };
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
                LocalProfileDescriptor descriptor = local.Save(Guid.NewGuid(), profile, rememberSecrets, null, enrollment, rememberKey ? key : null);
                applying = true;
                session.Activate(profile, materialized);
                Active = descriptor;
                persistedEditVersion = editVersion;
                local.SetActive(descriptor.LocalId);
                ClearSharedKey();
                sessionSharedKey = key.ToArray();
                HasConflict = false;
                await RefreshAsync();
            }
            finally { DeploymentProfileSecretBinding.Clear(profile); }
        }
        finally
        {
            applying = false;
            CryptographicOperations.ZeroMemory(key);
            gate.Release();
        }
    }

    public async Task SetSynchronizationEnabledAsync(bool enabled)
    {
        await gate.WaitAsync(lifetime.Token);
        try
        {
            LocalProfileEnrollment enrollment = RequireActive().Enrollment ?? throw new InvalidOperationException("The profile is not shared.");
            await SaveCurrentAsync(enrollment: enrollment with { IsEnabled = enabled });
        }
        finally { gate.Release(); }
    }

    public Task SynchronizeAsync() => SynchronizeAsync(false);

    private async Task SynchronizeAsync(bool automatic)
    {
        if (Active?.Enrollment is not { } enrollment || automatic && !enrollment.IsEnabled ||
            navigationGuard.State == ShellNavigationState.OperationRunning ||
            navigationGuard.State == ShellNavigationState.InteractionPending && (automatic || !ownsNavigationGuard)) return;
        if (automatic && Environment.TickCount64 - lastEditTick < 2000) return;
        if (!await gate.WaitAsync(0)) return;
        try
        {
            await SynchronizeCoreAsync(automatic);
        }
        catch (Exception ex) when (IsProfileFailure(ex)) { SetFailure(ex); }
        finally { gate.Release(); }
    }

    private async Task SynchronizeCoreAsync(bool automatic)
    {
        LocalProfileDescriptor current = RequireActive();
        LocalProfileEnrollment enrollment = current.Enrollment!;
        byte[] key = await GetSharedKeyAsync();
        try
        {
            using SharedProfileRepository remote = OpenRemote(enrollment, key);
            SharedProfileRepositoryResult result = await remote.LoadAsync(enrollment.KnownRevisionId, lifetime.Token);
            if (result.Status != SharedProfileRepositoryStatus.Success) { SetRemoteStatus(result.Status); return; }

            if (enrollment.PendingOperationId is Guid operationId)
            {
                SharedProfileRepositoryResult reconciliation = await remote.ReconcileAsync(operationId, enrollment.KnownRevisionId, lifetime.Token);
                if (reconciliation.Status == SharedProfileRepositoryStatus.Success)
                {
                    enrollment = enrollment with { KnownRevisionId = reconciliation.CommittedRevisionId, PendingOperationId = null, IsDirty = true };
                    await SaveCurrentAsync(enrollment: enrollment);
                    File.Delete(OutboxPath(current.LocalId, operationId));
                    result = await remote.LoadAsync(enrollment.KnownRevisionId, lifetime.Token);
                    if (result.Status != SharedProfileRepositoryStatus.Success) { SetRemoteStatus(result.Status); return; }
                }
                else if (reconciliation.Status == SharedProfileRepositoryStatus.NotCommitted)
                {
                    byte[] pending = await ReadBoundedAsync(OutboxPath(current.LocalId, operationId), 16 * 1024 * 1024 + 118);
                    result = await remote.PublishAsync(pending, enrollment.KnownRevisionId, operationId, lifetime.Token);
                    if (result.Status != SharedProfileRepositoryStatus.Success) { SetRemoteStatus(result.Status, result.Snapshot); return; }
                    enrollment = enrollment with { KnownRevisionId = result.CommittedRevisionId, PendingOperationId = null, IsDirty = true };
                    await SaveCurrentAsync(enrollment: enrollment);
                    File.Delete(OutboxPath(current.LocalId, operationId));
                }
                else { SetRemoteStatus(reconciliation.Status); return; }
            }

            if (result.Snapshot?.Head.IsTombstone == true)
            {
                HasConflict = true;
                conflictRevision = result.Snapshot.Head.RevisionId;
                StatusKey = "Profiles.DeletedRemote";
                Changed?.Invoke(this, EventArgs.Empty);
                return;
            }

            bool remoteChanged = result.Snapshot?.Head.RevisionId != enrollment.KnownRevisionId;
            bool dirty = enrollment.IsDirty || editVersion != persistedEditVersion;
            if (remoteChanged && dirty) { SetRemoteStatus(SharedProfileRepositoryStatus.Conflict, result.Snapshot); return; }
            if (remoteChanged && result.Snapshot is not null)
            {
                if (automatic && (!IsSettingsOpen || activationSuspensions > 0))
                {
                    StatusKey = "Profiles.UpdateAvailable";
                    Changed?.Invoke(this, EventArgs.Empty);
                    return;
                }
                await AcceptRemoteAsync(result.Snapshot, enrollment, key, automatic);
            }
            else if (dirty)
            {
                await PublishCurrentAsync(remote, enrollment, key);
            }
            else
            {
                StatusKey = "Profiles.Synchronized";
                Changed?.Invoke(this, EventArgs.Empty);
            }
        }
        finally { CryptographicOperations.ZeroMemory(key); }
    }

    private async Task PublishCurrentAsync(SharedProfileRepository remote, LocalProfileEnrollment enrollment, byte[] key)
    {
        LocalProfileDescriptor current = RequireActive();
        long version = editVersion;
        DeploymentProfileDocument profile = await session.CaptureAsync(current.ProfileId, current.DisplayName, enrollment.IncludeSecrets);
        try
        {
            if (enrollment.IncludeSecrets) EnsureCompleteCheckpoint(profile);
            byte[] payload = await Task.Run(() => packages.Encrypt(profile, key, ProfilePackagePurpose.SharedRevision, SharedContext(enrollment)));
            Guid operationId = Guid.NewGuid();
            await WriteAtomicAsync(OutboxPath(current.LocalId, operationId), payload);
            await SaveCurrentAsync(enrollment: enrollment with { PendingOperationId = operationId, IsDirty = true });
            SharedProfileRepositoryResult result = await remote.PublishAsync(payload, enrollment.KnownRevisionId, operationId, lifetime.Token);
            if (result.Status != SharedProfileRepositoryStatus.Success) { SetRemoteStatus(result.Status, result.Snapshot); return; }
            await SaveCurrentAsync(enrollment: enrollment with
            {
                KnownRevisionId = result.CommittedRevisionId,
                PendingOperationId = null,
                IsDirty = editVersion != version
            });
            File.Delete(OutboxPath(current.LocalId, operationId));
            HasConflict = false;
            StatusKey = "Profiles.Synchronized";
            Changed?.Invoke(this, EventArgs.Empty);
        }
        finally { DeploymentProfileSecretBinding.Clear(profile); }
    }

    public async Task ResolveConflictAsync(bool useRemote)
    {
        EnsureCanActivate();
        await gate.WaitAsync(lifetime.Token);
        try
        {
            LocalProfileEnrollment enrollment = RequireActive().Enrollment ?? throw new InvalidOperationException("The profile is not shared.");
            byte[] key = await GetSharedKeyAsync();
            try
            {
                using SharedProfileRepository remote = OpenRemote(enrollment, key);
                SharedProfileRepositoryResult result = await remote.LoadAsync(enrollment.KnownRevisionId, lifetime.Token);
                RequireSuccess(result);
                if (result.Snapshot?.Head.RevisionId != conflictRevision)
                {
                    SetRemoteStatus(SharedProfileRepositoryStatus.Conflict, result.Snapshot);
                    return;
                }
                SharedProfileSnapshot snapshot = result.Snapshot ?? throw new InvalidDataException("The shared revision is missing.");
                if (snapshot.Head.IsTombstone) throw new InvalidOperationException("Save the local profile as a copy to preserve your changes.");
                if (useRemote) await AcceptRemoteAsync(snapshot, enrollment, key);
                else
                {
                    enrollment = enrollment with { KnownRevisionId = snapshot.Head.RevisionId, PendingOperationId = null, IsDirty = true };
                    await SaveCurrentAsync(enrollment: enrollment);
                    await PublishCurrentAsync(remote, enrollment, key);
                }
            }
            finally { CryptographicOperations.ZeroMemory(key); }
        }
        finally { gate.Release(); }
    }

    private async Task AcceptRemoteAsync(SharedProfileSnapshot snapshot, LocalProfileEnrollment enrollment, byte[] key, bool automatic = false)
    {
        EnsureCanActivate();
        LocalProfileDescriptor current = RequireActive();
        long version = editVersion;
        DeploymentProfileDocument profile = packages.Decrypt(snapshot.EncryptedPayload, key, ProfilePackagePurpose.SharedRevision, SharedContext(enrollment));
        try
        {
            if (profile.ProfileId != enrollment.ProfileId) throw new InvalidDataException("The shared profile identity does not match.");
            DeploymentProfileDocument localProfile = await session.CaptureAsync(current.ProfileId, current.DisplayName, true);
            try
            {
                DeploymentProfileDocument merged = DeploymentProfileMerge.PreserveOmittedLocalValues(profile, localProfile);
                DeploymentProfileSecretBinding.Clear(profile);
                profile = merged;
            }
            finally { DeploymentProfileSecretBinding.Clear(localProfile); }
            string directory = CreateStagingDirectory();
            var document = await Task.Run(() => DeploymentProfileAssetService.Materialize(profile, directory));
            if (version != editVersion) { SetRemoteStatus(SharedProfileRepositoryStatus.Conflict, snapshot); return; }
            if (automatic && (!IsSettingsOpen || activationSuspensions > 0 || navigationGuard.State == ShellNavigationState.OperationRunning)) return;
            document = document with
            {
                General = document.General with
                {
                    IsoOutputPath = configuration.Current.General.IsoOutputPath,
                    CustomDriverDirectoryPath = configuration.Current.General.CustomDriverDirectoryPath
                },
                Telemetry = configuration.Current.Telemetry
            };
            profile = profile with { Configuration = document };
            if (current.RememberSecrets) EnsureCompleteCheckpoint(profile);
            enrollment = enrollment with { KnownRevisionId = snapshot.Head.RevisionId, PendingOperationId = null, IsDirty = false };
            LocalProfileDescriptor descriptor = local.Save(current.LocalId, profile, current.RememberSecrets, current.Revision, enrollment);
            applying = true;
            try { session.Activate(profile, document); }
            catch
            {
                Active = null;
                ClearSharedKey();
                debounce?.Cancel();
                throw;
            }
            Active = descriptor;
            persistedEditVersion = editVersion;
            HasConflict = false;
            await RefreshAsync();
        }
        finally
        {
            applying = false;
            DeploymentProfileSecretBinding.Clear(profile);
        }
    }

    private async Task<byte[]> GetSharedKeyAsync()
    {
        if (sessionSharedKey is not null) return sessionSharedKey.ToArray();
        LocalProfileDescriptor current = RequireActive();
        using WindowsCredential? credential = await Task.Run(() => local.ReadSharedKey(current.LocalId));
        return credential?.Secret.ToArray() ?? throw new LocalProfileLockedException(current.LocalId);
    }

    private async Task PollAsync()
    {
        using PeriodicTimer timer = new(TimeSpan.FromSeconds(30));
        try
        {
            while (await timer.WaitForNextTickAsync(lifetime.Token))
            {
                Task? synchronization = null;
                await dispatcher.EnqueueAsync(() => synchronization = SynchronizeAsync(true));
                await synchronization!;
            }
        }
        catch (OperationCanceledException) { }
    }

    private static SharedProfileRepository OpenRemote(LocalProfileEnrollment enrollment, byte[] key) =>
        new(enrollment.RootPath, enrollment.RepositoryId, enrollment.ProfileId, enrollment.KeyEpoch, key,
            new SharedProfileRepositoryOptions { MaximumPayloadBytes = 16 * 1024 * 1024 + 118, MaximumHistoryCount = 4096 });

    private static byte[] SharedContext(LocalProfileEnrollment enrollment) =>
        Encoding.UTF8.GetBytes($"{enrollment.RepositoryId:N}/{enrollment.ProfileId:N}/{enrollment.KeyEpoch}");

    private static string OutboxPath(Guid localId, Guid operationId)
    {
        string directory = Path.Combine(Constants.DeploymentProfilesDirectoryPath, "Outbox", localId.ToString("N"));
        Directory.CreateDirectory(directory);
        return Path.Combine(directory, operationId.ToString("N") + ".profile");
    }

    private static void ValidateSharedPath(string path)
    {
        if (!path.StartsWith(@"\\", StringComparison.Ordinal) || path.StartsWith(@"\\?", StringComparison.Ordinal) || path.StartsWith(@"\\.", StringComparison.Ordinal))
            throw new ArgumentException("Select an authenticated SMB shared folder using its UNC path.");
    }

    private static void RequireSuccess(SharedProfileRepositoryResult result)
    {
        if (result.Status == SharedProfileRepositoryStatus.UnsupportedFormat) throw new NotSupportedException("The shared profile format is unsupported.");
        if (result.Status != SharedProfileRepositoryStatus.Success) throw new IOException("The shared profile operation could not be completed.");
    }

    private void SetRemoteStatus(SharedProfileRepositoryStatus status, SharedProfileSnapshot? snapshot = null)
    {
        HasConflict = status == SharedProfileRepositoryStatus.Conflict;
        if (HasConflict) conflictRevision = snapshot?.Head.RevisionId;
        StatusKey = status switch
        {
            SharedProfileRepositoryStatus.Conflict => "Profiles.Conflict",
            SharedProfileRepositoryStatus.Busy or SharedProfileRepositoryStatus.Unavailable => "Profiles.Offline",
            SharedProfileRepositoryStatus.RollbackDetected => "Profiles.Rollback",
            SharedProfileRepositoryStatus.HistoryLimitExceeded => "Profiles.HistoryLimit",
            SharedProfileRepositoryStatus.UnsupportedFormat => "Profiles.Unsupported",
            _ => "Profiles.Failed"
        };
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private sealed record SharedInvitation(int FormatVersion, Guid RepositoryId, Guid ProfileId, int KeyEpoch, bool IncludeSecrets, string Key);
}
