// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Security.Cryptography;
using Foundry.Core.Models.Configuration;
using Foundry.Core.Models.Profiles;
using Foundry.Core.Services.Configuration;

namespace Foundry.Core.Services.Profiles;

/// <summary>Captures exact selected dependencies and rebinds them to a private local staging directory.</summary>
public static class DeploymentProfileAssetService
{
    public const int MaximumAssetBytes = 4 * 1024 * 1024;
    public const int MaximumTotalBytes = 8 * 1024 * 1024;

    /// <summary>Fingerprints bounded selected sources; only the caller's inclusion choice retains their sensitive bytes.</summary>
    public static IReadOnlyList<DeploymentProfileAsset> Capture(FoundryConfigurationDocument configuration, bool includeContent, bool requireContent = false)
    {
        List<DeploymentProfileAsset> assets = [];
        long total = 0;
        bool wiredEnabled = configuration.Network.Dot1x.IsEnabled;
        bool wifiEnabled = configuration.Network.WifiProvisioned && configuration.Network.Wifi.IsEnabled;
        bool wifiProfileEnabled = wifiEnabled && configuration.Network.Wifi.HasEnterpriseProfile;
        bool hardwareHashEnabled = configuration.Autopilot.IsEnabled && configuration.Autopilot.ProvisioningMode == AutopilotProvisioningMode.HardwareHashUpload;
        Add("wired-profile", ProfileAssetKind.WiredProfile, configuration.Network.Dot1x.ProfileTemplatePath, required: wiredEnabled, active: wiredEnabled);
        Add("wifi-profile", ProfileAssetKind.WifiProfile, configuration.Network.Wifi.EnterpriseProfileTemplatePath, required: wifiProfileEnabled, active: wifiProfileEnabled);
        Add("wired-certificate", ProfileAssetKind.WiredCertificate, configuration.Network.Dot1x.CertificatePath, required: wiredEnabled && configuration.Network.Dot1x.RequiresCertificate, active: wiredEnabled);
        Add("wifi-certificate", ProfileAssetKind.WifiCertificate, configuration.Network.Wifi.CertificatePath, required: wifiEnabled && configuration.Network.Wifi.RequiresCertificate, active: wifiEnabled);
        Add("autopilot-certificate", ProfileAssetKind.AutopilotCertificate, configuration.Autopilot.HardwareHashUpload.BootMediaCertificate.PfxPath,
            required: hardwareHashEnabled, active: hardwareHashEnabled);
        foreach (UnattendFileSettings file in configuration.Unattend.Files)
        {
            Add(file.Id, ProfileAssetKind.Unattend, file.SourcePath, file, configuration.Unattend.IsEnabled, configuration.Unattend.IsEnabled);
        }

        return assets;

        void Add(string id, ProfileAssetKind kind, string? path, UnattendFileSettings? answerFile = null, bool required = false, bool active = true)
        {
            if (requireContent && !active) return;
            if (string.IsNullOrWhiteSpace(path))
            {
                if (!required) return;
                if (includeContent && requireContent)
                {
                    Clear(assets);
                    throw new FileNotFoundException("A required profile dependency is missing.");
                }
                assets.Add(new DeploymentProfileAsset
                {
                    Id = id,
                    Kind = kind,
                    RelativePath = $"assets/{(int)kind}-{id}.bin",
                    State = includeContent ? ProfileValueState.Unavailable : ProfileValueState.Omitted,
                    Sha256 = answerFile?.ContentHash
                });
                return;
            }

            byte[]? content = null;
            try
            {
                ProfileValueState state = includeContent ? ProfileValueState.Present : ProfileValueState.Omitted;
                string? digest = answerFile?.ContentHash;
                try
                {
                    using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.Read);
                    if (stream.Length > MaximumAssetBytes || total + stream.Length > MaximumTotalBytes)
                    {
                        throw new InvalidDataException("Profile dependencies exceed the supported size.");
                    }

                    content = new byte[checked((int)stream.Length)];
                    stream.ReadExactly(content);
                    total += content.Length;
                    digest = Convert.ToHexString(SHA256.HashData(content));
                    if (answerFile is not null && !string.Equals(digest, answerFile.ContentHash, StringComparison.OrdinalIgnoreCase))
                    {
                        throw new InvalidDataException("An answer-file source changed. Refresh it before saving the profile.");
                    }
                }
                catch (Exception exception) when (!requireContent && (exception is IOException or UnauthorizedAccessException))
                {
                    if (content is not null) CryptographicOperations.ZeroMemory(content);
                    content = null;
                    state = includeContent ? ProfileValueState.Unavailable : ProfileValueState.Omitted;
                }

                assets.Add(new DeploymentProfileAsset
                {
                    Id = id,
                    Kind = kind,
                    RelativePath = $"assets/{(int)kind}-{Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(id)))}{SafeExtension(path)}",
                    State = state,
                    Sha256 = digest,
                    Content = includeContent ? content : null
                });
                if (includeContent) content = null;
            }
            catch
            {
                Clear(assets);
                throw;
            }
            finally
            {
                if (content is not null)
                {
                    CryptographicOperations.ZeroMemory(content);
                }
            }
        }
    }

    /// <summary>Materializes verified bytes under generated filenames. Imported paths never select a destination.</summary>
    public static FoundryConfigurationDocument Materialize(DeploymentProfileDocument profile, string privateDirectory)
    {
        Directory.CreateDirectory(privateDirectory);
        Dictionary<(ProfileAssetKind Kind, string Id), string> paths = [];
        long total = 0;
        foreach (DeploymentProfileAsset asset in profile.Assets)
        {
            if (asset.State != ProfileValueState.Present)
            {
                continue;
            }

            byte[] content = asset.Content ?? throw new InvalidDataException("A profile dependency is missing.");
            total += content.Length;
            if (content.Length > MaximumAssetBytes || total > MaximumTotalBytes ||
                !string.Equals(Convert.ToHexString(SHA256.HashData(content)), asset.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("A profile dependency failed integrity validation.");
            }

            string path = Path.Combine(privateDirectory, Guid.NewGuid().ToString("N") + SafeExtension(asset.RelativePath));
            paths.Add((asset.Kind, asset.Id), path);
            using FileStream stream = new(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            stream.Write(content);
        }

        FoundryConfigurationDocument document = profile.Configuration;
        return document with
        {
            Network = document.Network with
            {
                Dot1x = document.Network.Dot1x with
                {
                    ProfileTemplatePath = Get(ProfileAssetKind.WiredProfile, "wired-profile"),
                    CertificatePath = Get(ProfileAssetKind.WiredCertificate, "wired-certificate")
                },
                Wifi = document.Network.Wifi with
                {
                    EnterpriseProfileTemplatePath = Get(ProfileAssetKind.WifiProfile, "wifi-profile"),
                    CertificatePath = Get(ProfileAssetKind.WifiCertificate, "wifi-certificate")
                }
            },
            Unattend = document.Unattend with
            {
                Files = document.Unattend.Files.Select(file => file with { SourcePath = Get(ProfileAssetKind.Unattend, file.Id) ?? string.Empty }).ToArray()
            },
            Autopilot = document.Autopilot with
            {
                HardwareHashUpload = document.Autopilot.HardwareHashUpload with
                {
                    BootMediaCertificate = document.Autopilot.HardwareHashUpload.BootMediaCertificate with { PfxPath = Get(ProfileAssetKind.AutopilotCertificate, "autopilot-certificate") }
                }
            }
        };

        string? Get(ProfileAssetKind kind, string id) => paths.GetValueOrDefault((kind, id));
    }

    /// <summary>Clears owned source bytes when an export, activation, or build snapshot ends.</summary>
    public static void Clear(IEnumerable<DeploymentProfileAsset> assets)
    {
        foreach (DeploymentProfileAsset asset in assets)
        {
            if (asset.Content is not null)
            {
                CryptographicOperations.ZeroMemory(asset.Content);
            }
        }
    }

    private static string SafeExtension(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".xml" => ".xml",
        ".pfx" => ".pfx",
        ".p12" => ".p12",
        ".cer" => ".cer",
        ".crt" => ".crt",
        ".json" => ".json",
        _ => ".bin"
    };
}
