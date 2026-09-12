// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Foundry.Core.Services.Profiles;

/// <summary>Bounds and authenticates repository records. The shared directory must enforce trusted-writer ACLs.</summary>
internal sealed class SharedProfileRepositoryFiles(byte[] key)
{
    private const int MaximumMetadataBytes = 64 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        RespectRequiredConstructorParameters = true,
        MaxDepth = 16
    };

    internal FileStream AcquireLock(string path)
    {
        ValidatePath(path);
        try
        {
            // This file remains in place permanently. Ownership is the OS handle, never file existence.
            return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        }
        catch (IOException exception) when ((exception.HResult & 0xffff) is 32 or 33)
        {
            throw new SharedProfileRepositoryException(SharedProfileRepositoryStatus.Busy);
        }
    }

    /// <summary>The stable journal handle is both the exclusive lock and the only route for committing a head.</summary>
    internal FileStream OpenJournal(string path, bool create = false)
    {
        ValidatePath(path);
        try
        {
            return new FileStream(path, create ? FileMode.CreateNew : FileMode.Open,
                FileAccess.ReadWrite, FileShare.None, bufferSize: 1, FileOptions.WriteThrough);
        }
        catch (IOException exception) when ((exception.HResult & 0xffff) is 32 or 33)
        {
            throw new SharedProfileRepositoryException(SharedProfileRepositoryStatus.Busy);
        }
    }

    /// <summary>Only an incomplete final frame is recoverable; complete malformed or unauthenticated records fail closed.</summary>
    internal SharedProfileJournal<T> ReadJournal<T>(FileStream journal, int maximumRecords, CancellationToken token)
    {
        if (journal.Length > (long)maximumRecords * (MaximumMetadataBytes + 8))
            throw new SharedProfileRepositoryException(SharedProfileRepositoryStatus.HistoryLimitExceeded);
        journal.Position = 0;
        var records = new List<T>();
        long committedLength = 0;
        Span<byte> framing = stackalloc byte[4];
        while (journal.Position < journal.Length)
        {
            token.ThrowIfCancellationRequested();
            if (journal.Length - journal.Position < framing.Length) break;
            journal.ReadExactly(framing);
            int length = BinaryPrimitives.ReadInt32LittleEndian(framing);
            if (length is <= 0 or > MaximumMetadataBytes)
                throw new SharedProfileRepositoryException(SharedProfileRepositoryStatus.InvalidData);
            if (journal.Length - journal.Position < length + framing.Length) break;
            if (records.Count >= maximumRecords)
                throw new SharedProfileRepositoryException(SharedProfileRepositoryStatus.HistoryLimitExceeded);
            byte[] content = new byte[length];
            journal.ReadExactly(content);
            journal.ReadExactly(framing);
            if (BinaryPrimitives.ReadInt32LittleEndian(framing) != length)
                throw new SharedProfileRepositoryException(SharedProfileRepositoryStatus.InvalidData);
            records.Add(DeserializeSigned<T>(content, "head"));
            committedLength = journal.Position;
        }
        if (records.Count == 0) throw new SharedProfileRepositoryException(SharedProfileRepositoryStatus.InvalidData);
        return new(records, committedLength);
    }

    /// <summary>Writes and flushes through the already locked handle, including after a durable SMB reconnect.</summary>
    internal void AppendJournal<T>(FileStream journal, long committedLength, T value)
    {
        byte[] bytes = SerializeSigned("head", value);
        byte[] frame = new byte[bytes.Length + 8];
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(0, 4), bytes.Length);
        bytes.CopyTo(frame, 4);
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(bytes.Length + 4, 4), bytes.Length);
        // Truncate only the incomplete tail previously identified while holding this same exclusive handle.
        if (journal.Length != committedLength) journal.SetLength(committedLength);
        journal.Position = committedLength;
        journal.Write(frame);
        journal.Flush(flushToDisk: true);
    }

    internal bool Exists(string path)
    {
        ValidatePath(path);
        try { return (File.GetAttributes(path) & FileAttributes.Directory) == 0; }
        catch (FileNotFoundException) { return false; }
        catch (DirectoryNotFoundException) { return false; }
    }

    internal T ReadSigned<T>(string path, string purpose)
    {
        byte[] envelopeBytes = ReadBytes(path, MaximumMetadataBytes);
        return DeserializeSigned<T>(envelopeBytes, purpose);
    }

    private T DeserializeSigned<T>(byte[] envelopeBytes, string purpose)
    {
        ValidateJson(envelopeBytes);
        SignedRecord envelope = JsonSerializer.Deserialize<SignedRecord>(envelopeBytes, JsonOptions)
            ?? throw new SharedProfileRepositoryException(SharedProfileRepositoryStatus.InvalidData);
        if (envelope.Content is null || envelope.AuthenticationTag is not { Length: 32 })
            throw new SharedProfileRepositoryException(SharedProfileRepositoryStatus.InvalidData);
        byte[] expectedTag = Authenticate(purpose, envelope.Content);
        if (!CryptographicOperations.FixedTimeEquals(envelope.AuthenticationTag, expectedTag))
            throw new CryptographicException("Repository record authentication failed.");
        ValidateJson(envelope.Content);
        return JsonSerializer.Deserialize<T>(envelope.Content, JsonOptions)
            ?? throw new SharedProfileRepositoryException(SharedProfileRepositoryStatus.InvalidData);
    }

    internal void WriteSigned<T>(string path, string purpose, T value)
    {
        WriteBytes(path, SerializeSigned(purpose, value));
    }

    private byte[] SerializeSigned<T>(string purpose, T value)
    {
        byte[] content = JsonSerializer.SerializeToUtf8Bytes(value, JsonOptions);
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(new SignedRecord(content, Authenticate(purpose, content)), JsonOptions);
        if (bytes.Length > MaximumMetadataBytes) throw new SharedProfileRepositoryException(SharedProfileRepositoryStatus.InvalidData);
        return bytes;
    }

    internal byte[] ReadBytes(string path, int maximumBytes)
    {
        ValidatePath(path);
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
        if (stream.Length > maximumBytes) throw new SharedProfileRepositoryException(SharedProfileRepositoryStatus.InvalidData);
        int length = checked((int)stream.Length);
        byte[] bytes = new byte[length];
        stream.ReadExactly(bytes);
        if (stream.ReadByte() != -1) throw new SharedProfileRepositoryException(SharedProfileRepositoryStatus.InvalidData);
        return bytes;
    }

    internal void WriteBytes(string path, byte[] bytes)
    {
        ValidatePath(path);
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                stream.Write(bytes);
                stream.Flush(flushToDisk: true);
            }
            ValidatePath(path);
            File.Move(temporary, path);
        }
        finally
        {
            try { File.Delete(temporary); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    internal static void ValidatePath(string path)
    {
        string? current = Path.GetFullPath(path);
        while (current is not null)
        {
            try
            {
                if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                    throw new SharedProfileRepositoryException(SharedProfileRepositoryStatus.InvalidData);
            }
            catch (FileNotFoundException) { }
            catch (DirectoryNotFoundException) { }
            current = Path.GetDirectoryName(current);
        }
    }

    private byte[] Authenticate(string purpose, byte[] content)
    {
        byte[] prefix = Encoding.UTF8.GetBytes("Foundry.SharedProfiles.v1/" + purpose + "\0");
        using IncrementalHash hmac = IncrementalHash.CreateHMAC(HashAlgorithmName.SHA256, key);
        hmac.AppendData(prefix);
        hmac.AppendData(content);
        return hmac.GetHashAndReset();
    }

    private static void ValidateJson(byte[] bytes)
    {
        using JsonDocument json = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 16 });
        ValidateElement(json.RootElement);
    }

    private static void ValidateElement(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (JsonProperty property in element.EnumerateObject())
            {
                if (!names.Add(property.Name)) throw new SharedProfileRepositoryException(SharedProfileRepositoryStatus.InvalidData);
                ValidateElement(property.Value);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement item in element.EnumerateArray()) ValidateElement(item);
        }
    }

    private sealed record SignedRecord(byte[] Content, byte[] AuthenticationTag);
}

/// <summary>Internal classified failure; only its status crosses the repository boundary.</summary>
internal sealed class SharedProfileRepositoryException(SharedProfileRepositoryStatus status) : Exception
{
    internal SharedProfileRepositoryStatus Status { get; } = status;
}

/// <summary>Authenticated frames and the end of the last complete frame, observed under an exclusive journal handle.</summary>
internal sealed record SharedProfileJournal<T>(IReadOnlyList<T> Records, long CommittedLength);
