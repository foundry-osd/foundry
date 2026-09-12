// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Foundry.Core.Models.Configuration;
using Foundry.Core.Models.Profiles;

namespace Foundry.Core.Services.Profiles;

/// <summary>Binds passwords to public configuration identity, independently of local paths and credential targets.</summary>
public static class DeploymentProfileSecretBinding
{
    /// <summary>Hashes only identity metadata. Password values never participate in identifiers or equality markers.</summary>
    public static string Identity(ProfileSecretPurpose purpose, DeploymentProfileDocument profile, string? accountId = null)
    {
        FoundryConfigurationDocument configuration = profile.Configuration;
        string[] context = purpose switch
        {
            ProfileSecretPurpose.DeploymentPassword => ["media-protection"],
            ProfileSecretPurpose.WifiPassphrase => [configuration.Network.Wifi.Ssid ?? string.Empty, configuration.Network.Wifi.SecurityType ?? string.Empty],
            ProfileSecretPurpose.AdministratorPassword => ["builtin-administrator"],
            ProfileSecretPurpose.AdditionalAccountPassword => AccountContext(configuration.Customization.Oobe, accountId),
            ProfileSecretPurpose.WiredCertificatePassword => [AssetHash(ProfileAssetKind.WiredCertificate)],
            ProfileSecretPurpose.WifiCertificatePassword => [AssetHash(ProfileAssetKind.WifiCertificate)],
            ProfileSecretPurpose.AutopilotCertificatePassword => [configuration.Autopilot.HardwareHashUpload.Tenant.TenantId ?? string.Empty,
                configuration.Autopilot.HardwareHashUpload.Tenant.ClientId ?? string.Empty, AssetHash(ProfileAssetKind.AutopilotCertificate)],
            _ => throw new ArgumentOutOfRangeException(nameof(purpose))
        };
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { purpose, context }))));

        string AssetHash(ProfileAssetKind kind) => profile.Assets.SingleOrDefault(asset => asset.Kind == kind)?.Sha256 ?? "unavailable";
    }

    /// <summary>Releases all owned plaintext buffers after their session, export, or activation ends.</summary>
    public static void Clear(DeploymentProfileDocument profile)
    {
        foreach (DeploymentProfileSecret secret in profile.Secrets.Entries)
        {
            if (secret.Value is not null)
            {
                CryptographicOperations.ZeroMemory(secret.Value);
            }
        }

        DeploymentProfileAssetService.Clear(profile.Assets);
    }

    private static string[] AccountContext(OobeSettings settings, string? accountId)
    {
        OobeAdditionalAccountSettings account = settings.AdditionalAccounts.Single(account => account.Id == accountId);
        return [account.Id, account.UserName ?? string.Empty, account.Type.ToString()];
    }
}
