// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using Foundry.Deploy.Services.Http;
using Microsoft.Extensions.Logging;

namespace Foundry.Deploy.Services.Download;

public sealed class ArtifactDownloadService : IArtifactDownloadService
{
    private static readonly HttpClient DefaultHttpClient = InsecureHttpClientFactory.Create(TimeSpan.FromMinutes(30));
    private static readonly TimeSpan ProgressReportInterval = TimeSpan.FromMilliseconds(100);
    private const int CopyBufferSize = 80 * 1024;

    private readonly HttpClient _httpClient;
    private readonly ILogger<ArtifactDownloadService> _logger;

    public ArtifactDownloadService(ILogger<ArtifactDownloadService> logger)
        : this(logger, DefaultHttpClient)
    {
    }

    internal ArtifactDownloadService(ILogger<ArtifactDownloadService> logger, HttpClient httpClient)
    {
        _logger = logger;
        _httpClient = httpClient;
    }

    public async Task<ArtifactDownloadResult> DownloadAsync(
        string sourceUrl,
        string destinationPath,
        string? expectedHash = null,
        long? expectedSizeBytes = null,
        string? artifactKind = null,
        CancellationToken cancellationToken = default,
        IProgress<DownloadProgress>? progress = null)
    {
        string effectiveSourceUrl = NormalizeSourceUrl(sourceUrl);

        _logger.LogInformation("Starting artifact download. SourceUrl={SourceUrl}, DestinationPath={DestinationPath}",
            sourceUrl,
            destinationPath);

        if (string.IsNullOrWhiteSpace(sourceUrl))
        {
            throw new ArgumentException("Source URL is required.", nameof(sourceUrl));
        }

        if (string.IsNullOrWhiteSpace(destinationPath))
        {
            throw new ArgumentException("Destination path is required.", nameof(destinationPath));
        }

        string destinationDirectory = Path.GetDirectoryName(destinationPath)
            ?? throw new InvalidOperationException("Unable to resolve destination directory.");
        Directory.CreateDirectory(destinationDirectory);

        if (!string.Equals(sourceUrl, effectiveSourceUrl, StringComparison.Ordinal))
        {
            _logger.LogInformation(
                "Normalized artifact source URL for Windows Update content host. OriginalSourceUrl={OriginalSourceUrl}, EffectiveSourceUrl={EffectiveSourceUrl}",
                sourceUrl,
                effectiveSourceUrl);
        }

        try
        {
            string? normalizedExpectedHash = null;
            HashAlgorithmName? hashAlgorithm = null;
            if (!string.IsNullOrWhiteSpace(expectedHash))
            {
                normalizedExpectedHash = NormalizeHash(expectedHash);
                hashAlgorithm = ResolveHashAlgorithm(normalizedExpectedHash);
            }

            if (File.Exists(destinationPath) &&
                await TryUseExistingArtifactAsync(
                    sourceUrl,
                    destinationPath,
                    normalizedExpectedHash,
                    hashAlgorithm,
                    expectedSizeBytes,
                    artifactKind,
                    cancellationToken,
                    progress).ConfigureAwait(false))
            {
                long cachedSize = new FileInfo(destinationPath).Length;
                _logger.LogInformation("Artifact cache hit for DestinationPath={DestinationPath}.", destinationPath);
                return new ArtifactDownloadResult
                {
                    DestinationPath = destinationPath,
                    Downloaded = false,
                    Method = "cache-hit",
                    SizeBytes = cachedSize
                };
            }

            DownloadedArtifact downloadedArtifact = new(null);
            await HttpRetryPolicy
                .ExecuteAsync(
                    async ct =>
                    {
                        downloadedArtifact = await DownloadWithHttpClientAsync(
                                effectiveSourceUrl,
                                destinationPath,
                                hashAlgorithm,
                                progress,
                                ct)
                            .ConfigureAwait(false);
                    },
                    _logger,
                    "Artifact download",
                    cancellationToken)
                .ConfigureAwait(false);
            await EnsureDownloadedHashAsync(destinationPath, normalizedExpectedHash, hashAlgorithm, downloadedArtifact, cancellationToken).ConfigureAwait(false);

            if (normalizedExpectedHash is not null && hashAlgorithm is not null)
            {
                await ArtifactCacheManifestService
                    .WriteAsync(
                        destinationPath,
                        sourceUrl,
                        artifactKind,
                        normalizedExpectedHash,
                        hashAlgorithm.Value.Name ?? string.Empty,
                        expectedSizeBytes,
                        cancellationToken)
                    .ConfigureAwait(false);
            }

            _logger.LogInformation("Artifact downloaded via HttpClient. DestinationPath={DestinationPath}", destinationPath);
            return new ArtifactDownloadResult
            {
                DestinationPath = destinationPath,
                Downloaded = true,
                Method = "httpclient",
                SizeBytes = new FileInfo(destinationPath).Length
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Artifact download failed. SourceUrl={SourceUrl}, EffectiveSourceUrl={EffectiveSourceUrl}, SourceHost={SourceHost}, DestinationPath={DestinationPath}",
                sourceUrl,
                effectiveSourceUrl,
                TryGetSourceHost(effectiveSourceUrl),
                destinationPath);
            throw;
        }
    }

    private async Task<bool> TryUseExistingArtifactAsync(
        string sourceUrl,
        string destinationPath,
        string? normalizedExpectedHash,
        HashAlgorithmName? hashAlgorithm,
        long? expectedSizeBytes,
        string? artifactKind,
        CancellationToken cancellationToken,
        IProgress<DownloadProgress>? progress)
    {
        FileInfo artifact = new(destinationPath);
        if (expectedSizeBytes is > 0 && artifact.Length != expectedSizeBytes.Value)
        {
            _logger.LogInformation(
                "Artifact cache file size mismatch. DestinationPath={DestinationPath}, ExpectedSizeBytes={ExpectedSizeBytes}, ActualSizeBytes={ActualSizeBytes}",
                destinationPath,
                expectedSizeBytes.Value,
                artifact.Length);
            return false;
        }

        if (normalizedExpectedHash is null || hashAlgorithm is null)
        {
            progress?.Report(new DownloadProgress(artifact.Length, artifact.Length));
            return true;
        }

        if (ArtifactCacheManifestService.TryValidate(
                destinationPath,
                normalizedExpectedHash,
                hashAlgorithm.Value.Name ?? string.Empty,
                expectedSizeBytes,
                _logger,
                out _))
        {
            progress?.Report(new DownloadProgress(artifact.Length, artifact.Length));
            return true;
        }

        string actualHash = await ComputeHashAsync(destinationPath, hashAlgorithm.Value, cancellationToken).ConfigureAwait(false);
        if (!string.Equals(normalizedExpectedHash, actualHash, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        await ArtifactCacheManifestService
            .WriteAsync(
                destinationPath,
                sourceUrl,
                artifactKind,
                normalizedExpectedHash,
                hashAlgorithm.Value.Name ?? string.Empty,
                expectedSizeBytes,
                cancellationToken)
            .ConfigureAwait(false);

        progress?.Report(new DownloadProgress(artifact.Length, artifact.Length));
        return true;
    }

    private async Task<DownloadedArtifact> DownloadWithHttpClientAsync(
        string sourceUrl,
        string destinationPath,
        HashAlgorithmName? hashAlgorithm,
        IProgress<DownloadProgress>? progress,
        CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await _httpClient
            .GetAsync(sourceUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);

        response.EnsureSuccessStatusCode();
        long? totalBytes = response.Content.Headers.ContentLength;

        await using Stream sourceStream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        await using FileStream destinationStream = new(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None, CopyBufferSize, useAsync: true);
        using IncrementalHash? incrementalHash = hashAlgorithm is null
            ? null
            : IncrementalHash.CreateHash(hashAlgorithm.Value);
        byte[] buffer = new byte[CopyBufferSize];
        long bytesDownloaded = 0;
        DateTimeOffset nextReportAt = DateTimeOffset.UtcNow;

        progress?.Report(new DownloadProgress(bytesDownloaded, totalBytes));

        while (true)
        {
            int bytesRead = await sourceStream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken).ConfigureAwait(false);
            if (bytesRead == 0)
            {
                break;
            }

            incrementalHash?.AppendData(buffer.AsSpan(0, bytesRead));
            await destinationStream.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken).ConfigureAwait(false);
            bytesDownloaded += bytesRead;

            DateTimeOffset now = DateTimeOffset.UtcNow;
            if (progress is not null && now >= nextReportAt)
            {
                progress.Report(new DownloadProgress(bytesDownloaded, totalBytes));
                nextReportAt = now + ProgressReportInterval;
            }
        }

        progress?.Report(new DownloadProgress(bytesDownloaded, totalBytes));
        string? actualHash = incrementalHash is null
            ? null
            : Convert.ToHexString(incrementalHash.GetHashAndReset());
        return new DownloadedArtifact(actualHash);
    }

    private static async Task EnsureDownloadedHashAsync(
        string filePath,
        string? normalizedExpectedHash,
        HashAlgorithmName? hashAlgorithm,
        DownloadedArtifact downloadedArtifact,
        CancellationToken cancellationToken)
    {
        if (normalizedExpectedHash is null || hashAlgorithm is null)
        {
            return;
        }

        string actual = downloadedArtifact.Hash ?? await ComputeHashAsync(filePath, hashAlgorithm.Value, cancellationToken).ConfigureAwait(false);
        if (!string.Equals(normalizedExpectedHash, actual, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Hash verification failed for '{filePath}' ({hashAlgorithm.Value.Name}). Expected '{normalizedExpectedHash}', actual '{actual}'.");
        }
    }

    private static string NormalizeHash(string hash)
    {
        return hash
            .Trim()
            .Replace("-", string.Empty, StringComparison.OrdinalIgnoreCase)
            .ToUpperInvariant();
    }

    private static HashAlgorithmName ResolveHashAlgorithm(string normalizedHash)
    {
        return normalizedHash.Length switch
        {
            40 => HashAlgorithmName.SHA1,
            64 => HashAlgorithmName.SHA256,
            _ => throw new InvalidOperationException(
                $"Unsupported expected hash length ({normalizedHash.Length}). Only SHA1 (40) and SHA256 (64) are supported.")
        };
    }

    private static async Task<string> ComputeHashAsync(
        string filePath,
        HashAlgorithmName algorithm,
        CancellationToken cancellationToken)
    {
        using HashAlgorithm hashAlgorithm = algorithm.Name switch
        {
            "SHA1" => SHA1.Create(),
            "SHA256" => SHA256.Create(),
            _ => throw new InvalidOperationException($"Unsupported hash algorithm '{algorithm.Name}'.")
        };

        await using FileStream stream = new(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);
        byte[] hash = await hashAlgorithm.ComputeHashAsync(stream, cancellationToken).ConfigureAwait(false);
        return Convert.ToHexString(hash);
    }

    private static string TryGetSourceHost(string sourceUrl)
    {
        return Uri.TryCreate(sourceUrl, UriKind.Absolute, out Uri? uri)
            ? uri.Host
            : "invalid-url";
    }

    private static string NormalizeSourceUrl(string sourceUrl)
    {
        if (!Uri.TryCreate(sourceUrl, UriKind.Absolute, out Uri? uri))
        {
            return sourceUrl;
        }

        if (!string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            return sourceUrl;
        }

        if (!IsWindowsUpdateContentHost(uri.Host))
        {
            return sourceUrl;
        }

        UriBuilder builder = new(uri)
        {
            Scheme = Uri.UriSchemeHttp,
            Port = uri.Port == 443 ? 80 : uri.Port
        };

        return builder.Uri.AbsoluteUri;
    }

    private static bool IsWindowsUpdateContentHost(string host)
    {
        return host.Equals("dl.delivery.mp.microsoft.com", StringComparison.OrdinalIgnoreCase) ||
               host.EndsWith(".dl.delivery.mp.microsoft.com", StringComparison.OrdinalIgnoreCase);
    }

    private sealed record DownloadedArtifact(string? Hash);
}
