// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.ComponentModel;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Foundry.Core.Models.Profiles;
using Foundry.Utilities.Security;

namespace Foundry.Core.Services.Profiles;

/// <summary>Publishes encrypted local profile revisions beneath a caller-selected per-user directory.</summary>
public sealed class LocalDeploymentProfileRepository
{
    private readonly string root;
    private readonly string credentialScope;
    private readonly IWindowsCredentialStore credentials;
    private readonly IDeploymentProfilePackageService packages;

    public LocalDeploymentProfileRepository(string rootDirectory, IWindowsCredentialStore credentials, IDeploymentProfilePackageService packages)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);
        this.credentials = credentials ?? throw new ArgumentNullException(nameof(credentials));
        this.packages = packages ?? throw new ArgumentNullException(nameof(packages));
        root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(rootDirectory));
        credentialScope = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(root.ToUpperInvariant())))[..24];
    }

    /// <summary>Lists committed metadata. Unsupported or malformed metadata fails closed instead of resetting a profile.</summary>
    public IReadOnlyList<LocalProfileDescriptor> List()
    {
        using FileStream lease = AcquireLock();
        var descriptors = new List<LocalProfileDescriptor>();
        string directory = ManagedPath("profiles");
        if (!Directory.Exists(directory))
        {
            return descriptors;
        }
        foreach (string path in Directory.GetDirectories(directory))
        {
            if (!Guid.TryParseExact(Path.GetFileName(path), "N", out Guid id) || id == Guid.Empty)
            {
                continue;
            }
            bool recovered = Recover(id);
            LocalProfileDescriptor? descriptor = ReadHead(id);
            if (descriptor is not null)
            {
                descriptors.Add(descriptor with { CleanupPending = !recovered });
            }
        }
        return descriptors.OrderBy(profile => profile.DisplayName, StringComparer.OrdinalIgnoreCase).ThenBy(profile => profile.LocalId).ToArray();
    }

    /// <summary>Reads one fixed committed revision and transfers ownership of decrypted buffers to the caller.</summary>
    public LocalProfileSnapshot Read(Guid localId)
    {
        ValidateId(localId);
        using FileStream lease = AcquireLock();
        bool recovered = Recover(localId);
        return ReadSnapshot(RequireHead(localId) with { CleanupPending = !recovered });
    }

    /// <summary>Returns an owned remembered enrollment key. No remembered policy returns null; a missing requested key is locked.</summary>
    public WindowsCredential? ReadSharedKey(Guid localId)
    {
        ValidateId(localId);
        using FileStream lease = AcquireLock();
        _ = Recover(localId);
        LocalProfileDescriptor descriptor = RequireHead(localId);
        using LocalProfileSnapshot snapshot = ReadSnapshot(descriptor);
        return descriptor.Enrollment?.RememberSharedKey == true
            ? ReadKey(localId, descriptor.SharedKeyRevision!.Value, shared: true)
            : null;
    }

    /// <summary>Publishes a new revision only when the current revision matches. Null expected revision means create only.</summary>
    public LocalProfileDescriptor Save(Guid localId, DeploymentProfileDocument profile, bool rememberSecrets, Guid? expectedRevision,
        LocalProfileEnrollment? enrollment = null, byte[]? sharedKey = null)
    {
        ValidateId(localId);
        ArgumentNullException.ThrowIfNull(profile);
        ValidateEnrollment(enrollment, profile.ProfileId);
        using FileStream lease = AcquireLock();
        EnsureRecovered(localId);
        LocalProfileDescriptor? previous = ReadHead(localId);
        CheckRevision(previous, expectedRevision);
        if (previous is not null)
        {
            using LocalProfileSnapshot snapshot = ReadSnapshot(previous);
            if (previous.ProfileId != profile.ProfileId)
            {
                throw new ArgumentException("Create a new local identity for an independent profile copy.", nameof(profile));
            }
        }
        return SaveRevision(localId, profile, rememberSecrets, previous, enrollment, sharedKey);
    }

    /// <summary>Commits settings without secret/asset bytes, disables automatic shared access, then retires earlier keys.</summary>
    public LocalProfileDescriptor Forget(Guid localId, Guid expectedRevision)
    {
        ValidateId(localId);
        using FileStream lease = AcquireLock();
        EnsureRecovered(localId);
        LocalProfileDescriptor previous = RequireHead(localId);
        CheckRevision(previous, expectedRevision);
        using LocalProfileSnapshot snapshot = ReadSnapshot(previous);
        LocalProfileEnrollment? enrollment = previous.Enrollment is null ? null : previous.Enrollment with
        {
            RememberSharedKey = false,
            IsEnabled = false
        };
        return SaveRevision(localId, snapshot.Profile, false, previous, enrollment, null);
    }

    /// <summary>Deletes a committed local profile. Returns true if committed deletion still has journaled cleanup pending.</summary>
    public bool Delete(Guid localId, Guid expectedRevision)
    {
        ValidateId(localId);
        using FileStream lease = AcquireLock();
        EnsureRecovered(localId);
        LocalProfileDescriptor previous = RequireHead(localId);
        CheckRevision(previous, expectedRevision);
        var journal = new LocalProfileJournal
        {
            LocalId = localId,
            IsDelete = true,
            PreviousRevision = previous.Revision,
            PreviousSharedKeyRevision = previous.SharedKeyRevision
        };
        PublishJournal(journal);
        if (ReadActive() == localId)
        {
            PublishActive(null);
        }
        File.Delete(HeadPath(localId));
        return !Recover(localId);
    }

    /// <summary>Changes only the active local selection after verifying the selected revision is readable.</summary>
    public void SetActive(Guid? localId)
    {
        if (localId is { } id)
        {
            ValidateId(id);
        }
        using FileStream lease = AcquireLock();
        _ = ReadActive();
        if (localId is { } selected)
        {
            _ = Recover(selected);
            using LocalProfileSnapshot snapshot = ReadSnapshot(RequireHead(selected));
        }
        PublishActive(localId);
    }

    public Guid? GetActive()
    {
        using FileStream lease = AcquireLock();
        Guid? id = ReadActive();
        return id is { } localId && ReadHead(localId) is not null ? id : null;
    }

    private LocalProfileDescriptor SaveRevision(Guid localId, DeploymentProfileDocument profile, bool rememberSecrets,
        LocalProfileDescriptor? previous, LocalProfileEnrollment? enrollment, byte[]? sharedKey)
    {
        Guid? sharedRevision = ResolveSharedKeyRevision(localId, previous, enrollment, sharedKey);
        var descriptor = new LocalProfileDescriptor
        {
            LocalId = localId,
            ProfileId = profile.ProfileId,
            Revision = Guid.NewGuid(),
            DisplayName = profile.DisplayName,
            RememberSecrets = rememberSecrets,
            Enrollment = enrollment,
            SharedKeyRevision = sharedRevision
        };
        ValidateDescriptor(descriptor, localId);
        byte[] key = RandomNumberGenerator.GetBytes(32);
        try
        {
            DeploymentProfileDocument stored = rememberSecrets ? profile : OmitSecretBytes(profile);
            byte[] ciphertext = packages.Encrypt(stored, key, ProfilePackagePurpose.LocalStorage, DescriptorContext(descriptor));
            EnsureProfileDirectory(localId);
            PublishJournal(new LocalProfileJournal
            {
                LocalId = localId,
                PreviousRevision = previous?.Revision,
                NextRevision = descriptor.Revision,
                PreviousSharedKeyRevision = previous?.SharedKeyRevision,
                NextSharedKeyRevision = sharedRevision
            });
            return PublishRevision(descriptor, key, sharedKey, ciphertext, previous);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }
    }

    private LocalProfileDescriptor PublishRevision(LocalProfileDescriptor descriptor, byte[] key, byte[]? sharedKey,
        byte[] ciphertext, LocalProfileDescriptor? previous)
    {
        try
        {
            WriteVerifiedKey(descriptor.LocalId, descriptor.Revision, key, shared: false);
            if (descriptor.SharedKeyRevision is { } sharedRevision && sharedRevision != previous?.SharedKeyRevision)
            {
                WriteVerifiedKey(descriptor.LocalId, sharedRevision, sharedKey!, shared: true);
            }
            LocalProfileStorage.WriteNew(RevisionPath(descriptor.LocalId, descriptor.Revision), ciphertext);
            using (LocalProfileSnapshot verified = ReadSnapshot(descriptor))
            {
            }
            LocalProfileStorage.PublishJson(HeadPath(descriptor.LocalId), descriptor);
        }
        catch
        {
            _ = TryRecover(descriptor.LocalId);
            throw;
        }
        return descriptor with { CleanupPending = !Recover(descriptor.LocalId) };
    }

    private Guid? ResolveSharedKeyRevision(Guid localId, LocalProfileDescriptor? previous, LocalProfileEnrollment? enrollment, byte[]? sharedKey)
    {
        if (enrollment?.RememberSharedKey != true)
        {
            if (sharedKey is not null)
            {
                throw new ArgumentException("Remembering the shared key requires explicit enrollment policy.", nameof(sharedKey));
            }
            return null;
        }
        if (sharedKey is not null)
        {
            if (sharedKey.Length != 32)
            {
                throw new ArgumentException("The shared profile key must contain 32 bytes.", nameof(sharedKey));
            }
            return Guid.NewGuid();
        }
        if (previous?.SharedKeyRevision is not { } revision || previous.Enrollment is not { } old
            || old.RepositoryId != enrollment.RepositoryId || old.ProfileId != enrollment.ProfileId || old.KeyEpoch != enrollment.KeyEpoch)
        {
            throw new ArgumentException("A new shared enrollment requires its matching key.", nameof(sharedKey));
        }
        using WindowsCredential key = ReadKey(localId, revision, shared: true);
        return revision;
    }

    private LocalProfileSnapshot ReadSnapshot(LocalProfileDescriptor descriptor)
    {
        using WindowsCredential key = ReadKey(descriptor.LocalId, descriptor.Revision, shared: false);
        byte[] ciphertext = LocalProfileStorage.ReadBytes(RevisionPath(descriptor.LocalId, descriptor.Revision), 17 * 1024 * 1024);
        DeploymentProfileDocument profile = packages.Decrypt(ciphertext, key.Secret, ProfilePackagePurpose.LocalStorage, DescriptorContext(descriptor));
        var snapshot = new LocalProfileSnapshot(descriptor, profile);
        if (profile.ProfileId != descriptor.ProfileId || !string.Equals(profile.DisplayName, descriptor.DisplayName, StringComparison.Ordinal))
        {
            snapshot.Dispose();
            throw new InvalidDataException("The local profile identity does not match its committed metadata.");
        }
        return snapshot;
    }

    private WindowsCredential ReadKey(Guid localId, Guid revision, bool shared)
    {
        WindowsCredential key = credentials.Read(CredentialTarget(localId, revision, shared)) ?? throw new LocalProfileLockedException(localId);
        if (key.Secret.Length != 32)
        {
            key.Dispose();
            throw new InvalidDataException("The stored local profile key is invalid.");
        }
        return key;
    }

    private void WriteVerifiedKey(Guid localId, Guid revision, byte[] key, bool shared)
    {
        credentials.Write(CredentialTarget(localId, revision, shared), key);
        using WindowsCredential readback = ReadKey(localId, revision, shared);
        if (!CryptographicOperations.FixedTimeEquals(key, readback.Secret))
        {
            throw new IOException("The stored local profile key could not be verified.");
        }
    }

    private bool Recover(Guid localId)
    {
        string path = JournalPath(localId);
        if (!File.Exists(path))
        {
            return true;
        }
        LocalProfileJournal journal = LocalProfileStorage.ReadJson<LocalProfileJournal>(path);
        ValidateJournal(journal, localId);
        LocalProfileDescriptor? head = ReadHead(localId);
        if (journal.IsDelete)
        {
            if (head is not null && head.Revision != journal.PreviousRevision)
            {
                throw new InvalidDataException("The local deletion journal does not match the committed revision.");
            }
        }
        else if (head?.Revision != journal.PreviousRevision && head?.Revision != journal.NextRevision)
        {
            throw new InvalidDataException("The local save journal does not match the committed revision.");
        }
        bool committed = journal.IsDelete ? head is null : head?.Revision == journal.NextRevision;
        Guid? keepShared = committed ? journal.NextSharedKeyRevision : journal.PreviousSharedKeyRevision;
        if (head?.SharedKeyRevision != keepShared)
        {
            throw new InvalidDataException("The local operation journal does not match the committed enrollment key.");
        }
        try
        {
            RetireRevision(localId, committed ? journal.PreviousRevision : journal.NextRevision);
            Guid? retireShared = committed ? journal.PreviousSharedKeyRevision : journal.NextSharedKeyRevision;
            if (retireShared is { } shared && retireShared != keepShared)
            {
                credentials.Delete(CredentialTarget(localId, shared, shared: true));
            }
            File.Delete(path);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or Win32Exception)
        {
            return false;
        }
    }

    private bool TryRecover(Guid localId)
    {
        try
        {
            return Recover(localId);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or Win32Exception)
        {
            return false;
        }
    }

    private void EnsureRecovered(Guid localId)
    {
        if (!Recover(localId))
        {
            throw new IOException("A previous local profile operation still requires cleanup. Retry when its storage is available.");
        }
    }

    private void RetireRevision(Guid localId, Guid? revision)
    {
        if (revision is not { } id)
        {
            return;
        }
        credentials.Delete(CredentialTarget(localId, id, shared: false));
        File.Delete(RevisionPath(localId, id));
    }

    private LocalProfileDescriptor? ReadHead(Guid localId)
    {
        string path = HeadPath(localId);
        if (!File.Exists(path))
        {
            return null;
        }
        LocalProfileDescriptor descriptor = LocalProfileStorage.ReadJson<LocalProfileDescriptor>(path);
        ValidateDescriptor(descriptor, localId);
        return descriptor;
    }

    private LocalProfileDescriptor RequireHead(Guid localId) => ReadHead(localId) ?? throw new FileNotFoundException("The local profile does not exist.");

    private Guid? ReadActive()
    {
        string path = ManagedPath("active.json");
        if (!File.Exists(path))
        {
            return null;
        }
        LocalProfileActivePointer pointer = LocalProfileStorage.ReadJson<LocalProfileActivePointer>(path);
        if (pointer.LocalId == Guid.Empty)
        {
            throw new InvalidDataException("The active local profile identity is invalid.");
        }
        return pointer.LocalId;
    }

    private void PublishActive(Guid? localId) => LocalProfileStorage.PublishJson(ManagedPath("active.json"), new LocalProfileActivePointer { LocalId = localId });
    private void PublishJournal(LocalProfileJournal journal) => LocalProfileStorage.PublishJson(JournalPath(journal.LocalId), journal);
    private static byte[] DescriptorContext(LocalProfileDescriptor descriptor) => JsonSerializer.SerializeToUtf8Bytes(descriptor, LocalProfileStorage.JsonOptions);

    private FileStream AcquireLock()
    {
        _ = ManagedPath();
        Directory.CreateDirectory(root);
        return new FileStream(ManagedPath(".repository.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
    }

    private void EnsureProfileDirectory(Guid localId)
    {
        Directory.CreateDirectory(ManagedPath("profiles", localId.ToString("N"), "revisions"));
    }

    private string HeadPath(Guid localId) => ManagedPath("profiles", localId.ToString("N"), "head.json");
    private string JournalPath(Guid localId) => ManagedPath("profiles", localId.ToString("N"), "journal.json");
    private string RevisionPath(Guid localId, Guid revision) => ManagedPath("profiles", localId.ToString("N"), "revisions", revision.ToString("N") + ".profile");
    private string CredentialTarget(Guid localId, Guid revision, bool shared) => $"FoundryOSD/Profiles/{credentialScope}/{localId:N}/{(shared ? "Shared" : "Revision")}/{revision:N}";

    private string ManagedPath(params string[] segments)
    {
        string path = Path.GetFullPath(Path.Combine([root, .. segments]));
        string prefix = root + Path.DirectorySeparatorChar;
        if (!string.Equals(path, root, StringComparison.OrdinalIgnoreCase) && !path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("The local profile path is outside its managed directory.");
        }
        for (string? current = path; current is not null; current = Path.GetDirectoryName(current))
        {
            if ((File.Exists(current) || Directory.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
            {
                throw new IOException("Local profile storage cannot use redirected files or directories.");
            }
            if (string.Equals(current, root, StringComparison.OrdinalIgnoreCase))
            {
                break;
            }
        }
        return path;
    }

    private static void ValidateDescriptor(LocalProfileDescriptor descriptor, Guid localId)
    {
        if (descriptor.Version != 1 || descriptor.LocalId != localId || descriptor.LocalId == Guid.Empty || descriptor.ProfileId == Guid.Empty
            || descriptor.Revision == Guid.Empty || string.IsNullOrWhiteSpace(descriptor.DisplayName) || descriptor.DisplayName.Length > 128
            || descriptor.DisplayName.Any(char.IsControl) || descriptor.SharedKeyRevision == Guid.Empty
            || (descriptor.Enrollment?.RememberSharedKey == true) != descriptor.SharedKeyRevision.HasValue)
        {
            throw new InvalidDataException("The committed local profile metadata is invalid.");
        }
        ValidateEnrollment(descriptor.Enrollment, descriptor.ProfileId);
    }

    private static void ValidateEnrollment(LocalProfileEnrollment? enrollment, Guid profileId)
    {
        if (enrollment is not null && (string.IsNullOrWhiteSpace(enrollment.RootPath) || enrollment.RootPath.Length > 4096
            || enrollment.RootPath.Any(char.IsControl) || enrollment.RepositoryId == Guid.Empty || enrollment.ProfileId != profileId
            || enrollment.KeyEpoch <= 0 || enrollment.KnownRevisionId == Guid.Empty || enrollment.PendingOperationId == Guid.Empty))
        {
            throw new InvalidDataException("The local profile enrollment is invalid.");
        }
    }

    private static void ValidateJournal(LocalProfileJournal journal, Guid localId)
    {
        if (journal.LocalId != localId || journal.LocalId == Guid.Empty || journal.PreviousRevision == Guid.Empty
            || journal.NextRevision == Guid.Empty || journal.PreviousSharedKeyRevision == Guid.Empty || journal.NextSharedKeyRevision == Guid.Empty
            || (journal.PreviousRevision is null && journal.PreviousSharedKeyRevision is not null)
            || (journal.IsDelete ? journal.PreviousRevision is null || journal.NextRevision is not null || journal.NextSharedKeyRevision is not null
                : journal.NextRevision is null || journal.NextRevision == journal.PreviousRevision))
        {
            throw new InvalidDataException("The local profile operation journal is invalid.");
        }
    }

    private static void ValidateId(Guid id)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("A nonempty local profile identity is required.", nameof(id));
        }
    }

    private static void CheckRevision(LocalProfileDescriptor? current, Guid? expected)
    {
        if (current?.Revision != expected)
        {
            throw new LocalProfileConflictException(current?.Revision);
        }
    }

    private static DeploymentProfileDocument OmitSecretBytes(DeploymentProfileDocument profile) => profile with
    {
        Secrets = new()
        {
            Entries = profile.Secrets.Entries.Select(secret => secret.State == ProfileValueState.Present
                ? secret with { State = ProfileValueState.Omitted, Value = null } : secret).ToArray()
        },
        Assets = profile.Assets.Select(asset => asset.State == ProfileValueState.Present
            ? asset with { State = ProfileValueState.Omitted, Content = null } : asset).ToArray()
    };
}
