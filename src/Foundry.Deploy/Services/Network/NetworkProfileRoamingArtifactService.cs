// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Foundry.Core.Models.Network;
using Foundry.Core.Models.Configuration;
using Foundry.Core.Services.Autopilot;
using Foundry.Deploy.Services.Deployment.PreOobe;
using Foundry.Deploy.Services.Security;
using Microsoft.Extensions.Logging;
using CoreDeployNetworkProfileRoamingSettings = Foundry.Core.Models.Configuration.Deploy.DeployNetworkProfileRoamingSettings;

namespace Foundry.Deploy.Services.Network;

/// <summary>
/// Converts Connect-captured network profile roaming artifacts into pre-OOBE data files.
/// </summary>
/// <remarks>
/// Each secret envelope's key identifier decides its key: deployment-keyed envelopes (a protected PFX and its
/// password) decrypt with the unlocked deployment key, media-keyed ones (schema 5 media) with the media key. Each key is
/// read lazily, at most once per load, only when an envelope needs it (schema 6 media have no media key file), and zeroed
/// when the load ends.
/// </remarks>
public sealed class NetworkProfileRoamingArtifactService : INetworkProfileRoamingArtifactService
{
    private const string ImportSettingsRelativePath = @"NetworkProfiles\import-settings.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    private readonly INetworkSecretKeyReader _networkSecretKeyReader;
    private readonly IDeploymentSecretKeyProvider _deploymentSecretKeyProvider;
    private readonly ILogger<NetworkProfileRoamingArtifactService> _logger;

    public NetworkProfileRoamingArtifactService(
        INetworkSecretKeyReader networkSecretKeyReader,
        IDeploymentSecretKeyProvider deploymentSecretKeyProvider,
        ILogger<NetworkProfileRoamingArtifactService> logger)
    {
        _networkSecretKeyReader = networkSecretKeyReader;
        _deploymentSecretKeyProvider = deploymentSecretKeyProvider;
        _logger = logger;
    }

    public async Task<PreOobeNetworkProfileRoamingPayload?> LoadAsync(
        CoreDeployNetworkProfileRoamingSettings settings,
        string workspaceRootPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);

        if (!settings.IsAnyEnabled)
        {
            return null;
        }

        string artifactRootPath = ResolveArtifactRootPath(settings);
        string manifestPath = Path.Combine(artifactRootPath, NetworkProfileRoamingArtifacts.ManifestFileName);
        if (!File.Exists(manifestPath))
        {
            _logger.LogDebug("Network profile roaming skipped because no manifest exists at '{ManifestPath}'.", manifestPath);
            return null;
        }

        NetworkProfileRoamingManifest? manifest = await ReadManifestAsync(manifestPath, cancellationToken).ConfigureAwait(false);
        if (manifest is null)
        {
            _logger.LogWarning("Network profile roaming skipped because the manifest at '{ManifestPath}' could not be read.", manifestPath);
            return null;
        }

        var dataFiles = new List<PreOobeScriptDataFile>();
        var importSettings = new NetworkProfileRoamingImportSettings();

        IReadOnlyList<NetworkProfileRoamingImportProfile> wifiProfiles = settings.Wifi.IsEnabled
            ? AddWifiProfileDataFiles(dataFiles, artifactRootPath, manifest.GetOrderedWifiProfiles(), cancellationToken)
            : [];
        NetworkProfileRoamingImportProfile? preferredWifiProfile = wifiProfiles.FirstOrDefault();
        importSettings.WifiProfileRelativePath = preferredWifiProfile?.RelativePath;
        importSettings.WifiProfileSource = preferredWifiProfile?.Source;
        importSettings.WifiProfileConnectivityExpectation = preferredWifiProfile?.ConnectivityExpectation;
        importSettings.WifiProfiles = wifiProfiles.Count > 1 ? wifiProfiles : null;
        importSettings.WiredDot1xProfileRelativePath = AddProfileDataFile(
            dataFiles,
            artifactRootPath,
            settings.WiredDot1x.IsEnabled ? manifest.WiredDot1xProfile?.RelativePath : null,
            isSensitive: false,
            cancellationToken);
        importSettings.WiredDot1xProfileSource = settings.WiredDot1x.IsEnabled ? manifest.WiredDot1xProfile?.Source : null;
        importSettings.WiredDot1xProfileConnectivityExpectation = settings.WiredDot1x.IsEnabled ? manifest.WiredDot1xProfile?.ConnectivityExpectation : null;
        importSettings.Certificates = await AddCertificateDataFilesAsync(
                dataFiles,
                artifactRootPath,
                workspaceRootPath,
                manifest.Certificates,
                settings,
                cancellationToken)
            .ConfigureAwait(false);

        if (importSettings.WifiProfileRelativePath is null &&
            importSettings.WiredDot1xProfileRelativePath is null &&
            importSettings.Certificates.Count == 0)
        {
            return null;
        }

        dataFiles.Add(new PreOobeScriptDataFile
        {
            FileName = ImportSettingsRelativePath,
            Content = JsonSerializer.Serialize(importSettings, JsonOptions) + Environment.NewLine
        });

        return new PreOobeNetworkProfileRoamingPayload
        {
            DataFiles = dataFiles
        };
    }

    private static string ResolveArtifactRootPath(CoreDeployNetworkProfileRoamingSettings settings)
    {
        return string.IsNullOrWhiteSpace(settings.ArtifactRootPath)
            ? NetworkProfileRoamingArtifacts.DefaultArtifactRootPath
            : settings.ArtifactRootPath.Trim();
    }

    private static async Task<NetworkProfileRoamingManifest?> ReadManifestAsync(
        string manifestPath,
        CancellationToken cancellationToken)
    {
        try
        {
            await using FileStream stream = File.OpenRead(manifestPath);
            return await JsonSerializer.DeserializeAsync<NetworkProfileRoamingManifest>(
                    stream,
                    JsonOptions,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    // Stages every Wi-Fi profile whose file exists, in manifest order (most preferred first). The single
    // wifiProfile* import fields carry the first staged one; wifiProfiles is written only when there are several, so a
    // schema 1 manifest produces the same import settings as before.
    private static IReadOnlyList<NetworkProfileRoamingImportProfile> AddWifiProfileDataFiles(
        List<PreOobeScriptDataFile> dataFiles,
        string artifactRootPath,
        IReadOnlyList<NetworkProfileRoamingProfile> profiles,
        CancellationToken cancellationToken)
    {
        var stagedProfiles = new List<NetworkProfileRoamingImportProfile>();
        var stagedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (NetworkProfileRoamingProfile profile in profiles)
        {
            if (string.IsNullOrWhiteSpace(profile.RelativePath) ||
                !stagedPaths.Add(ToStagedDataRelativePath(profile.RelativePath)))
            {
                continue;
            }

            string? stagedRelativePath = AddProfileDataFile(dataFiles, artifactRootPath, profile.RelativePath, isSensitive: true, cancellationToken);
            if (stagedRelativePath is not null)
            {
                stagedProfiles.Add(new NetworkProfileRoamingImportProfile
                {
                    RelativePath = stagedRelativePath,
                    Source = profile.Source,
                    ConnectivityExpectation = profile.ConnectivityExpectation
                });
            }
        }

        return stagedProfiles;
    }

    // A Wi-Fi profile may carry plaintext key material (a personal passphrase or an enterprise keyMaterial element), so it is
    // staged sensitive: zeroed after staging and deleted by PostInstall once imported. A wired LAN profile has no key material.
    private static string? AddProfileDataFile(
        List<PreOobeScriptDataFile> dataFiles,
        string artifactRootPath,
        string? artifactRelativePath,
        bool isSensitive,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(artifactRelativePath))
        {
            return null;
        }

        string sourcePath = ResolveArtifactPath(artifactRootPath, artifactRelativePath);
        if (!File.Exists(sourcePath))
        {
            return null;
        }

        string stagedRelativePath = ToStagedDataRelativePath(artifactRelativePath);
        dataFiles.Add(new PreOobeScriptDataFile
        {
            FileName = stagedRelativePath,
            Bytes = File.ReadAllBytes(sourcePath),
            IsSensitive = isSensitive
        });
        cancellationToken.ThrowIfCancellationRequested();
        return stagedRelativePath;
    }

    private async Task<IReadOnlyList<NetworkProfileRoamingImportCertificate>> AddCertificateDataFilesAsync(
        List<PreOobeScriptDataFile> dataFiles,
        string artifactRootPath,
        string workspaceRootPath,
        IReadOnlyList<NetworkProfileRoamingCertificate> certificates,
        CoreDeployNetworkProfileRoamingSettings settings,
        CancellationToken cancellationToken)
    {
        var importCertificates = new List<NetworkProfileRoamingImportCertificate>();
        var secretKeys = new RoamingSecretKeys(_networkSecretKeyReader, _deploymentSecretKeyProvider, workspaceRootPath);

        try
        {
            foreach (NetworkProfileRoamingCertificate certificate in certificates)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (string.IsNullOrWhiteSpace(certificate.RelativePath))
                {
                    continue;
                }

                NetworkProfileRoamingTransportSettings? transportSettings = ResolveTransportSettings(certificate, settings);
                if (transportSettings is null || !transportSettings.IsEnabled)
                {
                    continue;
                }

                bool isPfxPrivateKey = string.Equals(certificate.Kind, NetworkProfileRoamingArtifacts.PfxPrivateKeyKind, StringComparison.OrdinalIgnoreCase);
                if (isPfxPrivateKey && !transportSettings.IncludePrivateKeyMaterial)
                {
                    continue;
                }

                string sourcePath = ResolveArtifactPath(artifactRootPath, certificate.RelativePath);
                if (!File.Exists(sourcePath))
                {
                    continue;
                }

                byte[]? certificateBytes = await ReadCertificateBytesAsync(sourcePath, certificate, secretKeys, cancellationToken)
                    .ConfigureAwait(false);
                if (certificateBytes is null)
                {
                    continue;
                }

                // The staged name keeps the manifest path: PostInstall imports by kind, not by extension.
                string stagedRelativePath = ToStagedDataRelativePath(certificate.RelativePath);
                dataFiles.Add(new PreOobeScriptDataFile
                {
                    FileName = stagedRelativePath,
                    Bytes = certificateBytes,
                    IsSensitive = isPfxPrivateKey || certificate.IsContentProtected
                });

                string? passwordRelativePath = isPfxPrivateKey
                    ? await AddPfxPasswordDataFileAsync(
                            dataFiles,
                            artifactRootPath,
                            certificate,
                            secretKeys,
                            cancellationToken)
                        .ConfigureAwait(false)
                    : null;

                importCertificates.Add(new NetworkProfileRoamingImportCertificate
                {
                    RelativePath = stagedRelativePath,
                    Kind = isPfxPrivateKey ? "pfx" : "certificate",
                    StoreName = string.IsNullOrWhiteSpace(certificate.StoreName)
                        ? (isPfxPrivateKey ? "My" : "Root")
                        : certificate.StoreName.Trim(),
                    PasswordRelativePath = passwordRelativePath
                });
            }

            return importCertificates;
        }
        finally
        {
            secretKeys.Clear();
        }
    }

    // A protected certificate file is a deployment-keyed SecretEnvelope JSON document; it is staged decrypted.
    private static async Task<byte[]?> ReadCertificateBytesAsync(
        string sourcePath,
        NetworkProfileRoamingCertificate certificate,
        RoamingSecretKeys secretKeys,
        CancellationToken cancellationToken)
    {
        if (!certificate.IsContentProtected)
        {
            return File.ReadAllBytes(sourcePath);
        }

        SecretEnvelope? envelope = await ReadEnvelopeAsync(sourcePath, cancellationToken).ConfigureAwait(false);
        if (envelope is null)
        {
            return null;
        }

        byte[] deploymentKey = await secretKeys.GetDeploymentKeyAsync(cancellationToken).ConfigureAwait(false);
        return MediaSecretEnvelopeProtector.DecryptBytes(envelope, deploymentKey, MediaSecretEnvelopeProtector.DeploymentKeyId);
    }

    private static NetworkProfileRoamingTransportSettings? ResolveTransportSettings(
        NetworkProfileRoamingCertificate certificate,
        CoreDeployNetworkProfileRoamingSettings settings)
    {
        if (NetworkProfileRoamingArtifacts.IsCertificateOwnedBySource(
                certificate,
                NetworkProfileRoamingArtifacts.ProvisionedWiredDot1xSource))
        {
            return settings.WiredDot1x;
        }

        if (NetworkProfileRoamingArtifacts.IsCertificateOwnedBySource(
                certificate,
                NetworkProfileRoamingArtifacts.ManualWifiSource) ||
            NetworkProfileRoamingArtifacts.IsCertificateOwnedBySource(
                certificate,
                NetworkProfileRoamingArtifacts.ProvisionedWifiSource))
        {
            return settings.Wifi;
        }

        return settings.IsAnyEnabled
            ? new NetworkProfileRoamingTransportSettings
            {
                IsEnabled = true,
                IncludePrivateKeyMaterial =
                    (settings.WiredDot1x.IsEnabled && settings.WiredDot1x.IncludePrivateKeyMaterial) ||
                    (settings.Wifi.IsEnabled && settings.Wifi.IncludePrivateKeyMaterial)
            }
            : null;
    }

    // The envelope is parsed before any key is read: its key identifier chooses the key, and schema 6 media have no media key.
    private static async Task<string?> AddPfxPasswordDataFileAsync(
        List<PreOobeScriptDataFile> dataFiles,
        string artifactRootPath,
        NetworkProfileRoamingCertificate certificate,
        RoamingSecretKeys secretKeys,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(certificate.PasswordSecretRelativePath))
        {
            return null;
        }

        string passwordSecretPath = ResolveArtifactPath(artifactRootPath, certificate.PasswordSecretRelativePath);
        if (!File.Exists(passwordSecretPath))
        {
            return null;
        }

        SecretEnvelope? envelope = await ReadEnvelopeAsync(passwordSecretPath, cancellationToken).ConfigureAwait(false);
        if (envelope is null)
        {
            return null;
        }

        string password = string.Equals(envelope.KeyId, MediaSecretEnvelopeProtector.DeploymentKeyId, StringComparison.Ordinal)
            ? MediaSecretEnvelopeProtector.DecryptString(
                envelope,
                await secretKeys.GetDeploymentKeyAsync(cancellationToken).ConfigureAwait(false),
                MediaSecretEnvelopeProtector.DeploymentKeyId)
            : MediaSecretEnvelopeProtector.DecryptString(
                envelope,
                await secretKeys.GetMediaKeyAsync(cancellationToken).ConfigureAwait(false));
        string passwordRelativePath = ToStagedDataRelativePath(certificate.RelativePath + ".password");
        dataFiles.Add(new PreOobeScriptDataFile
        {
            FileName = passwordRelativePath,
            Content = password,
            IsSensitive = true
        });

        return passwordRelativePath;
    }

    private static async Task<SecretEnvelope?> ReadEnvelopeAsync(string path, CancellationToken cancellationToken)
    {
        await using FileStream stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<SecretEnvelope>(
                stream,
                JsonOptions,
                cancellationToken)
            .ConfigureAwait(false);
    }

    private static string ResolveArtifactPath(string artifactRootPath, string relativePath)
    {
        string sanitizedRelativePath = NormalizeRelativePath(relativePath);
        string rootPath = Path.GetFullPath(artifactRootPath);
        string fullPath = Path.GetFullPath(Path.Combine(rootPath, sanitizedRelativePath));
        if (!fullPath.StartsWith(rootPath.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Network profile roaming artifact path is outside the artifact root.");
        }

        return fullPath;
    }

    private static string ToStagedDataRelativePath(string artifactRelativePath)
    {
        return Path.Combine("NetworkProfiles", NormalizeRelativePath(artifactRelativePath));
    }

    private static string NormalizeRelativePath(string relativePath)
    {
        string normalized = relativePath.Replace('/', Path.DirectorySeparatorChar).Trim();
        if (Path.IsPathRooted(normalized))
        {
            throw new InvalidOperationException("Network profile roaming artifact paths must be relative.");
        }

        string[] segments = normalized.Split(
            [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
            StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0 ||
            segments.Any(segment => segment is "." or ".."))
        {
            throw new InvalidOperationException("Network profile roaming artifact path is invalid.");
        }

        return Path.Combine(segments);
    }

    /// <summary>
    /// Reads the media key and the deployment key lazily, each at most once per load, and zeroes both in <see cref="Clear"/>.
    /// </summary>
    private sealed class RoamingSecretKeys(
        INetworkSecretKeyReader mediaKeys,
        IDeploymentSecretKeyProvider deploymentKeys,
        string workspaceRootPath)
    {
        private byte[]? _mediaKey;
        private byte[]? _deploymentKey;

        public async Task<byte[]> GetMediaKeyAsync(CancellationToken cancellationToken) =>
            _mediaKey ??= await mediaKeys.ReadAsync(workspaceRootPath, cancellationToken).ConfigureAwait(false);

        public async Task<byte[]> GetDeploymentKeyAsync(CancellationToken cancellationToken) =>
            _deploymentKey ??= await deploymentKeys.ReadAsync(workspaceRootPath, cancellationToken).ConfigureAwait(false);

        public void Clear()
        {
            if (_mediaKey is not null)
            {
                CryptographicOperations.ZeroMemory(_mediaKey);
                _mediaKey = null;
            }

            if (_deploymentKey is not null)
            {
                CryptographicOperations.ZeroMemory(_deploymentKey);
                _deploymentKey = null;
            }
        }
    }

    private sealed record NetworkProfileRoamingImportSettings
    {
        public string? WifiProfileRelativePath { get; set; }
        public string? WifiProfileSource { get; set; }
        public string? WifiProfileConnectivityExpectation { get; set; }
        public IReadOnlyList<NetworkProfileRoamingImportProfile>? WifiProfiles { get; set; }
        public string? WiredDot1xProfileRelativePath { get; set; }
        public string? WiredDot1xProfileSource { get; set; }
        public string? WiredDot1xProfileConnectivityExpectation { get; set; }
        public IReadOnlyList<NetworkProfileRoamingImportCertificate> Certificates { get; set; } = [];
    }

    private sealed record NetworkProfileRoamingImportProfile
    {
        public string RelativePath { get; init; } = string.Empty;
        public string? Source { get; init; }
        public string? ConnectivityExpectation { get; init; }
    }

    private sealed record NetworkProfileRoamingImportCertificate
    {
        public string RelativePath { get; init; } = string.Empty;
        public string Kind { get; init; } = string.Empty;
        public string StoreName { get; init; } = string.Empty;
        public string? PasswordRelativePath { get; init; }
    }
}
