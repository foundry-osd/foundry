using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Foundry.Connect.Models.Configuration;
using Foundry.Core.Models.Network;
using Foundry.Connect.Services.Runtime;
using Microsoft.Extensions.Logging;

namespace Foundry.Connect.Services.Network;

/// <summary>
/// Writes Foundry-managed network profile roaming artifacts in WinPE.
/// </summary>
public sealed class NetworkProfileRoamingService : INetworkProfileRoamingService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly FoundryConnectConfiguration _configuration;
    private readonly ILogger<NetworkProfileRoamingService> _logger;
    private readonly string _artifactRootPath;
    private bool _artifactRootPrepared;

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

        if (_configuration.Network.ProfileRoaming.IsEnabled)
        {
            PrepareArtifactRoot();
        }
    }

    /// <inheritdoc />
    public Task CaptureWifiProfileAsync(NetworkProfileRoamingCaptureRequest request, CancellationToken cancellationToken)
    {
        return CaptureProfileAsync(request, NetworkProfileRoamingArtifacts.WifiProfileFileName, isWifiProfile: true, cancellationToken);
    }

    /// <inheritdoc />
    public Task CaptureWiredDot1xProfileAsync(NetworkProfileRoamingCaptureRequest request, CancellationToken cancellationToken)
    {
        return CaptureProfileAsync(request, NetworkProfileRoamingArtifacts.WiredDot1xProfileFileName, isWifiProfile: false, cancellationToken);
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

        bool startedFreshSession = PrepareArtifactRoot();
        string destinationPath = Path.Combine(_artifactRootPath, destinationFileName);
        File.Copy(request.ProfilePath, destinationPath, overwrite: true);

        string source = FormatSource(request.Source);
        NetworkProfileRoamingManifest existingManifest = startedFreshSession
            ? new NetworkProfileRoamingManifest()
            : await LoadManifestAsync(cancellationToken).ConfigureAwait(false);
        DeleteCertificateArtifactsForSource(source);
        List<NetworkProfileRoamingCertificate> certificates = existingManifest.Certificates
            .Where(certificate => !NetworkProfileRoamingArtifacts.IsCertificateOwnedBySource(certificate, source))
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

    private bool PrepareArtifactRoot()
    {
        if (_artifactRootPrepared)
        {
            return false;
        }

        DeleteDirectory(_artifactRootPath);
        Directory.CreateDirectory(_artifactRootPath);
        _artifactRootPrepared = true;
        return true;
    }

    private async Task<NetworkProfileRoamingManifest> LoadManifestAsync(CancellationToken cancellationToken)
    {
        string manifestPath = Path.Combine(_artifactRootPath, NetworkProfileRoamingArtifacts.ManifestFileName);
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
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Existing network profile roaming manifest could not be read. It will be replaced.");
            return new NetworkProfileRoamingManifest();
        }
    }

    private async Task WriteManifestAsync(NetworkProfileRoamingManifest manifest, CancellationToken cancellationToken)
    {
        string manifestPath = Path.Combine(_artifactRootPath, NetworkProfileRoamingArtifacts.ManifestFileName);
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

            if (NetworkProfileRoamingArtifacts.IsPfxPath(certificatePath))
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
        string certificateRootPath = Path.Combine(_artifactRootPath, NetworkProfileRoamingArtifacts.CertificatesDirectoryName, NetworkProfileRoamingArtifacts.RootStoreName, sourceName);
        Directory.CreateDirectory(certificateRootPath);
        string fileName = NetworkProfileRoamingArtifacts.CreateStableCertificateFileName(certificatePath);
        string destinationPath = Path.Combine(certificateRootPath, fileName);
        File.Copy(certificatePath, destinationPath, overwrite: true);

        return new NetworkProfileRoamingCertificate
        {
            RelativePath = NetworkProfileRoamingArtifacts.NormalizeRelativePath(Path.Combine(NetworkProfileRoamingArtifacts.CertificatesDirectoryName, NetworkProfileRoamingArtifacts.RootStoreName, sourceName, fileName)),
            Kind = NetworkProfileRoamingArtifacts.PublicCertificateKind,
            StoreName = NetworkProfileRoamingArtifacts.RootStoreName
        };
    }

    private async Task<NetworkProfileRoamingCertificate?> CopyPfxCertificateAsync(
        NetworkProfileRoamingProfileSource source,
        string certificatePath,
        SecretEnvelope? passwordSecret,
        CancellationToken cancellationToken)
    {
        if (!_configuration.Network.ProfileRoaming.IncludePrivateKeyMaterial)
        {
            _logger.LogDebug("PFX certificate was not copied to the network profile roaming artifact because private-key roaming is disabled.");
            return null;
        }

        string sourceName = FormatSource(source);
        string certificateRootPath = Path.Combine(_artifactRootPath, NetworkProfileRoamingArtifacts.CertificatesDirectoryName, NetworkProfileRoamingArtifacts.MyStoreName, sourceName);
        Directory.CreateDirectory(certificateRootPath);
        string fileName = NetworkProfileRoamingArtifacts.CreateStableCertificateFileName(certificatePath);
        string destinationPath = Path.Combine(certificateRootPath, fileName);
        File.Copy(certificatePath, destinationPath, overwrite: true);

        string? passwordSecretRelativePath = null;
        if (passwordSecret is not null)
        {
            string passwordSecretFileName = $"{fileName}.password.json";
            string passwordSecretPath = Path.Combine(certificateRootPath, passwordSecretFileName);
            await using FileStream stream = File.Create(passwordSecretPath);
            await JsonSerializer.SerializeAsync(stream, passwordSecret, JsonOptions, cancellationToken).ConfigureAwait(false);
            passwordSecretRelativePath = NetworkProfileRoamingArtifacts.NormalizeRelativePath(Path.Combine(NetworkProfileRoamingArtifacts.CertificatesDirectoryName, NetworkProfileRoamingArtifacts.MyStoreName, sourceName, passwordSecretFileName));
        }

        return new NetworkProfileRoamingCertificate
        {
            RelativePath = NetworkProfileRoamingArtifacts.NormalizeRelativePath(Path.Combine(NetworkProfileRoamingArtifacts.CertificatesDirectoryName, NetworkProfileRoamingArtifacts.MyStoreName, sourceName, fileName)),
            Kind = NetworkProfileRoamingArtifacts.PfxPrivateKeyKind,
            StoreName = NetworkProfileRoamingArtifacts.MyStoreName,
            PasswordSecretRelativePath = passwordSecretRelativePath
        };
    }

    private void DeleteCertificateArtifactsForSource(string source)
    {
        DeleteDirectory(Path.Combine(_artifactRootPath, NetworkProfileRoamingArtifacts.CertificatesDirectoryName, NetworkProfileRoamingArtifacts.RootStoreName, source));
        DeleteDirectory(Path.Combine(_artifactRootPath, NetworkProfileRoamingArtifacts.CertificatesDirectoryName, NetworkProfileRoamingArtifacts.MyStoreName, source));
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

    private static string FormatSource(NetworkProfileRoamingProfileSource source)
    {
        return source switch
        {
            NetworkProfileRoamingProfileSource.ManualWifi => NetworkProfileRoamingArtifacts.ManualWifiSource,
            NetworkProfileRoamingProfileSource.ProvisionedWifi => NetworkProfileRoamingArtifacts.ProvisionedWifiSource,
            NetworkProfileRoamingProfileSource.ProvisionedWiredDot1x => NetworkProfileRoamingArtifacts.ProvisionedWiredDot1xSource,
            _ => throw new InvalidOperationException($"Unsupported network profile roaming source '{source}'.")
        };
    }

    private static string FormatConnectivityExpectation(NetworkProfileRoamingConnectivityExpectation expectation)
    {
        return expectation switch
        {
            NetworkProfileRoamingConnectivityExpectation.PreOobeConnectable => NetworkProfileRoamingArtifacts.PreOobeConnectableExpectation,
            NetworkProfileRoamingConnectivityExpectation.ImportOnly => NetworkProfileRoamingArtifacts.ImportOnlyExpectation,
            NetworkProfileRoamingConnectivityExpectation.DependsOnMachineCredential => NetworkProfileRoamingArtifacts.DependsOnMachineCredentialExpectation,
            _ => throw new InvalidOperationException($"Unsupported network profile roaming connectivity expectation '{expectation}'.")
        };
    }

}
