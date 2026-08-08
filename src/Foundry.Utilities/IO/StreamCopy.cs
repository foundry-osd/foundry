// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Utilities.IO;

/// <summary>
/// Copies streams while reporting the cumulative number of bytes written.
/// </summary>
public static class StreamCopy
{
    private const int BufferSize = 80 * 1024;

    /// <summary>
    /// Copies a source stream to a destination stream and reports after each successful write.
    /// </summary>
    public static async Task<long> CopyAsync(
        Stream source,
        Stream destination,
        Action<long>? reportBytesCopied,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(destination);
        cancellationToken.ThrowIfCancellationRequested();

        byte[] buffer = new byte[BufferSize];
        long bytesCopied = 0;

        while (true)
        {
            int bytesRead = await source
                .ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken)
                .ConfigureAwait(false);
            if (bytesRead == 0)
            {
                return bytesCopied;
            }

            await destination
                .WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken)
                .ConfigureAwait(false);
            bytesCopied += bytesRead;
            reportBytesCopied?.Invoke(bytesCopied);
        }
    }
}
