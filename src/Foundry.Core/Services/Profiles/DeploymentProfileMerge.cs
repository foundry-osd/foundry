// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Models.Configuration;
using Foundry.Core.Models.Profiles;

namespace Foundry.Core.Services.Profiles;

/// <summary>Preserves explicitly omitted local values only when their authenticated logical context still matches.</summary>
public static class DeploymentProfileMerge
{
    /// <summary>Retains authenticated source fingerprints while an unchanged selected file is missing on this PC. Restored metadata never supplies confidential bytes.</summary>
    public static DeploymentProfileDocument PreserveOmittedSourceMetadata(DeploymentProfileDocument captured,
        DeploymentProfileDocument baseline, FoundryConfigurationDocument materializedBaseline, bool includeSecrets)
    {
        if (captured.ProfileId != baseline.ProfileId) return captured;
        List<DeploymentProfileAsset> assets = captured.Assets.ToList();
        var restoredKinds = new HashSet<ProfileAssetKind>();
        foreach (DeploymentProfileAsset previous in baseline.Assets)
        {
            if (previous.Sha256 is null || previous.State == ProfileValueState.Deleted ||
                !HasMatchingSource(previous, captured.Configuration, materializedBaseline)) continue;
            int index = assets.FindIndex(asset => asset.Id == previous.Id && asset.Kind == previous.Kind);
            if (index >= 0 && (assets[index].Sha256 is not null ||
                assets[index].State is not (ProfileValueState.Omitted or ProfileValueState.Unavailable))) continue;
            DeploymentProfileAsset restored = previous with
            {
                State = includeSecrets ? ProfileValueState.Unavailable : ProfileValueState.Omitted,
                Content = null
            };
            if (index >= 0) assets[index] = restored;
            else assets.Add(restored);
            restoredKinds.Add(previous.Kind);
        }
        DeploymentProfileDocument result = captured with { Assets = assets };
        List<DeploymentProfileSecret> secrets = captured.Secrets.Entries.ToList();
        foreach ((ProfileAssetKind kind, ProfileSecretPurpose purpose) in CertificateSecrets)
        {
            if (!restoredKinds.Contains(kind)) continue;
            string identity = DeploymentProfileSecretBinding.Identity(purpose, result);
            int index = secrets.FindIndex(secret => secret.Purpose == purpose);
            if (index >= 0)
            {
                secrets[index] = secrets[index] with { Identity = identity };
            }
            else if (baseline.Secrets.Entries.Any(secret => secret.Purpose == purpose && secret.Identity == identity && secret.State != ProfileValueState.Deleted))
            {
                secrets.Add(new()
                {
                    Purpose = purpose,
                    Identity = identity,
                    State = includeSecrets ? ProfileValueState.Unavailable : ProfileValueState.Omitted
                });
            }
        }
        return result with { Secrets = new() { Entries = secrets } };
    }

    private static readonly (ProfileAssetKind Kind, ProfileSecretPurpose Purpose)[] CertificateSecrets =
    [
        (ProfileAssetKind.WiredCertificate, ProfileSecretPurpose.WiredCertificatePassword),
        (ProfileAssetKind.WifiCertificate, ProfileSecretPurpose.WifiCertificatePassword),
        (ProfileAssetKind.AutopilotCertificate, ProfileSecretPurpose.AutopilotCertificatePassword)
    ];

    private static bool HasMatchingSource(DeploymentProfileAsset asset, FoundryConfigurationDocument current,
        FoundryConfigurationDocument baseline) => asset.Kind switch
        {
            ProfileAssetKind.WiredProfile or ProfileAssetKind.WiredCertificate =>
                current.Network.Dot1x.IsEnabled == baseline.Network.Dot1x.IsEnabled &&
                current.Network.Dot1x.RequiresCertificate == baseline.Network.Dot1x.RequiresCertificate &&
                current.Network.Dot1x.ProfileTemplatePath == baseline.Network.Dot1x.ProfileTemplatePath &&
                (asset.Kind != ProfileAssetKind.WiredCertificate || current.Network.Dot1x.CertificatePath == baseline.Network.Dot1x.CertificatePath),
            ProfileAssetKind.WifiProfile or ProfileAssetKind.WifiCertificate =>
                current.Network.WifiProvisioned == baseline.Network.WifiProvisioned &&
                current.Network.Wifi.IsEnabled == baseline.Network.Wifi.IsEnabled &&
                current.Network.Wifi.HasEnterpriseProfile == baseline.Network.Wifi.HasEnterpriseProfile &&
                current.Network.Wifi.RequiresCertificate == baseline.Network.Wifi.RequiresCertificate &&
                current.Network.Wifi.Ssid == baseline.Network.Wifi.Ssid &&
                current.Network.Wifi.SecurityType == baseline.Network.Wifi.SecurityType &&
                current.Network.Wifi.EnterpriseProfileTemplatePath == baseline.Network.Wifi.EnterpriseProfileTemplatePath &&
                (asset.Kind != ProfileAssetKind.WifiCertificate || current.Network.Wifi.CertificatePath == baseline.Network.Wifi.CertificatePath),
            ProfileAssetKind.AutopilotCertificate =>
                current.Autopilot.IsEnabled == baseline.Autopilot.IsEnabled &&
                current.Autopilot.ProvisioningMode == baseline.Autopilot.ProvisioningMode &&
                current.Autopilot.HardwareHashUpload.Tenant.TenantId == baseline.Autopilot.HardwareHashUpload.Tenant.TenantId &&
                current.Autopilot.HardwareHashUpload.Tenant.ClientId == baseline.Autopilot.HardwareHashUpload.Tenant.ClientId &&
                current.Autopilot.HardwareHashUpload.BootMediaCertificate.PfxPath == baseline.Autopilot.HardwareHashUpload.BootMediaCertificate.PfxPath,
            ProfileAssetKind.Unattend => current.Unattend.IsEnabled == baseline.Unattend.IsEnabled &&
                current.Unattend.Files.FirstOrDefault(file => file.Id == asset.Id) is { } currentFile &&
                baseline.Unattend.Files.FirstOrDefault(file => file.Id == asset.Id) is { } previousFile &&
                currentFile.SourcePath == previousFile.SourcePath && currentFile.ContentHash == previousFile.ContentHash,
            _ => false
        };

    /// <summary>Returns independent owned buffers. Deletion, unavailable values, and changed contexts never fall back.</summary>
    public static DeploymentProfileDocument PreserveOmittedLocalValues(DeploymentProfileDocument incoming, DeploymentProfileDocument local)
    {
        if (incoming.ProfileId != local.ProfileId)
            throw new ArgumentException("Local values belong to a different profile.", nameof(local));
        return incoming with
        {
            Secrets = new DeploymentProfileSecrets
            {
                Entries = incoming.Secrets.Entries.Select(secret =>
                {
                    DeploymentProfileSecret selected = secret;
                    if (secret.State == ProfileValueState.Omitted)
                    {
                        selected = local.Secrets.Entries.SingleOrDefault(candidate => candidate.Purpose == secret.Purpose &&
                            candidate.Identity == secret.Identity && candidate.State is ProfileValueState.Present or ProfileValueState.Blank) ?? secret;
                    }
                    return selected with { Value = selected.Value?.ToArray() };
                }).ToArray()
            },
            Assets = incoming.Assets.Select(asset =>
            {
                DeploymentProfileAsset selected = asset;
                if (asset.State == ProfileValueState.Omitted && asset.Sha256 is not null)
                {
                    selected = local.Assets.SingleOrDefault(candidate => candidate.Id == asset.Id && candidate.Kind == asset.Kind &&
                        candidate.State == ProfileValueState.Present && string.Equals(candidate.Sha256, asset.Sha256, StringComparison.OrdinalIgnoreCase)) ?? asset;
                }
                return selected with { Content = selected.Content?.ToArray() };
            }).ToArray()
        };
    }
}
