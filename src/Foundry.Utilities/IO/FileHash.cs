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
}
