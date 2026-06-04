using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Foundry.Core.Models.Network;
using Foundry.Deploy.Services.Autopilot;
using Foundry.Deploy.Services.Deployment.PreOobe;
using Microsoft.Extensions.Logging;
using CoreDeployNetworkProfileRoamingSettings = Foundry.Core.Models.Configuration.Deploy.DeployNetworkProfileRoamingSettings;
using CoreMediaSecretEnvelopeProtector = Foundry.Core.Services.Autopilot.MediaSecretEnvelopeProtector;
using CoreSecretEnvelope = Foundry.Core.Models.Configuration.SecretEnvelope;

namespace Foundry.Deploy.Services.Network;

/// <summary>
/// Converts Connect-captured network profile roaming artifacts into pre-OOBE data files.
/// </summary>
public sealed class NetworkProfileRoamingArtifactService : INetworkProfileRoamingArtifactService
{
    private const string ImportSettingsRelativePath = @"NetworkProfiles\import-settings.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    private readonly IMediaSecretKeyReader _mediaSecretKeyReader;
    private readonly ILogger<NetworkProfileRoamingArtifactService> _logger;

    public NetworkProfileRoamingArtifactService(
        IMediaSecretKeyReader mediaSecretKeyReader,
        ILogger<NetworkProfileRoamingArtifactService> logger)
    {
        _mediaSecretKeyReader = mediaSecretKeyReader;
        _logger = logger;
    }

    public async Task<PreOobeNetworkProfileRoamingPayload?> LoadAsync(
        CoreDeployNetworkProfileRoamingSettings settings,
        string workspaceRootPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);

        if (!settings.IsEnabled)
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

        importSettings.WifiProfileRelativePath = AddProfileDataFile(
            dataFiles,
            artifactRootPath,
            manifest.WifiProfile?.RelativePath,
            cancellationToken);
        importSettings.WifiProfileSource = manifest.WifiProfile?.Source;
        importSettings.WifiProfileConnectivityExpectation = manifest.WifiProfile?.ConnectivityExpectation;
        importSettings.WiredDot1xProfileRelativePath = AddProfileDataFile(
            dataFiles,
            artifactRootPath,
            manifest.WiredDot1xProfile?.RelativePath,
            cancellationToken);
        importSettings.WiredDot1xProfileSource = manifest.WiredDot1xProfile?.Source;
        importSettings.WiredDot1xProfileConnectivityExpectation = manifest.WiredDot1xProfile?.ConnectivityExpectation;
        importSettings.Certificates = await AddCertificateDataFilesAsync(
                dataFiles,
                artifactRootPath,
                workspaceRootPath,
                manifest.Certificates,
                settings.IncludePrivateKeyMaterial,
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

    private static string? AddProfileDataFile(
        List<PreOobeScriptDataFile> dataFiles,
        string artifactRootPath,
        string? artifactRelativePath,
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
            Bytes = File.ReadAllBytes(sourcePath)
        });
        cancellationToken.ThrowIfCancellationRequested();
        return stagedRelativePath;
    }

    private async Task<IReadOnlyList<NetworkProfileRoamingImportCertificate>> AddCertificateDataFilesAsync(
        List<PreOobeScriptDataFile> dataFiles,
        string artifactRootPath,
        string workspaceRootPath,
        IReadOnlyList<NetworkProfileRoamingCertificate> certificates,
        bool includePrivateKeyMaterial,
        CancellationToken cancellationToken)
    {
        var importCertificates = new List<NetworkProfileRoamingImportCertificate>();
        byte[]? mediaSecretKey = null;

        foreach (NetworkProfileRoamingCertificate certificate in certificates)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (string.IsNullOrWhiteSpace(certificate.RelativePath))
            {
                continue;
            }

            bool isPfxPrivateKey = string.Equals(certificate.Kind, NetworkProfileRoamingArtifacts.PfxPrivateKeyKind, StringComparison.OrdinalIgnoreCase);
            if (isPfxPrivateKey && !includePrivateKeyMaterial)
            {
                continue;
            }

            string sourcePath = ResolveArtifactPath(artifactRootPath, certificate.RelativePath);
            if (!File.Exists(sourcePath))
            {
                continue;
            }

            string stagedRelativePath = ToStagedDataRelativePath(certificate.RelativePath);
            dataFiles.Add(new PreOobeScriptDataFile
            {
                FileName = stagedRelativePath,
                Bytes = File.ReadAllBytes(sourcePath),
                IsSensitive = isPfxPrivateKey
            });

            string? passwordRelativePath = null;
            if (isPfxPrivateKey)
            {
                if (!string.IsNullOrWhiteSpace(certificate.PasswordSecretRelativePath))
                {
                    mediaSecretKey ??= await _mediaSecretKeyReader.ReadAsync(workspaceRootPath, cancellationToken).ConfigureAwait(false);
                }

                passwordRelativePath = await AddPfxPasswordDataFileAsync(
                        dataFiles,
                        artifactRootPath,
                        certificate,
                        mediaSecretKey,
                        cancellationToken)
                    .ConfigureAwait(false);
            }

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

        if (mediaSecretKey is not null)
        {
            CryptographicOperations.ZeroMemory(mediaSecretKey);
        }

        return importCertificates;
    }

    private async Task<string?> AddPfxPasswordDataFileAsync(
        List<PreOobeScriptDataFile> dataFiles,
        string artifactRootPath,
        NetworkProfileRoamingCertificate certificate,
        byte[]? mediaSecretKey,
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

        if (mediaSecretKey is null)
        {
            return null;
        }

        await using FileStream stream = File.OpenRead(passwordSecretPath);
        CoreSecretEnvelope? envelope = await JsonSerializer.DeserializeAsync<CoreSecretEnvelope>(
                stream,
                JsonOptions,
                cancellationToken)
            .ConfigureAwait(false);
        if (envelope is null)
        {
            return null;
        }

        string password = CoreMediaSecretEnvelopeProtector.DecryptString(envelope, mediaSecretKey);
        string passwordRelativePath = ToStagedDataRelativePath(certificate.RelativePath + ".password");
        dataFiles.Add(new PreOobeScriptDataFile
        {
            FileName = passwordRelativePath,
            Content = password,
            IsSensitive = true
        });

        return passwordRelativePath;
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

    private sealed record NetworkProfileRoamingImportSettings
    {
        public string? WifiProfileRelativePath { get; set; }
        public string? WifiProfileSource { get; set; }
        public string? WifiProfileConnectivityExpectation { get; set; }
        public string? WiredDot1xProfileRelativePath { get; set; }
        public string? WiredDot1xProfileSource { get; set; }
        public string? WiredDot1xProfileConnectivityExpectation { get; set; }
        public IReadOnlyList<NetworkProfileRoamingImportCertificate> Certificates { get; set; } = [];
    }

    private sealed record NetworkProfileRoamingImportCertificate
    {
        public string RelativePath { get; init; } = string.Empty;
        public string Kind { get; init; } = string.Empty;
        public string StoreName { get; init; } = string.Empty;
        public string? PasswordRelativePath { get; init; }
    }
}
