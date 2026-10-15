// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Utilities.IO;
using Foundry.Utilities.Progress;

namespace Foundry.Core.Services.WinPe;

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
    /// Copies <paramref name="source"/> to <paramref name="destination"/>, reporting each whole-percent change, or every
    /// chunk when the size is unknown, and a final complete report.
    /// </summary>
    /// <returns>The number of bytes written.</returns>
    internal static async Task<long> CopyAsync(
        Stream source,
        FileStream destination,
        long? totalBytes,
        WinPeDownloadProgress item,
        IProgress<WinPeDownloadProgress>? progress,
        Action reportProgress,
        CancellationToken cancellationToken)
    {
        int lastReportedPercent = -1;
        long bytesWritten = await StreamCopy.CopyAsync(
            source,
            destination,
            copiedBytes =>
            {
                reportProgress();
                double? percentage = TransferProgress.CalculatePercentage(copiedBytes, totalBytes);
                if (percentage.HasValue)
                {
                    int downloadPercent = (int)percentage.Value;
                    if (downloadPercent == lastReportedPercent)
                    {
                        return;
                    }

                    lastReportedPercent = downloadPercent;
                }

                Report(progress, item, percentage.HasValue ? (int)percentage.Value : null, copiedBytes, totalBytes);
            },
            cancellationToken).ConfigureAwait(false);

        Report(progress, item, 100, bytesWritten, totalBytes is > 0 ? totalBytes : bytesWritten);
        return bytesWritten;
    }
}
