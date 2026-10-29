// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Foundry.Core.Services.Storage;

/// <summary>Publishes verified immutable generations of authoring download originals.</summary>
public sealed class AuthoringArtifactCache(string cacheRoot)
{
    private readonly string root = Path.GetFullPath(cacheRoot);

    /// <summary>Acquires and verifies an entry, retaining its interprocess lease through consumer disposal.</summary>
    public Task<CachedArtifactLease> AcquireAsync(CachedArtifactRequest request, Func<string, CancellationToken, Task> downloadToTemporaryFile,
        CancellationToken cancellationToken = default, IProgress<CachedArtifactVerificationProgress>? verificationProgress = null)
    {
        ArgumentNullException.ThrowIfNull(downloadToTemporaryFile);
        return AcquireAsync(request, async (path, token) =>
        {
            await downloadToTemporaryFile(path, token).ConfigureAwait(false);
            return null;
        }, cancellationToken, verificationProgress);
    }

    /// <summary>
    /// Acquires and verifies an entry, retaining its interprocess lease through consumer disposal. The download returns the
    /// SHA-256 it computed while writing the file, so a new download is not read again to be verified; <see langword="null"/>
    /// makes the cache read it back. A cached generation is always read back.
    /// </summary>
    public async Task<CachedArtifactLease> AcquireAsync(CachedArtifactRequest request, Func<string, CancellationToken, Task<string?>> downloadToTemporaryFile,
        CancellationToken cancellationToken = default, IProgress<CachedArtifactVerificationProgress>? verificationProgress = null)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(downloadToTemporaryFile);
        string? expectedHash = ValidateRequest(request);
        cancellationToken.ThrowIfCancellationRequested();
        string identity = JsonSerializer.Serialize(new { Version = 1, request.Kind, request.SourceIdentity, ExpectedSha256 = expectedHash });
        string key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)));
        string entry = Path.Combine(root, key);
        Directory.CreateDirectory(entry);
        FileStream entryLease = await AcquireEntryLeaseAsync(entry, cancellationToken).ConfigureAwait(false);
        string? pending = null;
        try
        {
            if (expectedHash is not null || request.AllowCompletedTransferReuse)
            {
                foreach (string generation in Directory.EnumerateDirectories(entry, "generation-*"))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    string path = Path.Combine(generation, request.FileName);
                    Receipt? receipt = expectedHash is null ? await ReadReceiptAsync(generation, cancellationToken).ConfigureAwait(false) : null;
                    if (expectedHash is null && (receipt is null || receipt.Identity != identity)) continue;
                    VerifiedFile? verified = await VerifyAsync(path, expectedHash ?? receipt!.Sha256,
                        request.ExpectedLength, receipt?.Length, cancellationToken, verificationProgress).ConfigureAwait(false);
                    if (verified is not null)
                    {
                        return new CachedArtifactLease(path, true, entryLease);
                    }
                }
            }

            string generationId = Guid.NewGuid().ToString("N");
            pending = Path.Combine(entry, ".pending-" + generationId);
            Directory.CreateDirectory(pending);
            string temporaryPath = Path.Combine(pending, request.FileName);
            string? streamedHash = await downloadToTemporaryFile(temporaryPath, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            VerifiedFile? downloaded = streamedHash is null
                ? await VerifyAsync(temporaryPath, expectedHash, request.ExpectedLength, null, cancellationToken, verificationProgress).ConfigureAwait(false)
                : CheckStreamed(temporaryPath, streamedHash, expectedHash, request.ExpectedLength, verificationProgress);
            if (downloaded is null) throw new InvalidDataException("Downloaded artifact failed length or SHA-256 verification.");

            // Flush the validated original before publishing its directory; no old generation is removed.
            using (var stream = new FileStream(temporaryPath, FileMode.Open, FileAccess.Write, FileShare.None)) stream.Flush(flushToDisk: true);
            if (expectedHash is null)
            {
                string receiptPath = Path.Combine(pending, "receipt.json");
                await using var stream = new FileStream(receiptPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                await JsonSerializer.SerializeAsync(stream, new Receipt(identity, downloaded.Length, downloaded.Sha256), cancellationToken: cancellationToken).ConfigureAwait(false);
                stream.Flush(flushToDisk: true);
            }
            cancellationToken.ThrowIfCancellationRequested();
            string committed = Path.Combine(entry, "generation-" + generationId);
            Directory.Move(pending, committed);
            pending = null;
            return new CachedArtifactLease(Path.Combine(committed, request.FileName), false, entryLease);
        }
        catch
        {
            await entryLease.DisposeAsync().ConfigureAwait(false);
            throw;
        }
        finally
        {
            if (pending is not null)
            {
                try { Directory.Delete(pending, recursive: true); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
    }

    /// <summary>
    /// Deletes every entry no process currently leases, and loose files left by older cache layouts, best-effort.
    /// An entry leased by another process keeps its bytes for the lifetime of that lease.
    /// </summary>
    public void PurgeUnleasedEntries()
    {
        if (!Directory.Exists(root)) return;
        foreach (string entry in Directory.EnumerateDirectories(root))
        {
            FileStream lease;
            try { lease = new FileStream(Path.Combine(entry, ".lease"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
            catch (IOException) { continue; }
            catch (UnauthorizedAccessException) { continue; }

            using (lease)
            {
                foreach (string child in Directory.EnumerateFileSystemEntries(entry).Where(path => !path.Equals(lease.Name, StringComparison.OrdinalIgnoreCase)))
                {
                    TryDelete(child);
                }
            }

            TryDelete(entry);
        }

        foreach (string file in Directory.EnumerateFiles(root)) TryDelete(file);
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
            else File.Delete(path);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private static string? ValidateRequest(CachedArtifactRequest request)
    {
        if (!Enum.IsDefined(request.Kind)) throw new ArgumentOutOfRangeException(nameof(request));
        ArgumentException.ThrowIfNullOrWhiteSpace(request.SourceIdentity);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.FileName);
        if (request.FileName != Path.GetFileName(request.FileName) || request.FileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
            || request.FileName is "." or ".." || request.FileName.Equals("receipt.json", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Artifact filename must be a safe leaf filename.", nameof(request));
        if (request.ExpectedLength is <= 0) throw new ArgumentOutOfRangeException(nameof(request), "Expected length must be positive.");
        if (request.ExpectedSha256 is null or "") return null;
        string hash = request.ExpectedSha256.Trim();
        if (hash.Length != 64 || !hash.All(Uri.IsHexDigit)) throw new ArgumentException("Expected SHA-256 must contain exactly 64 hexadecimal digits.", nameof(request));
        return hash.ToUpperInvariant();
    }

    private static async Task<FileStream> AcquireEntryLeaseAsync(string entry, CancellationToken cancellationToken)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try { return new FileStream(Path.Combine(entry, ".lease"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
            catch (IOException exception) when ((exception.HResult & 0xFFFF) is 32 or 33)
            {
                await Task.Delay(50, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private static async Task<Receipt?> ReadReceiptAsync(string generation, CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = File.OpenRead(Path.Combine(generation, "receipt.json"));
            Receipt? receipt = await JsonSerializer.DeserializeAsync<Receipt>(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
            return receipt is { Length: > 0, Sha256.Length: 64 } && receipt.Sha256.All(Uri.IsHexDigit) ? receipt : null;
        }
        catch (FileNotFoundException) { return null; }
        catch (DirectoryNotFoundException) { return null; }
        catch (JsonException) { return null; }
    }

    /// <summary>Checks a new download against the SHA-256 computed while it was written, and its length on disk.</summary>
    private static VerifiedFile? CheckStreamed(string path, string streamedHash, string? expectedHash, long? expectedLength,
        IProgress<CachedArtifactVerificationProgress>? progress)
    {
        var file = new FileInfo(path);
        if (!file.Exists || file.Length == 0 || expectedLength.HasValue && file.Length != expectedLength) return null;
        if (expectedHash is not null && !streamedHash.Equals(expectedHash, StringComparison.OrdinalIgnoreCase)) return null;
        progress?.Report(new(file.Length, file.Length, true));
        return new(file.Length, streamedHash.ToUpperInvariant());
    }

    private static async Task<VerifiedFile?> VerifyAsync(string path, string? expectedHash, long? expectedLength, long? recordedLength,
        CancellationToken cancellationToken, IProgress<CachedArtifactVerificationProgress>? progress)
    {
        cancellationToken.ThrowIfCancellationRequested();
        FileStream stream;
        try { stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 128 * 1024, useAsync: true); }
        catch (FileNotFoundException) { return null; }
        catch (DirectoryNotFoundException) { return null; }
        await using (stream)
        {
            long length = stream.Length;
            if (length == 0 || expectedLength.HasValue && length != expectedLength || recordedLength.HasValue && length != recordedLength) return null;
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            byte[] buffer = new byte[128 * 1024];
            long verified = 0;
            progress?.Report(new(0, length, false));
            DateTimeOffset nextReport = DateTimeOffset.UtcNow;
            while (true)
            {
                int read = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
                if (read == 0) break;
                hash.AppendData(buffer.AsSpan(0, read));
                verified += read;
                if (verified < length && DateTimeOffset.UtcNow >= nextReport)
                {
                    progress?.Report(new(verified, length, false));
                    nextReport = DateTimeOffset.UtcNow.AddMilliseconds(100);
                }
            }
            cancellationToken.ThrowIfCancellationRequested();
            string actual = Convert.ToHexString(hash.GetHashAndReset());
            if (expectedHash is not null && !actual.Equals(expectedHash, StringComparison.OrdinalIgnoreCase)) return null;
            progress?.Report(new(verified, length, true));
            cancellationToken.ThrowIfCancellationRequested();
            return new(length, actual);
        }
    }

    private sealed record Receipt(string Identity, long Length, string Sha256);
    private sealed record VerifiedFile(long Length, string Sha256);
}
