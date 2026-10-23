// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Security.Cryptography;
using System.Text;

namespace Foundry.Core.Services.NetworkCache;

/// <summary>Result of one publication attempt.</summary>
public enum NetworkCachePublishOutcome
{
    Published,
    AlreadyPresent,
    Failed
}

/// <summary>
/// Publishes files to a network cache share without locks. A writer copies to a session-unique partial name and
/// renames it to the final name; the rename never overwrites, so the first writer wins and readers, which only
/// open final names, never see a partial file.
/// </summary>
public static class NetworkCacheWriter
{
    private static readonly TimeSpan OrphanAge = TimeSpan.FromHours(24);

    /// <summary>
    /// Copies <paramref name="sourcePath"/> to <paramref name="targetDirectory"/> as <paramref name="fileName"/>, hashing
    /// the bytes as they are copied. With <paramref name="expectedHash"/> (SHA-1 or SHA-256 hex), the copy is published
    /// only when it matches; without it, the copy must match <paramref name="expectedSize"/> and its SHA-256 is written
    /// as a sidecar before the rename, so a reader never finds a hashless final file without its sidecar. A mismatch or
    /// IO failure returns <see cref="NetworkCachePublishOutcome.Failed"/>; cancellation deletes the partial and is rethrown.
    /// Share existence checks and directory creation run synchronously before the first await, so callers on a UI or
    /// step thread must invoke it through the thread pool.
    /// </summary>
    public static Task<NetworkCachePublishOutcome> PublishFileAsync(
        string sourcePath,
        string targetDirectory,
        string fileName,
        string sessionId,
        string? expectedHash,
        long? expectedSize,
        TimeProvider clock,
        CancellationToken cancellationToken) =>
        PublishFileAsync(sourcePath, targetDirectory, fileName, sessionId, expectedHash, expectedSize, clock, cancellationToken, beforeRename: null);

    internal static async Task<NetworkCachePublishOutcome> PublishFileAsync(
        string sourcePath,
        string targetDirectory,
        string fileName,
        string sessionId,
        string? expectedHash,
        long? expectedSize,
        TimeProvider clock,
        CancellationToken cancellationToken,
        Action? beforeRename)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        cancellationToken.ThrowIfCancellationRequested();
        string finalPath = Path.Combine(targetDirectory, fileName);
        if (File.Exists(finalPath)) return NetworkCachePublishOutcome.AlreadyPresent;

        string? normalizedHash = string.IsNullOrWhiteSpace(expectedHash) ? null : expectedHash.Trim().Replace("-", string.Empty, StringComparison.Ordinal);
        HashAlgorithmName algorithm = normalizedHash?.Length == 40 ? HashAlgorithmName.SHA1 : HashAlgorithmName.SHA256;
        string partialPath = Path.Combine(targetDirectory, NetworkCacheLayout.PartialName(fileName, sessionId));
        try
        {
            Directory.CreateDirectory(targetDirectory);
            DeleteOrphanPartials(targetDirectory, clock);
            string actualHash;
            long length;
            // Readers and writers of the local file are tolerated: the hash computed here is what gets checked and published.
            await using (FileStream input = new(sourcePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1 << 20, useAsync: true))
            await using (FileStream output = new(partialPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1 << 20, useAsync: true))
            {
                (actualHash, length) = await CopyAndHashAsync(input, output, algorithm, cancellationToken).ConfigureAwait(false);
                await output.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            bool matches = normalizedHash is not null
                ? string.Equals(normalizedHash, actualHash, StringComparison.OrdinalIgnoreCase)
                : expectedSize is > 0 && length == expectedSize.Value;
            if (!matches)
            {
                TryDelete(partialPath);
                return NetworkCachePublishOutcome.Failed;
            }

            if (normalizedHash is null && !File.Exists(finalPath + NetworkCacheLayout.HashSidecarSuffix))
            {
                // Never replace another writer's sidecar: its final file may already be published under it.
                await using FileStream sidecar = new(finalPath + NetworkCacheLayout.HashSidecarSuffix, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                await sidecar.WriteAsync(Encoding.ASCII.GetBytes(actualHash.ToLowerInvariant()), cancellationToken).ConfigureAwait(false);
            }

            beforeRename?.Invoke();
            if (File.Exists(finalPath))
            {
                TryDelete(partialPath);
                return NetworkCachePublishOutcome.AlreadyPresent;
            }

            File.Move(partialPath, finalPath, overwrite: false);
            return NetworkCachePublishOutcome.Published;
        }
        catch (OperationCanceledException)
        {
            TryDelete(partialPath);
            throw;
        }
        catch (IOException) when (File.Exists(finalPath))
        {
            TryDelete(partialPath);
            return NetworkCachePublishOutcome.AlreadyPresent;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            TryDelete(partialPath);
            return NetworkCachePublishOutcome.Failed;
        }
    }

    private static async Task<(string Hash, long Length)> CopyAndHashAsync(Stream input, Stream output, HashAlgorithmName algorithm, CancellationToken cancellationToken)
    {
        using IncrementalHash hash = IncrementalHash.CreateHash(algorithm);
        byte[] buffer = new byte[1 << 20];
        long length = 0;
        int read;
        while ((read = await input.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
        {
            hash.AppendData(buffer, 0, read);
            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
            length += read;
        }

        return (Convert.ToHexString(hash.GetHashAndReset()), length);
    }

    /// <summary>Deletes partial files older than 24 hours left by writers that stopped mid-copy.</summary>
    public static void DeleteOrphanPartials(string targetDirectory, TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        if (!Directory.Exists(targetDirectory)) return;
        DateTime threshold = clock.GetUtcNow().UtcDateTime - OrphanAge;
        foreach (string partial in Directory.EnumerateFiles(targetDirectory, "*" + NetworkCacheLayout.PartialSuffix))
        {
            if (File.GetLastWriteTimeUtc(partial) < threshold) TryDelete(partial);
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }
}
