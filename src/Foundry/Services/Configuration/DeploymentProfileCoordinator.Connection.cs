// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Security.Cryptography;
using System.Text.Json;
using Foundry.Core.Models.Profiles;
using Foundry.Core.Services.Profiles;

namespace Foundry.Services.Configuration;

public sealed partial class DeploymentProfileCoordinator
{
    /// <summary>Indicates when a connection file is needed to restore access without replacing the local draft.</summary>
    public bool RequiresSharedAccess => Active?.Enrollment is { } enrollment && sessionSharedKey is null &&
        (!enrollment.RememberSharedKey || StatusKey == "Profiles.Locked");

    /// <summary>Returns an optional authenticated package hint; legacy connection files still require a folder selection.</summary>
    public static string? GetSharedFolderHint(DeploymentProfileDocument profile, string? sourcePath = null)
    {
        SharedInvitation invitation = ReadSharedInvitation(profile);
        if (string.IsNullOrWhiteSpace(invitation.RootPath)) return null;
        return SharedProfileLocation.ResolveConnectionFolder(invitation.RootPath, sourcePath);
    }

    /// <summary>Removes local enrollment while retaining current settings and the password-retention policy; shared files are untouched.</summary>
    public async Task DisconnectAsync()
    {
        EnsureCanActivate();
        await gate.WaitAsync(lifetime.Token);
        try
        {
            LocalProfileDescriptor current = RequireActive();
            if (current.Enrollment is null) return;
            Logger.Information("Disconnecting shared configuration. LocalProfileId={LocalProfileId}", current.LocalId);
            try
            {
                await SaveCurrentAsync(clearEnrollment: true);
            }
            finally
            {
                if (Active?.Enrollment is null)
                {
                    ClearSharedKey();
                    ClearConflict();
                    CleanupLocalTransferFiles(current);
                }
            }
            await RefreshAsync();
            Logger.Information("Shared configuration disconnected locally. LocalProfileId={LocalProfileId}", current.LocalId);
        }
        finally { gate.Release(); }
    }

    /// <summary>Authenticates a matching connection file and restores only enrollment access, keeping the selected profile and draft.</summary>
    public async Task RestoreSharedAccessAsync(DeploymentProfileDocument profile, bool rememberKey)
    {
        EnsureCanActivate();
        SharedInvitation invitation = ReadSharedInvitation(profile);
        byte[] key = Convert.FromBase64String(invitation.Key);
        try
        {
            if (key.Length != 32) throw new InvalidDataException("Invalid connection key.");
            await gate.WaitAsync(lifetime.Token);
            try
            {
                LocalProfileDescriptor current = RequireActive();
                LocalProfileEnrollment enrollment = current.Enrollment ?? throw new InvalidOperationException("The configuration is not shared.");
                if (invitation.RepositoryId != enrollment.RepositoryId || invitation.ProfileId != enrollment.ProfileId || invitation.KeyEpoch != enrollment.KeyEpoch)
                    throw new InvalidDataException("The connection file does not match this shared configuration.");
                using SharedProfileRepository remote = OpenRemote(enrollment, key);
                SharedProfileRepositoryResult loaded = await remote.LoadAsync(enrollment.KnownRevisionId, lifetime.Token);
                RequireSuccess(loaded);
                LocalProfileDescriptor restored = await Task.Run(() =>
                {
                    using LocalProfileSnapshot snapshot = local.Read(current.LocalId);
                    return local.Save(current.LocalId, snapshot.Profile, current.RememberSecrets, current.Revision,
                        enrollment with { RememberSharedKey = rememberKey }, rememberKey ? key : null);
                });
                Active = restored;
                ClearSharedKey();
                sessionSharedKey = key.ToArray();
                await RefreshAsync();
                if (loaded.Snapshot?.Head.IsTombstone == true)
                    SetRemoteStatus(SharedProfileRepositoryStatus.Conflict, loaded.Snapshot);
                Logger.Information("Shared configuration access restored. LocalProfileId={LocalProfileId}, RememberKey={RememberKey}", current.LocalId, rememberKey);
            }
            finally { gate.Release(); }
        }
        finally { CryptographicOperations.ZeroMemory(key); }
    }

    private async Task<byte[]> CreateConnectionPackageAsync(LocalProfileEnrollment enrollment, string displayName, byte[] key, string passphrase, bool requireComplete = false)
    {
        DeploymentProfileDocument profile = await session.CaptureAsync(enrollment.ProfileId, displayName, enrollment.IncludeSecrets);
        try
        {
            if (requireComplete && enrollment.IncludeSecrets) EnsureCompleteCheckpoint(profile);
            var invitation = new SharedInvitation(1, enrollment.RepositoryId, enrollment.ProfileId, enrollment.KeyEpoch,
                enrollment.IncludeSecrets, Convert.ToBase64String(key), enrollment.RootPath);
            var access = new DeploymentProfileSecret
            {
                Purpose = ProfileSecretPurpose.SharedProfileKey,
                Identity = "shared-enrollment",
                State = ProfileValueState.Present,
                Value = JsonSerializer.SerializeToUtf8Bytes(invitation)
            };
            profile = profile with { Secrets = new DeploymentProfileSecrets { Entries = profile.Secrets.Entries.Append(access).ToArray() } };
            return await Task.Run(() => packages.Export(profile, passphrase.AsSpan()));
        }
        finally { DeploymentProfileSecretBinding.Clear(profile); }
    }

    private static SharedInvitation ReadSharedInvitation(DeploymentProfileDocument profile)
    {
        DeploymentProfileSecret[] records = profile.Secrets.Entries.Where(secret => secret.Purpose == ProfileSecretPurpose.SharedProfileKey).ToArray();
        if (records.Length != 1 || records[0].Value is null) throw new InvalidDataException("Select a valid connection file.");
        SharedInvitation invitation = JsonSerializer.Deserialize<SharedInvitation>(records[0].Value!) ?? throw new InvalidDataException("Invalid connection file.");
        if (invitation.FormatVersion != 1 || invitation.RepositoryId == Guid.Empty || invitation.ProfileId != profile.ProfileId || invitation.KeyEpoch < 1)
            throw new InvalidDataException("Invalid connection identity.");
        return invitation;
    }

    private async Task PublishConnectionFileAsync()
    {
        LocalProfileDescriptor current = RequireActive();
        LocalProfileEnrollment enrollment = current.Enrollment!;
        string staged = ConnectionOutboxPath(current.LocalId, enrollment.RepositoryId);
        ValidateNoReparseAncestors(staged);
        byte[] encrypted = await ReadBoundedAsync(staged, 16 * 1024 * 1024 + 118);
        string destination = Path.Combine(enrollment.RootPath, "Connection.foundryprofile");
        Logger.Information("Publishing encrypted connection file. LocalProfileId={LocalProfileId}", current.LocalId);
        await PublishConnectionPackageAsync(destination, encrypted);
        await SaveCurrentAsync(enrollment: enrollment with
        {
            PendingConnectionFile = false,
            IsDirty = enrollment.IsDirty || editVersion != persistedEditVersion
        });
        TryDeleteTransferFile(staged);
        MarkSynchronized();
        Logger.Information("Encrypted connection file published. LocalProfileId={LocalProfileId}", current.LocalId);
    }

    private static Task PublishConnectionPackageAsync(string destination, byte[] encrypted) => Task.Run(() =>
    {
        ValidateNoReparseAncestors(destination);
        if (File.Exists(destination))
        {
            using FileStream existing = new(destination, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (existing.Length != encrypted.Length) throw new IOException("A different connection file already exists.");
            byte[] content = new byte[encrypted.Length];
            existing.ReadExactly(content);
            if (!content.AsSpan().SequenceEqual(encrypted)) throw new IOException("A different connection file already exists.");
            return;
        }
        string temporary = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (FileStream output = new(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                output.Write(encrypted);
                output.Flush(true);
            }
            File.Move(temporary, destination, overwrite: false);
        }
        finally
        {
            try { File.Delete(temporary); }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                Logger.Warning(exception, "Temporary encrypted connection file cleanup remains pending.");
            }
        }
    });

    private static string ConnectionOutboxPath(Guid localId, Guid repositoryId) =>
        Path.Combine(Constants.DeploymentProfilesDirectoryPath, "ConnectionOutbox", localId.ToString("N"), repositoryId.ToString("N") + ".foundryprofile");

    private void TryCleanupUncommittedConnectionFile(Guid localId, Guid repositoryId)
    {
        try
        {
            using LocalProfileSnapshot snapshot = local.Read(localId);
            if (snapshot.Descriptor.Enrollment?.RepositoryId != repositoryId)
                TryDeleteTransferFile(ConnectionOutboxPath(localId, repositoryId));
        }
        catch (Exception exception) when (IsProfileFailure(exception))
        {
            Logger.Warning(exception, "Encrypted connection staging retained until local enrollment can be verified.");
        }
    }

    private static void CleanupLocalTransferFiles(LocalProfileDescriptor descriptor)
    {
        if (descriptor.Enrollment is not { } enrollment) return;
        TryDeleteTransferFile(ConnectionOutboxPath(descriptor.LocalId, enrollment.RepositoryId));
        if (enrollment.PendingOperationId is { } operationId)
            TryDeleteTransferFile(Path.Combine(Constants.DeploymentProfilesDirectoryPath, "Outbox", descriptor.LocalId.ToString("N"), operationId.ToString("N") + ".profile"));
    }

    private static void TryDeleteTransferFile(string path)
    {
        try
        {
            ValidateNoReparseAncestors(path);
            File.Delete(path);
        }
        catch (FileNotFoundException) { }
        catch (DirectoryNotFoundException) { }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Logger.Warning(exception, "Local encrypted profile transfer cleanup remains pending.");
        }
    }
}
