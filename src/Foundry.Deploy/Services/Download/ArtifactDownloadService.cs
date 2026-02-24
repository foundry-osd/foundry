using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.IO;
using Foundry.Deploy.Services.System;

namespace Foundry.Deploy.Services.Download;

public sealed class ArtifactDownloadService : IArtifactDownloadService
{
    private static readonly HttpClient HttpClient = new()
    {
        Timeout = TimeSpan.FromMinutes(30)
    };

    private readonly IProcessRunner _processRunner;

    public ArtifactDownloadService(IProcessRunner processRunner)
    {
        _processRunner = processRunner;
    }

    public async Task<ArtifactDownloadResult> DownloadAsync(
        string sourceUrl,
        string destinationPath,
        string? expectedSha256 = null,
        bool preferBits = true,
        CancellationToken cancellationToken = default)
    {
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

        if (File.Exists(destinationPath) && await ValidateHashIfRequestedAsync(destinationPath, expectedSha256, cancellationToken).ConfigureAwait(false))
        {
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
            await EnsureHashAsync(destinationPath, expectedSha256, cancellationToken).ConfigureAwait(false);
            return new ArtifactDownloadResult
            {
                DestinationPath = destinationPath,
                Downloaded = true,
                Method = "bits",
                SizeBytes = new FileInfo(destinationPath).Length
            };
        }

        await DownloadWithHttpClientAsync(sourceUrl, destinationPath, cancellationToken).ConfigureAwait(false);
        await EnsureHashAsync(destinationPath, expectedSha256, cancellationToken).ConfigureAwait(false);

        return new ArtifactDownloadResult
        {
            DestinationPath = destinationPath,
            Downloaded = true,
            Method = "httpclient",
            SizeBytes = new FileInfo(destinationPath).Length
        };
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

    private static async Task EnsureHashAsync(string filePath, string? expectedSha256, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(expectedSha256))
        {
            return;
        }

        string expected = NormalizeHash(expectedSha256);
        string actual = await ComputeSha256Async(filePath, cancellationToken).ConfigureAwait(false);
        if (!string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Hash verification failed for '{filePath}'. Expected '{expected}', actual '{actual}'.");
        }
    }

    private static async Task<bool> ValidateHashIfRequestedAsync(string filePath, string? expectedSha256, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(expectedSha256))
        {
            return true;
        }

        string expected = NormalizeHash(expectedSha256);
        string actual = await ComputeSha256Async(filePath, cancellationToken).ConfigureAwait(false);
        return string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeHash(string hash)
    {
        return hash
            .Trim()
            .Replace("-", string.Empty, StringComparison.OrdinalIgnoreCase)
            .ToUpperInvariant();
    }

    private static async Task<string> ComputeSha256Async(string filePath, CancellationToken cancellationToken)
    {
        using var sha256 = SHA256.Create();
        await using FileStream stream = new(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);
        byte[] hash = await sha256.ComputeHashAsync(stream, cancellationToken).ConfigureAwait(false);
        return Convert.ToHexString(hash);
    }
}
