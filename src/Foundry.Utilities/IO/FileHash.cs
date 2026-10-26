// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Security.Cryptography;

namespace Foundry.Utilities.IO;

/// <summary>
/// Provides file hash operations.
/// </summary>
public static class FileHash
{
    private const int BufferSize = 1024 * 1024;

    /// <summary>
    /// Computes the SHA-256 hash of a file as an uppercase hexadecimal string.
    /// </summary>
    /// <param name="filePath">The file to hash.</param>
    /// <param name="cancellationToken">A token used to cancel the operation.</param>
    /// <returns>The SHA-256 hash of the file.</returns>
    public static async Task<string> ComputeSha256Async(string filePath, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        using var sha256 = SHA256.Create();
        await using FileStream stream = File.OpenRead(filePath);
        byte[] hash = await sha256.ComputeHashAsync(stream, cancellationToken).ConfigureAwait(false);
        return Convert.ToHexString(hash);
    }

    /// <summary>
    /// Computes the SHA-256 hash of a stream from its current position to its end and reports the cumulative number of bytes hashed
    /// after each read.
    /// </summary>
    /// <param name="stream">The stream to hash; it is left at its end and open.</param>
    /// <param name="reportBytesHashed">Receives the bytes hashed so far, or <see langword="null"/>.</param>
    /// <param name="cancellationToken">A token used to cancel the operation between reads.</param>
    /// <returns>The SHA-256 hash.</returns>
    public static async Task<byte[]> ComputeSha256Async(Stream stream, Action<long>? reportBytesHashed, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(stream);
        cancellationToken.ThrowIfCancellationRequested();

        using var sha256 = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        byte[] buffer = new byte[BufferSize];
        long bytesHashed = 0;
        while (true)
        {
            int bytesRead = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken).ConfigureAwait(false);
            if (bytesRead == 0)
            {
                return sha256.GetHashAndReset();
            }

            sha256.AppendData(buffer, 0, bytesRead);
            bytesHashed += bytesRead;
            reportBytesHashed?.Invoke(bytesHashed);
        }
    }
}
