using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.IO;
using Foundry.Deploy.Services.System;
using Microsoft.Extensions.Logging;

namespace Foundry.Deploy.Services.Download;

public sealed class ArtifactDownloadService : IArtifactDownloadService
{
    private static readonly HttpClient HttpClient = new()
    {
        Timeout = TimeSpan.FromMinutes(30)
    };

    private readonly IProcessRunner _processRunner;
    private readonly ILogger<ArtifactDownloadService> _logger;

    public ArtifactDownloadService(IProcessRunner processRunner, ILogger<ArtifactDownloadService> logger)
    {
        _processRunner = processRunner;
        _logger = logger;
    }

    public async Task<ArtifactDownloadResult> DownloadAsync(
        string sourceUrl,
        string destinationPath,
        string? expectedHash = null,
        bool preferBits = true,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Starting artifact download. SourceUrl={SourceUrl}, DestinationPath={DestinationPath}, PreferBits={PreferBits}",
            sourceUrl,
            destinationPath,
            preferBits);

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

        try
        {
            if (File.Exists(destinationPath) && await ValidateHashIfRequestedAsync(destinationPath, expectedHash, cancellationToken).ConfigureAwait(false))
            {
                _logger.LogInformation("Artifact cache hit for DestinationPath={DestinationPath}.", destinationPath);
                return new ArtifactDownloadResult
                {
                    DestinationPath = destinationPath,
                    Downloaded = false,
                    Method = "cache-hit",
                    SizeBytes = new FileInfo(destinationPath).Length
                };
            }

            if (preferBits && await TryBitsDownloadAsync(sourceUrl, destinationPath, cancellationToken).ConfigureAwait(false))
            {
                _logger.LogInformation("Artifact downloaded via BITS. DestinationPath={DestinationPath}", destinationPath);
                await EnsureHashAsync(destinationPath, expectedHash, cancellationToken).ConfigureAwait(false);
                return new ArtifactDownloadResult
                {
                    DestinationPath = destinationPath,
                    Downloaded = true,
                    Method = "bits",
                    SizeBytes = new FileInfo(destinationPath).Length
                };
            }

            _logger.LogInformation("Falling back to HttpClient download. SourceUrl={SourceUrl}", sourceUrl);
            await DownloadWithHttpClientAsync(sourceUrl, destinationPath, cancellationToken).ConfigureAwait(false);
            await EnsureHashAsync(destinationPath, expectedHash, cancellationToken).ConfigureAwait(false);

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
            _logger.LogError(ex, "Artifact download failed. SourceUrl={SourceUrl}, DestinationPath={DestinationPath}", sourceUrl, destinationPath);
            throw;
        }
    }

    private async Task<bool> TryBitsDownloadAsync(string sourceUrl, string destinationPath, CancellationToken cancellationToken)
    {
        string escapedUrl = sourceUrl.Replace("'", "''");
        string escapedDestination = destinationPath.Replace("'", "''");
        string script = $@"
$ProgressPreference='SilentlyContinue'
if (!(Get-Command -Name Start-BitsTransfer -ErrorAction SilentlyContinue)) {{ exit 41 }}
Start-BitsTransfer -Source '{escapedUrl}' -Destination '{escapedDestination}' -TransferType Download -ErrorAction Stop
";

        string encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
        string args = $"-NoProfile -ExecutionPolicy Bypass -EncodedCommand {encoded}";

        ProcessExecutionResult execution = await _processRunner
            .RunAsync("powershell.exe", args, Path.GetTempPath(), cancellationToken)
            .ConfigureAwait(false);

        if (!execution.IsSuccess)
        {
            _logger.LogWarning("BITS download attempt failed. ExitCode={ExitCode}", execution.ExitCode);
        }

        return execution.IsSuccess && File.Exists(destinationPath);
    }

    private static async Task DownloadWithHttpClientAsync(string sourceUrl, string destinationPath, CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await HttpClient
            .GetAsync(sourceUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);

        response.EnsureSuccessStatusCode();

        await using Stream sourceStream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        await using FileStream destinationStream = new(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true);
        await sourceStream.CopyToAsync(destinationStream, cancellationToken).ConfigureAwait(false);
    }

    private static async Task EnsureHashAsync(string filePath, string? expectedHash, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(expectedHash))
        {
            return;
        }

        string expected = NormalizeHash(expectedHash);
        HashAlgorithmName algorithm = ResolveHashAlgorithm(expected);
        string actual = await ComputeHashAsync(filePath, algorithm, cancellationToken).ConfigureAwait(false);
        if (!string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Hash verification failed for '{filePath}' ({algorithm.Name}). Expected '{expected}', actual '{actual}'.");
        }
    }

    private static async Task<bool> ValidateHashIfRequestedAsync(string filePath, string? expectedHash, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(expectedHash))
        {
            return true;
        }

        string expected = NormalizeHash(expectedHash);
        HashAlgorithmName algorithm = ResolveHashAlgorithm(expected);
        string actual = await ComputeHashAsync(filePath, algorithm, cancellationToken).ConfigureAwait(false);
        return string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase);
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
}
