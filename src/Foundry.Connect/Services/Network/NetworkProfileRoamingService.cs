using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Foundry.Connect.Models.Configuration;
using Foundry.Connect.Models.Network;
using Foundry.Connect.Services.Runtime;
using Microsoft.Extensions.Logging;

namespace Foundry.Connect.Services.Network;

/// <summary>
/// Writes Foundry-managed network profile roaming artifacts in WinPE.
/// </summary>
public sealed class NetworkProfileRoamingService : INetworkProfileRoamingService
{
    private const string WifiProfileFileName = "wifi-profile.xml";
    private const string WiredDot1xProfileFileName = "wired-dot1x-profile.xml";
    private const string ManifestFileName = "manifest.json";
    private const string PublicCertificateKind = "publicCertificate";
    private const string PfxPrivateKeyKind = "pfxPrivateKey";
    private const string CertificatesDirectoryName = "certificates";
    private const string RootStoreName = "Root";
    private const string MyStoreName = "My";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly FoundryConnectConfiguration _configuration;
    private readonly ILogger<NetworkProfileRoamingService> _logger;
    private readonly string _artifactRootPath;

    /// <summary>
    /// Initializes a network profile roaming service.
    /// </summary>
    /// <param name="configuration">The loaded runtime configuration.</param>
    /// <param name="logger">Logger.</param>
    public NetworkProfileRoamingService(
        FoundryConnectConfiguration configuration,
        ILogger<NetworkProfileRoamingService> logger)
        : this(configuration, logger, ConnectWorkspacePaths.GetNetworkProfileRoamingDirectory())
    {
    }

    internal NetworkProfileRoamingService(
        FoundryConnectConfiguration configuration,
        ILogger<NetworkProfileRoamingService> logger,
        string artifactRootPath)
    {
        _configuration = configuration;
        _logger = logger;
        _artifactRootPath = artifactRootPath;
    }

    /// <inheritdoc />
    public Task CaptureWifiProfileAsync(NetworkProfileRoamingCaptureRequest request, CancellationToken cancellationToken)
    {
        return CaptureProfileAsync(request, WifiProfileFileName, isWifiProfile: true, cancellationToken);
    }

    /// <inheritdoc />
    public Task CaptureWiredDot1xProfileAsync(NetworkProfileRoamingCaptureRequest request, CancellationToken cancellationToken)
    {
        return CaptureProfileAsync(request, WiredDot1xProfileFileName, isWifiProfile: false, cancellationToken);
    }

    private async Task CaptureProfileAsync(
        NetworkProfileRoamingCaptureRequest request,
        string destinationFileName,
        bool isWifiProfile,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!_configuration.Network.ProfileRoaming.IsEnabled)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(request.ProfilePath) || !File.Exists(request.ProfilePath))
        {
            _logger.LogDebug("Network profile roaming skipped because the source profile path was missing.");
            return;
        }

        Directory.CreateDirectory(_artifactRootPath);
        string destinationPath = Path.Combine(_artifactRootPath, destinationFileName);
        File.Copy(request.ProfilePath, destinationPath, overwrite: true);

        string source = FormatSource(request.Source);
        NetworkProfileRoamingManifest existingManifest = await LoadManifestAsync(cancellationToken).ConfigureAwait(false);
        DeleteCertificateArtifactsForSource(source);
        List<NetworkProfileRoamingCertificate> certificates = existingManifest.Certificates
            .Where(certificate => !IsCertificateOwnedBySource(certificate, source))
            .ToList();
        certificates.AddRange(await CopyCertificatesAsync(request, cancellationToken).ConfigureAwait(false));

        var profile = new NetworkProfileRoamingProfile
        {
            RelativePath = destinationFileName,
            Source = source,
            ConnectivityExpectation = FormatConnectivityExpectation(request.ConnectivityExpectation)
        };

        NetworkProfileRoamingManifest manifest = existingManifest with
        {
            UpdatedAtUtc = DateTimeOffset.UtcNow,
            WifiProfile = isWifiProfile ? profile : existingManifest.WifiProfile,
            WiredDot1xProfile = isWifiProfile ? existingManifest.WiredDot1xProfile : profile,
            Certificates = certificates
                .GroupBy(static certificate => certificate.RelativePath, StringComparer.OrdinalIgnoreCase)
                .Select(static group => group.Last())
                .ToArray()
        };

        await WriteManifestAsync(manifest, cancellationToken).ConfigureAwait(false);
    }

    private async Task<NetworkProfileRoamingManifest> LoadManifestAsync(CancellationToken cancellationToken)
    {
        string manifestPath = Path.Combine(_artifactRootPath, ManifestFileName);
        if (!File.Exists(manifestPath))
        {
            return new NetworkProfileRoamingManifest();
        }

        try
        {
            await using FileStream stream = File.OpenRead(manifestPath);
            NetworkProfileRoamingManifest? manifest = await JsonSerializer.DeserializeAsync<NetworkProfileRoamingManifest>(
                stream,
                JsonOptions,
                cancellationToken).ConfigureAwait(false);
            return manifest ?? new NetworkProfileRoamingManifest();
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Existing network profile roaming manifest could not be read. It will be replaced.");
            return new NetworkProfileRoamingManifest();
        }
    }

    private async Task WriteManifestAsync(NetworkProfileRoamingManifest manifest, CancellationToken cancellationToken)
    {
        string manifestPath = Path.Combine(_artifactRootPath, ManifestFileName);
        await using FileStream stream = File.Create(manifestPath);
        await JsonSerializer.SerializeAsync(stream, manifest, JsonOptions, cancellationToken).ConfigureAwait(false);
    }

    private async Task<IReadOnlyList<NetworkProfileRoamingCertificate>> CopyCertificatesAsync(
        NetworkProfileRoamingCaptureRequest request,
        CancellationToken cancellationToken)
    {
        List<NetworkProfileRoamingCertificate> certificates = [];
        foreach (string certificatePath in request.CertificatePaths ?? [])
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (string.IsNullOrWhiteSpace(certificatePath) || !File.Exists(certificatePath))
            {
                continue;
            }

            if (IsPfxPath(certificatePath))
            {
                NetworkProfileRoamingCertificate? pfxCertificate = await CopyPfxCertificateAsync(
                    request.Source,
                    certificatePath,
                    request.CertificatePfxPasswordSecret,
                    cancellationToken).ConfigureAwait(false);
                if (pfxCertificate is not null)
                {
                    certificates.Add(pfxCertificate);
                }

                continue;
            }

            certificates.Add(CopyPublicCertificate(request.Source, certificatePath));
        }

        return certificates;
    }

    private NetworkProfileRoamingCertificate CopyPublicCertificate(
        NetworkProfileRoamingProfileSource source,
        string certificatePath)
    {
        string sourceName = FormatSource(source);
        string certificateRootPath = Path.Combine(_artifactRootPath, CertificatesDirectoryName, RootStoreName, sourceName);
        Directory.CreateDirectory(certificateRootPath);
        string fileName = CreateStableCertificateFileName(certificatePath);
        string destinationPath = Path.Combine(certificateRootPath, fileName);
        File.Copy(certificatePath, destinationPath, overwrite: true);

        return new NetworkProfileRoamingCertificate
        {
            RelativePath = NormalizeRelativePath(Path.Combine(CertificatesDirectoryName, RootStoreName, sourceName, fileName)),
            Kind = PublicCertificateKind,
            StoreName = RootStoreName
        };
    }

    private async Task<NetworkProfileRoamingCertificate?> CopyPfxCertificateAsync(
        NetworkProfileRoamingProfileSource source,
        string certificatePath,
        SecretEnvelope? passwordSecret,
        CancellationToken cancellationToken)
    {
        if (!_configuration.Network.ProfileRoaming.IncludePrivateKeyMaterial || passwordSecret is null)
        {
            _logger.LogDebug("PFX certificate was not copied to the network profile roaming artifact because private-key roaming is disabled or no encrypted password secret is available.");
            return null;
        }

        string sourceName = FormatSource(source);
        string certificateRootPath = Path.Combine(_artifactRootPath, CertificatesDirectoryName, MyStoreName, sourceName);
        Directory.CreateDirectory(certificateRootPath);
        string fileName = CreateStableCertificateFileName(certificatePath);
        string destinationPath = Path.Combine(certificateRootPath, fileName);
        File.Copy(certificatePath, destinationPath, overwrite: true);

        string passwordSecretFileName = $"{fileName}.password.json";
        string passwordSecretPath = Path.Combine(certificateRootPath, passwordSecretFileName);
        await using FileStream stream = File.Create(passwordSecretPath);
        await JsonSerializer.SerializeAsync(stream, passwordSecret, JsonOptions, cancellationToken).ConfigureAwait(false);

        return new NetworkProfileRoamingCertificate
        {
            RelativePath = NormalizeRelativePath(Path.Combine(CertificatesDirectoryName, MyStoreName, sourceName, fileName)),
            Kind = PfxPrivateKeyKind,
            StoreName = MyStoreName,
            PasswordSecretRelativePath = NormalizeRelativePath(Path.Combine(CertificatesDirectoryName, MyStoreName, sourceName, passwordSecretFileName))
        };
    }

    private static bool IsPfxPath(string certificatePath)
    {
        string extension = Path.GetExtension(certificatePath);
        return string.Equals(extension, ".pfx", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(extension, ".p12", StringComparison.OrdinalIgnoreCase);
    }

    private void DeleteCertificateArtifactsForSource(string source)
    {
        DeleteDirectory(Path.Combine(_artifactRootPath, CertificatesDirectoryName, RootStoreName, source));
        DeleteDirectory(Path.Combine(_artifactRootPath, CertificatesDirectoryName, MyStoreName, source));
    }

    private void DeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to delete stale network profile roaming certificate artifacts at {Path}.", path);
        }
    }

    private static bool IsCertificateOwnedBySource(NetworkProfileRoamingCertificate certificate, string source)
    {
        string rootPrefix = NormalizeRelativePath(Path.Combine(CertificatesDirectoryName, RootStoreName, source)) + "\\";
        string myPrefix = NormalizeRelativePath(Path.Combine(CertificatesDirectoryName, MyStoreName, source)) + "\\";
        return certificate.RelativePath.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase) ||
            certificate.RelativePath.StartsWith(myPrefix, StringComparison.OrdinalIgnoreCase);
    }

    private static string CreateStableCertificateFileName(string certificatePath)
    {
        string extension = Path.GetExtension(certificatePath);
        string fileNameWithoutExtension = Path.GetFileNameWithoutExtension(certificatePath);
        string pathHash = Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(Path.GetFullPath(certificatePath))))
            [..16]
            .ToLowerInvariant();
        return $"{fileNameWithoutExtension}-{pathHash}{extension}";
    }

    private static string FormatSource(NetworkProfileRoamingProfileSource source)
    {
        return source switch
        {
            NetworkProfileRoamingProfileSource.ManualWifi => "manualWifi",
            NetworkProfileRoamingProfileSource.ProvisionedWifi => "provisionedWifi",
            NetworkProfileRoamingProfileSource.ProvisionedWiredDot1x => "provisionedWiredDot1x",
            _ => throw new InvalidOperationException($"Unsupported network profile roaming source '{source}'.")
        };
    }

    private static string FormatConnectivityExpectation(NetworkProfileRoamingConnectivityExpectation expectation)
    {
        return expectation switch
        {
            NetworkProfileRoamingConnectivityExpectation.PreOobeConnectable => "preOobeConnectable",
            NetworkProfileRoamingConnectivityExpectation.ImportOnly => "importOnly",
            NetworkProfileRoamingConnectivityExpectation.DependsOnMachineCredential => "dependsOnMachineCredential",
            _ => throw new InvalidOperationException($"Unsupported network profile roaming connectivity expectation '{expectation}'.")
        };
    }

    private static string NormalizeRelativePath(string relativePath)
    {
        return relativePath.Replace('/', '\\').TrimStart('\\');
    }
}
