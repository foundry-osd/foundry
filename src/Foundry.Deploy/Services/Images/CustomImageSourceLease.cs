// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.IO;
using System.Security.Cryptography;

namespace Foundry.Deploy.Services.Images;

/// <summary>Protects a regular source file against writes and replacement before hashing and image inspection.</summary>
public sealed class CustomImageSourceLease : IDisposable
{
    private readonly FileStream _stream;

    private CustomImageSourceLease(FileStream stream, string hash)
    {
        _stream = stream;
        ContentHash = hash;
    }

    public string ContentHash { get; }
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

    /// <summary>Rejects redirection in the file and every parent directory.</summary>
    public static void EnsureRegularPath(string path)
    {
        string fullPath = global::System.IO.Path.GetFullPath(path);
        if ((File.GetAttributes(fullPath) & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != 0)
            throw new InvalidDataException("CustomImages.InvalidSource");
        for (DirectoryInfo? directory = Directory.GetParent(fullPath); directory is not null; directory = directory.Parent)
            if ((directory.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("CustomImages.InvalidSource");
    }

    /// <summary>Rechecks readability immediately before destructive work while preserving the protected handle.</summary>
    public bool IsReadable()
    {
        try { _stream.Position = 0; return _stream.ReadByte() >= 0; }
        catch (Exception exception) when (exception is IOException or ObjectDisposedException) { return false; }
    }

    public void Dispose() => _stream.Dispose();
}
