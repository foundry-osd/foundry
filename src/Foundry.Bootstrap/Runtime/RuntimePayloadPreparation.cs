// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Security.Cryptography;
using Serilog;

namespace Foundry.Bootstrap.Runtime;

/// <summary>Copies and authenticates complete archives before exposing executable bytes in trusted WinPE storage.</summary>
internal sealed class RuntimePayloadPreparation(string winPeRoot, RuntimeTransfer transfer, ILogger logger,
    Action<RuntimeDownloadProgress>? progress, Action<string>? activity, bool retainPayload)
{
    /// <summary>Retains successful payloads until WinPE exits; failed preparations remove only their own workspace.</summary>
    internal async Task<string> PrepareAsync(string source, string expectedHash, string applicationName,
        CancellationToken cancellationToken, Func<string, Task>? cacheArchive = null)
    {
        expectedHash = RequireHash(expectedHash);
        cancellationToken.ThrowIfCancellationRequested();
        string workspace = Path.Combine(winPeRoot, "Execution", Guid.NewGuid().ToString("N"));
        string archive = Path.Combine(workspace, "archive.zip");
        string payload = Path.Combine(workspace, "Payload");
        Directory.CreateDirectory(workspace);
        bool ready = false;
        try
        {
            await transfer.SaveAsync(source, archive, applicationName, cancellationToken).ConfigureAwait(false);
            await VerifyAsync(archive, expectedHash, applicationName, cancellationToken).ConfigureAwait(false);
            activity?.Invoke($"Extracting files for {applicationName}...");
            await RuntimeArchive.ExtractAsync(archive, payload, cancellationToken,
                (bytes, total) => progress?.Invoke(new(applicationName, bytes, total, RuntimeProgressPhase.Extraction))).ConfigureAwait(false);
            string executable = Path.Combine(payload, applicationName + ".exe");
            if (!File.Exists(executable)) throw new InvalidDataException($"Authenticated archive does not contain '{applicationName}.exe'.");
            cancellationToken.ThrowIfCancellationRequested();
            if (cacheArchive is not null) await cacheArchive(archive).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            File.Delete(archive);
            logger.Information("Authenticated runtime payload prepared for {PayloadApplication} in boot-owned storage", applicationName);
            ready = true;
            return executable;
        }
        finally
        {
            if (!ready || !retainPayload)
            {
                try { Directory.Delete(workspace, recursive: true); }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    logger.Warning(exception, "Could not remove unused runtime preparation for {PayloadApplication}", applicationName);
                }
            }
        }
    }

    /// <summary>Requires an authenticated, supported digest rather than treating absent metadata as successful validation.</summary>
    internal static string RequireHash(string? value)
    {
        string hash = value?.Trim() ?? "";
        if (hash.Length != 64 || hash.Any(character => !Uri.IsHexDigit(character)))
            throw new InvalidDataException("A valid trusted archive SHA256 digest is required.");
        return hash;
    }

    private async Task VerifyAsync(string archive, string expected, string applicationName, CancellationToken cancellationToken)
    {
        activity?.Invoke($"Verifying archive for {applicationName}...");
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        await using var input = new FileStream(archive, FileMode.Open, FileAccess.Read, FileShare.Read,
            81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
        byte[] buffer = new byte[81920];
        long received = 0;
        progress?.Invoke(new(applicationName, 0, input.Length, RuntimeProgressPhase.Verification));
        int read;
        while ((read = await input.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
        {
            hash.AppendData(buffer, 0, read);
            received += read;
            progress?.Invoke(new(applicationName, received, input.Length, RuntimeProgressPhase.Verification));
        }
        if (!string.Equals(Convert.ToHexString(hash.GetHashAndReset()), expected, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Runtime archive SHA256 does not match the trusted digest.");
        logger.Debug("Runtime archive SHA256 authentication passed for {PayloadApplication}", applicationName);
    }
}
