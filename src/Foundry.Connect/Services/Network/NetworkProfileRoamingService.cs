// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Foundry.Core.Models.Configuration;
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

    private readonly FoundryConnectConfigurationDocument _configuration;
    private readonly ILogger<NetworkProfileRoamingService> _logger;
    private readonly string _artifactRootPath;
    private bool _artifactRootPrepared;

    /// <summary>
    /// Initializes a network profile roaming service.
    /// </summary>
    /// <param name="configuration">The loaded runtime configuration.</param>
    /// <param name="logger">Logger.</param>
    public NetworkProfileRoamingService(
        FoundryConnectConfigurationDocument configuration,
        ILogger<NetworkProfileRoamingService> logger)
        : this(configuration, logger, ConnectWorkspacePaths.GetNetworkProfileRoamingDirectory())
    {
    }

    internal NetworkProfileRoamingService(
        FoundryConnectConfigurationDocument configuration,
        ILogger<NetworkProfileRoamingService> logger,
        string artifactRootPath)
    {
        _configuration = configuration;
        _logger = logger;
        _artifactRootPath = artifactRootPath;

        if (_configuration.Network.ProfileRoaming.IsAnyEnabled)
        {
            PrepareArtifactRoot();
        }
    }

    /// <inheritdoc />
    public Task CaptureWifiProfileAsync(NetworkProfileRoamingCaptureRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return CaptureWifiProfilesAsync([request], cancellationToken);
    }

    /// <inheritdoc />
    public async Task CaptureWifiProfilesAsync(IReadOnlyList<NetworkProfileRoamingCaptureRequest> requests, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(requests);

        Foundry.Core.Models.Configuration.NetworkProfileRoamingTransportSettings transportSettings = _configuration.Network.ProfileRoaming.Wifi;
        if (!transportSettings.IsEnabled)
        {
            return;
        }

        NetworkProfileRoamingCaptureRequest[] available = requests.Where(request => IsSourceAvailable(request, isWifiProfile: true)).ToArray();
        if (available.Length == 0)
        {
            return;
        }

        bool startedFreshSession = PrepareArtifactRoot();
        var profiles = new NetworkProfileRoamingProfile[available.Length];
        for (int index = 0; index < available.Length; index++)
        {
            string fileName = NetworkProfileRoamingArtifacts.GetWifiProfileFileName(index);
            await WriteProfileFileAsync(available[index], fileName, cancellationToken).ConfigureAwait(false);
            profiles[index] = CreateProfile(available[index], fileName);
        }

        NetworkProfileRoamingManifest existingManifest = await LoadExistingManifestAsync(startedFreshSession, cancellationToken).ConfigureAwait(false);
        IReadOnlyList<NetworkProfileRoamingCertificate> certificates = await ReplaceCertificatesAsync(existingManifest, available, transportSettings, cancellationToken)
            .ConfigureAwait(false);
        NetworkProfileRoamingManifest manifest = existingManifest with
        {
            SchemaVersion = NetworkProfileRoamingManifest.CurrentSchemaVersion,
            UpdatedAtUtc = DateTimeOffset.UtcNow,
            WifiProfile = profiles[0],
            WifiProfiles = profiles,
            Certificates = certificates
        };

        await WriteManifestAsync(manifest, cancellationToken).ConfigureAwait(false);
        _logger.LogInformation(
            "Network profile roaming capture completed. ProfileKind={ProfileKind}, ProfileCount={ProfileCount}, ProvisionedCount={ProvisionedCount}, ManualCount={ManualCount}, CertificateCount={CertificateCount}, PrivateKeyRoamingEnabled={PrivateKeyRoamingEnabled}",
            "Wifi",
            available.Length,
            available.Count(static request => request.Source == NetworkProfileRoamingProfileSource.ProvisionedWifi),
            available.Count(static request => request.Source == NetworkProfileRoamingProfileSource.ManualWifi),
            manifest.Certificates.Count,
            transportSettings.IncludePrivateKeyMaterial);
    }

    /// <inheritdoc />
    public async Task CaptureWiredDot1xProfileAsync(NetworkProfileRoamingCaptureRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        Foundry.Core.Models.Configuration.NetworkProfileRoamingTransportSettings transportSettings = _configuration.Network.ProfileRoaming.WiredDot1x;
        if (!transportSettings.IsEnabled || !IsSourceAvailable(request, isWifiProfile: false))
        {
            return;
        }

        bool startedFreshSession = PrepareArtifactRoot();
        await WriteProfileFileAsync(request, NetworkProfileRoamingArtifacts.WiredDot1xProfileFileName, cancellationToken).ConfigureAwait(false);

        NetworkProfileRoamingManifest existingManifest = await LoadExistingManifestAsync(startedFreshSession, cancellationToken).ConfigureAwait(false);
        IReadOnlyList<NetworkProfileRoamingCertificate> certificates = await ReplaceCertificatesAsync(existingManifest, [request], transportSettings, cancellationToken)
            .ConfigureAwait(false);
        NetworkProfileRoamingManifest manifest = existingManifest with
        {
            SchemaVersion = NetworkProfileRoamingManifest.CurrentSchemaVersion,
            UpdatedAtUtc = DateTimeOffset.UtcNow,
            WiredDot1xProfile = CreateProfile(request, NetworkProfileRoamingArtifacts.WiredDot1xProfileFileName),
            Certificates = certificates
        };

        await WriteManifestAsync(manifest, cancellationToken).ConfigureAwait(false);
        _logger.LogInformation(
            "Network profile roaming capture completed. ProfileKind={ProfileKind}, Source={Source}, ConnectivityExpectation={ConnectivityExpectation}, CertificateCount={CertificateCount}, PrivateKeyRoamingEnabled={PrivateKeyRoamingEnabled}",
            "WiredDot1x",
            request.Source,
            request.ConnectivityExpectation,
            manifest.Certificates.Count,
            transportSettings.IncludePrivateKeyMaterial);
    }

    private bool IsSourceAvailable(NetworkProfileRoamingCaptureRequest request, bool isWifiProfile)
    {
        if (request.ProfileXml is not null ||
            (!string.IsNullOrWhiteSpace(request.ProfilePath) && File.Exists(request.ProfilePath)))
        {
            return true;
        }

        _logger.LogWarning(
            "Network profile roaming skipped because the source profile is unavailable. ProfileKind={ProfileKind}, Source={Source}",
            isWifiProfile ? "Wifi" : "WiredDot1x",
            request.Source);
        return false;
    }

    // XML read back from WLAN is written as UTF-8 without a BOM; a source file is copied byte for byte.
    private async Task WriteProfileFileAsync(NetworkProfileRoamingCaptureRequest request, string destinationFileName, CancellationToken cancellationToken)
    {
        string destinationPath = Path.Combine(_artifactRootPath, destinationFileName);
        if (request.ProfileXml is not null)
        {
            await File.WriteAllTextAsync(destinationPath, request.ProfileXml, new UTF8Encoding(false), cancellationToken).ConfigureAwait(false);
            return;
        }

        File.Copy(request.ProfilePath!, destinationPath, overwrite: true);
    }

    private static NetworkProfileRoamingProfile CreateProfile(NetworkProfileRoamingCaptureRequest request, string fileName)
    {
        return new NetworkProfileRoamingProfile
        {
            RelativePath = fileName,
            Source = FormatSource(request.Source),
            ConnectivityExpectation = FormatConnectivityExpectation(request.ConnectivityExpectation)
        };
    }

    private async Task<NetworkProfileRoamingManifest> LoadExistingManifestAsync(bool startedFreshSession, CancellationToken cancellationToken)
    {
        return startedFreshSession
            ? new NetworkProfileRoamingManifest()
            : await LoadManifestAsync(cancellationToken).ConfigureAwait(false);
    }

    // The certificates of every source being captured are replaced; other sources keep theirs.
    private async Task<IReadOnlyList<NetworkProfileRoamingCertificate>> ReplaceCertificatesAsync(
        NetworkProfileRoamingManifest existingManifest,
        IReadOnlyList<NetworkProfileRoamingCaptureRequest> requests,
        Foundry.Core.Models.Configuration.NetworkProfileRoamingTransportSettings transportSettings,
        CancellationToken cancellationToken)
    {
        string[] sources = requests.Select(static request => FormatSource(request.Source)).Distinct(StringComparer.Ordinal).ToArray();
        foreach (string source in sources)
        {
            DeleteCertificateArtifactsForSource(source);
        }

        List<NetworkProfileRoamingCertificate> certificates = existingManifest.Certificates
            .Where(certificate => !sources.Any(source => NetworkProfileRoamingArtifacts.IsCertificateOwnedBySource(certificate, source)))
            .ToList();
        foreach (NetworkProfileRoamingCaptureRequest request in requests)
        {
            certificates.AddRange(await CopyCertificatesAsync(request, transportSettings, cancellationToken).ConfigureAwait(false));
        }

        return certificates
            .GroupBy(static certificate => certificate.RelativePath, StringComparer.OrdinalIgnoreCase)
            .Select(static group => group.Last())
            .ToArray();
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
            // The exception is not attached: its message can name the manifest path or echo profile content.
            _logger.LogDebug(
                "Existing network profile roaming manifest could not be read. It will be replaced. FailureType={FailureType}, ErrorCode={ErrorCode}",
                ex.GetType().Name,
                ex.HResult);
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
        Foundry.Core.Models.Configuration.NetworkProfileRoamingTransportSettings transportSettings,
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

            // A protected PFX is the deployment-keyed envelope file: it follows the private-key rule and stays encrypted.
            bool isProtectedPfx = NetworkProfileRoamingArtifacts.IsProtectedPfxPath(certificatePath);
            if (isProtectedPfx || NetworkProfileRoamingArtifacts.IsPfxPath(certificatePath))
            {
                NetworkProfileRoamingCertificate? pfxCertificate = await CopyPfxCertificateAsync(
                    request.Source,
                    certificatePath,
                    request.CertificatePfxPasswordSecret,
                    transportSettings.IncludePrivateKeyMaterial,
                    isProtectedPfx,
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
        bool includePrivateKeyMaterial,
        bool isContentProtected,
        CancellationToken cancellationToken)
    {
        if (!includePrivateKeyMaterial)
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
            PasswordSecretRelativePath = passwordSecretRelativePath,
            IsContentProtected = isContentProtected
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
            _logger.LogWarning(
                ex,
                "Failed to delete stale network profile roaming artifacts. ArtifactName={ArtifactName}",
                Path.GetFileName(path));
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
