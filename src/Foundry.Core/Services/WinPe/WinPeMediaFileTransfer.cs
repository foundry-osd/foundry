// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Buffers;
using System.Security.Cryptography;
using Foundry.Core.Services.Images;

namespace Foundry.Core.Services.WinPe;

/// <summary>The length and SHA-256 of the bytes a copy read from its source.</summary>
/// <param name="Length">The bytes read.</param>
/// <param name="Hash">The SHA-256 of those bytes, upper-case hexadecimal.</param>
internal sealed record WinPeCopiedFile(long Length, string Hash)
{
    /// <summary>Returns whether the copy read exactly the recorded content.</summary>
    public bool Matches(long length, string hash) => Length == length && Hash.Equals(hash, StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// Copies and hashes media content in chunks, reporting the bytes of each chunk, so a single large file such as a custom
/// image moves the progress while it is copied to or checked on a USB drive or a network share.
/// </summary>
internal static class WinPeMediaFileTransfer
{
    private const int BufferSize = 1024 * 1024;

    /// <summary>File that a media or share publication holds open, exclusively, per kind of content while it publishes.</summary>
    public const string PublicationLockFileName = ".publish.lock";

    /// <summary>
    /// Copies <paramref name="sourcePath"/> to a new file and flushes it to disk; reports the bytes written after each chunk and
    /// returns the length and SHA-256 of the bytes it read from the source.
    /// </summary>
    public static async Task<WinPeCopiedFile> CopyAsync(string sourcePath, string destinationPath, Action<long>? onBytes, CancellationToken cancellationToken)
    {
        byte[] buffer = ArrayPool<byte>.Shared.Rent(BufferSize);
        try
        {
            await using var source = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read, BufferSize, useAsync: true);
            await using var target = new FileStream(destinationPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, BufferSize, useAsync: true);
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            long length = 0;
            int read;
            while ((read = await source.ReadAsync(buffer.AsMemory(0, BufferSize), cancellationToken).ConfigureAwait(false)) > 0)
            {
                hash.AppendData(buffer, 0, read);
                await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                length += read;
                onBytes?.Invoke(read);
            }

            await target.FlushAsync(cancellationToken).ConfigureAwait(false);
            target.Flush(flushToDisk: true);
            return new WinPeCopiedFile(length, Convert.ToHexString(hash.GetHashAndReset()));
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    /// <summary>
    /// Returns whether <paramref name="path"/> exists with <paramref name="length"/>, without reading it, and then reports the
    /// length as processed; a missing file counts later, while it is copied. A network cache share uses it for content already
    /// published at its content-addressed path.
    /// </summary>
    public static bool HasLength(string path, long length, Action<long>? onBytes)
    {
        CustomImagePathPolicy.ValidateNoReparsePoints(path);
        var file = new FileInfo(path);
        bool present = file.Exists && file.Length == length;
        if (present) onBytes?.Invoke(length);
        return present;
    }

    /// <summary>
    /// Sets the write time of an existing file or folder to now, so the 24-hour grace of a network cache clean up treats reused
    /// content as just published; a missing entry is ignored, since its copy is new.
    /// </summary>
    public static void MarkInUse(string path)
    {
        DateTime now = DateTime.UtcNow;
        if (Directory.Exists(path)) Directory.SetLastWriteTimeUtc(path, now);
        else if (File.Exists(path)) File.SetLastWriteTimeUtc(path, now);
    }

    /// <summary>Returns the SHA-256 of the rest of <paramref name="stream"/> as hexadecimal; reports the bytes read after each chunk.</summary>
    public static async Task<string> HashAsync(Stream stream, Action<long>? onBytes, CancellationToken cancellationToken)
    {
        byte[] buffer = ArrayPool<byte>.Shared.Rent(BufferSize);
        try
        {
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            int read;
            while ((read = await stream.ReadAsync(buffer.AsMemory(0, BufferSize), cancellationToken).ConfigureAwait(false)) > 0)
            {
                hash.AppendData(buffer, 0, read);
                onBytes?.Invoke(read);
            }

            return Convert.ToHexString(hash.GetHashAndReset());
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }
}

/// <summary>
/// Turns the chunk counts of one publication into progress reports up to <c>90</c> percent, at most every 100 milliseconds like
/// every other transfer, and at the end of each file. Content already on the destination counts once, when it is checked.
/// On a drive, copied content counts half while it is copied and half while the copy is read back; on a network cache share,
/// which is never read back, it counts once while it is copied. Only file content counts, not manifests.
/// </summary>
internal sealed class WinPeMediaByteProgress(IProgress<WinPeMediaProgress>? progress, long totalBytes, string status)
{
    /// <summary>Creates the progress of the files of a lease.</summary>
    public static WinPeMediaByteProgress ForFiles(IProgress<WinPeMediaProgress>? progress, IEnumerable<long> fileLengths, string status) =>
        new(progress, fileLengths.Aggregate(0L, (total, length) => checked(total + length)), status);

    private const long ReportIntervalMilliseconds = 100;
    private double completed;
    private long lastReported;
    private long nextReport = Environment.TickCount64 + ReportIntervalMilliseconds;

    /// <summary>Gets the callback for a pass that counts each byte fully.</summary>
    public Action<long> Full => bytes => Advance(bytes);

    /// <summary>Gets the callback for one of the two passes over a copied file.</summary>
    public Action<long> Half => bytes => Advance(bytes / 2.0);

    /// <summary>Reports the progress reached at the end of a file, unless it was just reported.</summary>
    public void EndOfFile()
    {
        if ((long)completed > lastReported) Report((long)completed);
    }

    /// <summary>Reports the publication as complete.</summary>
    public void Complete(int percent, string completedStatus) =>
        progress?.Report(new WinPeMediaProgress { Percent = percent, Status = completedStatus, BytesCompleted = totalBytes, TotalBytes = totalBytes });

    private void Advance(double bytes)
    {
        completed = Math.Min(totalBytes, completed + bytes);
        if (Environment.TickCount64 >= nextReport) Report((long)completed);
    }

    private void Report(long current)
    {
        lastReported = current;
        nextReport = Environment.TickCount64 + ReportIntervalMilliseconds;
        progress?.Report(new WinPeMediaProgress
        {
            Percent = (int)(current * 90.0 / Math.Max(1, totalBytes)),
            Status = status,
            BytesCompleted = current,
            TotalBytes = totalBytes
        });
    }
}
