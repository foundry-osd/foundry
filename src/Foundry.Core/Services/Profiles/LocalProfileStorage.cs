// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Text.Json;
using System.Text.Json.Serialization;

namespace Foundry.Core.Services.Profiles;

/// <summary>Bounded metadata and durable file publication for a dedicated local profile directory.</summary>
internal static class LocalProfileStorage
{
    internal static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        MaxDepth = 16
    };

    internal static T ReadJson<T>(string path)
    {
        byte[] bytes = ReadBytes(path, 64 * 1024);
        try
        {
            using JsonDocument document = JsonDocument.Parse(bytes, new() { MaxDepth = 16 });
            ValidateProperties(document.RootElement);
            if (document.RootElement.GetProperty("version").GetInt32() != 1)
            {
                throw new InvalidDataException("The local profile metadata version is unsupported.");
            }
            return JsonSerializer.Deserialize<T>(bytes, JsonOptions)
                ?? throw new InvalidDataException("The local profile metadata is missing.");
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or KeyNotFoundException or FormatException)
        {
            throw new InvalidDataException("The local profile metadata is invalid.", exception);
        }
    }

    internal static byte[] ReadBytes(string path, int maximumLength)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length is <= 0 || stream.Length > maximumLength)
        {
            throw new InvalidDataException("The local profile file size is invalid.");
        }
        byte[] bytes = new byte[(int)stream.Length];
        stream.ReadExactly(bytes);
        return bytes;
    }

    internal static void WriteNew(string path, ReadOnlySpan<byte> bytes)
    {
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        stream.Write(bytes);
        stream.Flush(flushToDisk: true);
    }

    internal static void PublishJson<T>(string path, T value)
    {
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(value, JsonOptions);
        string temporary = path + ".pending";
        try
        {
            File.Delete(temporary);
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                stream.Write(bytes);
                stream.Flush(flushToDisk: true);
            }
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            File.Delete(temporary);
        }
    }

    private static void ValidateProperties(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (JsonProperty property in element.EnumerateObject())
            {
                if (!names.Add(property.Name))
                {
                    throw new InvalidDataException("Duplicate local profile metadata properties are not allowed.");
                }
                ValidateProperties(property.Value);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement item in element.EnumerateArray())
            {
                ValidateProperties(item);
            }
        }
    }
}

/// <summary>Contains only local operation identities needed to retire a committed revision or roll back an unpublished candidate.</summary>
internal sealed record LocalProfileJournal
{
    public int Version { get; init; } = 1;
    public Guid LocalId { get; init; }
    public bool IsDelete { get; init; }
    public Guid? PreviousRevision { get; init; }
    public Guid? NextRevision { get; init; }
    public Guid? PreviousSharedKeyRevision { get; init; }
    public Guid? NextSharedKeyRevision { get; init; }
}

internal sealed record LocalProfileActivePointer
{
    public int Version { get; init; } = 1;
    public Guid? LocalId { get; init; }
}
