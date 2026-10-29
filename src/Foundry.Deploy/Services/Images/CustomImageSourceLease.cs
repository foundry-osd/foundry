// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.IO;
using System.Security.Cryptography;
using Foundry.Core.Services.Images;
using Foundry.Deploy.Services.Download;

namespace Foundry.Deploy.Services.Images;

/// <summary>Protects a regular source file against writes and replacement before hashing and image inspection.</summary>
public sealed class CustomImageSourceLease : IDisposable
{
    private const int CopyBufferSize = 1024 * 1024;
    // Same cadence as every other transfer: often enough to look live, rarely enough not to flood the UI.
    private const long ProgressReportIntervalMilliseconds = 100;
    private static readonly TimeSpan DefaultCopyStallTimeout = TimeSpan.FromSeconds(30);
    private readonly FileStream _stream;

    private CustomImageSourceLease(FileStream stream, string? hash)
    {
        _stream = stream;
        ContentHash = hash;
    }

    /// <summary>Gets the SHA-256 digest read when the lease was acquired, or <see langword="null"/> when the content was not hashed.</summary>
    public string? ContentHash { get; }
    public long Length => _stream.Length;

    /// <summary>Opens with read-only sharing before checking bytes; failures release the handle.</summary>
    public static async Task<CustomImageSourceLease> AcquireAsync(string path, long? expectedLength,
        string? expectedHash, CancellationToken cancellationToken)
    {
        EnsureRegularPath(path);
        var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        try
        {
            EnsureRegularPath(path);
            if (stream.Length <= 0 || expectedLength is not null && stream.Length != expectedLength)
                throw new InvalidDataException("CustomImages.InvalidSource");
            string hash = Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false));
            if (expectedHash is not null && !string.Equals(hash, expectedHash, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("The source file SHA-256 digest does not match the recorded digest.");
            stream.Position = 0;
            return new CustomImageSourceLease(stream, hash);
        }
        catch { stream.Dispose(); throw; }
    }

    /// <summary>
    /// Opens with the same read-only sharing and redirection checks as <see cref="AcquireAsync"/> and checks only the length,
    /// without reading the content. Used for a network cache share image, whose digest is verified while it is copied to the
    /// target, so the share is read once.
    /// </summary>
    public static CustomImageSourceLease OpenWithoutHashing(string path, long expectedLength)
    {
        EnsureRegularPath(path);
        var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, CopyBufferSize,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        try
        {
            EnsureRegularPath(path);
            if (stream.Length <= 0 || stream.Length != expectedLength)
                throw new InvalidDataException("CustomImages.InvalidSource");
            return new CustomImageSourceLease(stream, null);
        }
        catch { stream.Dispose(); throw; }
    }

    /// <summary>
    /// Copies the protected content to <paramref name="destinationPath"/> through the leased handle, computing SHA-256 in the same
    /// pass. A length or digest mismatch throws <see cref="InvalidDataException"/>; a source that delivers no data for the stall
    /// timeout throws <see cref="TimeoutException"/>. Any failure, cancellation included, deletes the partial destination.
    /// </summary>
    public async Task CopyVerifiedToAsync(string destinationPath, string expectedHash, IProgress<DownloadProgress>? progress,
        CancellationToken cancellationToken, TimeSpan? stallTimeout = null)
    {
        TimeSpan timeout = stallTimeout ?? DefaultCopyStallTimeout;
        long expectedLength = _stream.Length;
        // The watchdog restarts on every chunk, so only a source that stops answering is abandoned.
        using var stall = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        stall.CancelAfter(timeout);
        try
        {
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            await using (var destination = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None, CopyBufferSize, useAsync: true))
            {
                _stream.Position = 0;
                byte[] buffer = new byte[CopyBufferSize];
                long copied = 0;
                long nextReport = Environment.TickCount64 + ProgressReportIntervalMilliseconds;
                progress?.Report(new(0, expectedLength));
                int read;
                while ((read = await _stream.ReadAsync(buffer, stall.Token).ConfigureAwait(false)) != 0)
                {
                    copied = checked(copied + read);
                    if (copied > expectedLength) throw new InvalidDataException("The source file is longer than its recorded length.");
                    hash.AppendData(buffer, 0, read);
                    await destination.WriteAsync(buffer.AsMemory(0, read), stall.Token).ConfigureAwait(false);
                    stall.CancelAfter(timeout);
                    if (Environment.TickCount64 >= nextReport)
                    {
                        progress?.Report(new(copied, expectedLength));
                        nextReport = Environment.TickCount64 + ProgressReportIntervalMilliseconds;
                    }
                }
                if (copied != expectedLength) throw new InvalidDataException("The source file is shorter than its recorded length.");
                progress?.Report(new(copied, expectedLength));
            }
            if (!string.Equals(Convert.ToHexString(hash.GetHashAndReset()), expectedHash, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("The source file SHA-256 digest does not match the recorded digest.");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            TryDelete(destinationPath);
            throw new TimeoutException("The custom image source stopped responding during the copy.");
        }
        catch
        {
            TryDelete(destinationPath);
            throw;
        }
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
    }

    /// <summary>Rejects redirection in the file and every parent directory.</summary>
    public static void EnsureRegularPath(string path) => CustomImageMediaManifestReader.EnsureRegularPath(path);

    /// <summary>Rechecks readability immediately before destructive work while preserving the protected handle.</summary>
    public bool IsReadable()
    {
        try { _stream.Position = 0; return _stream.ReadByte() >= 0; }
        catch (Exception exception) when (exception is IOException or ObjectDisposedException) { return false; }
    }

    public void Dispose() => _stream.Dispose();
}
