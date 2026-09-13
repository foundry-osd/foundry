// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Security.Cryptography;
using Foundry.Core.Models.Profiles;
using Foundry.Core.Services.Profiles;

namespace Foundry.Services.Configuration;

public sealed partial class DeploymentProfileCoordinator
{
    /// <summary>Authenticates the shared folder and previews its current revision without changing local settings.</summary>
    public async Task<SharedProfilePreview> PreviewSharedAsync(DeploymentProfileDocument invitationProfile, string rootPath)
    {
        EnsureCanActivate();
        ValidateSharedPath(rootPath);
        SharedInvitation invitation = ReadSharedInvitation(invitationProfile);
        byte[] key = Convert.FromBase64String(invitation.Key);
        DeploymentProfileDocument? profile = null;
        try
        {
            if (key.Length != 32) throw new InvalidDataException("Invalid connection key.");
            LocalProfileEnrollment enrollment = new()
            {
                RootPath = rootPath,
                RepositoryId = invitation.RepositoryId,
                ProfileId = invitation.ProfileId,
                KeyEpoch = invitation.KeyEpoch
            };
            using SharedProfileRepository remote = OpenRemote(enrollment, key);
            SharedProfileRepositoryResult result = await remote.LoadAsync(cancellationToken: lifetime.Token);
            RequireSuccess(result);
            SharedProfileSnapshot head = result.Snapshot ?? throw new InvalidDataException("The shared profile has no revision.");
            if (head.Head.IsTombstone) throw new InvalidDataException("The shared profile was deleted.");
            profile = packages.Decrypt(head.EncryptedPayload, key, ProfilePackagePurpose.SharedRevision, SharedContext(enrollment));
            if (profile.ProfileId != enrollment.ProfileId) throw new InvalidDataException("The shared profile identity does not match.");
            return new SharedProfilePreview(profile, head.Head.RevisionId);
        }
        catch
        {
            if (profile is not null) DeploymentProfileSecretBinding.Clear(profile);
            throw;
        }
        finally { CryptographicOperations.ZeroMemory(key); }
    }
}

/// <summary>Owns decrypted preview buffers until the operator confirms or cancels joining the reviewed revision.</summary>
public sealed record SharedProfilePreview(DeploymentProfileDocument Profile, Guid RevisionId) : IDisposable
{
    public void Dispose() => DeploymentProfileSecretBinding.Clear(Profile);
}
