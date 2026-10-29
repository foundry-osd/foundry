// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Security.Cryptography;
using Foundry.Utilities.IO;
using Foundry.Utilities.Progress;

namespace Foundry.Core.Services.WinPe;

/// <summary>The length and SHA-256 of a downloaded file, computed while it was written.</summary>
/// <param name="Length">The bytes written.</param>
/// <param name="Sha256">The SHA-256 of those bytes, upper-case hexadecimal.</param>
internal sealed record WinPeDownloadedFile(long Length, string Sha256);

/// <summary>
/// Copies a download stream to its destination while reporting byte counts for one described item.
/// </summary>
internal static class WinPeDownloadReporting
{
    /// <summary>
    /// Reports <paramref name="item"/> with a percentage and optional byte counts.
    /// </summary>
    internal static void Report(
        IProgress<WinPeDownloadProgress>? progress,
        WinPeDownloadProgress item,
        int? percent,
        long? bytesTransferred = null,
        long? totalBytes = null)
    {
        progress?.Report(item with
        {
            Percent = percent.HasValue ? Math.Clamp(percent.Value, 0, 100) : null,
            BytesTransferred = bytesTransferred,
            TotalBytes = totalBytes
        });
    }

    /// <summary>
    /// Copies <paramref name="source"/> to <paramref name="destination"/> and computes the SHA-256 of the bytes read in the same
    /// pass, so the download is never read again to be verified. Progress is reported at most every 100 milliseconds, like every
    /// other transfer, and once complete.
    /// </summary>
    /// <returns>The bytes written and their SHA-256.</returns>
    internal static async Task<WinPeDownloadedFile> CopyAsync(
        Stream source,
        FileStream destination,
        long? totalBytes,
        WinPeDownloadProgress item,
        IProgress<WinPeDownloadProgress>? progress,
        Action reportProgress,
        CancellationToken cancellationToken)
    {
        long nextReport = 0;
        using var sha256 = SHA256.Create();
        long bytesWritten;
        await using (var hashing = new CryptoStream(source, sha256, CryptoStreamMode.Read, leaveOpen: true))
        {
            bytesWritten = await StreamCopy.CopyAsync(
                hashing,
                destination,
                copiedBytes =>
                {
                    reportProgress();
                    if (Environment.TickCount64 < nextReport) return;
                    nextReport = Environment.TickCount64 + ProgressReportIntervalMilliseconds;
                    double? percentage = TransferProgress.CalculatePercentage(copiedBytes, totalBytes);
                    Report(progress, item, percentage.HasValue ? (int)percentage.Value : null, copiedBytes, totalBytes);
                },
                cancellationToken).ConfigureAwait(false);
        }

        Report(progress, item, 100, bytesWritten, totalBytes is > 0 ? totalBytes : bytesWritten);
        return new WinPeDownloadedFile(bytesWritten, Convert.ToHexString(sha256.Hash!));
    }

    private const long ProgressReportIntervalMilliseconds = 100;
}
