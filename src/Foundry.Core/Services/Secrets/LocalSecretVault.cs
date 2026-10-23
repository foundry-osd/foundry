// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Foundry.Core.Services.Secrets;

/// <summary>Describes the outcome of <see cref="LocalSecretVault.Load"/>.</summary>
public enum VaultStatus
{
    /// <summary>The vault was read.</summary>
    Ok,

    /// <summary>No vault file exists yet.</summary>
    Empty,

    /// <summary>The vault file exists but cannot be decrypted or parsed; it is left in place until the next save.</summary>
    Unreadable,
}

/// <summary>Holds secrets by logical id (see <see cref="SecretIds"/>) in one file protected with DPAPI for the current user.</summary>
/// <remarks>
/// The file content is <c>{ "version": 1, "entries": { "&lt;id&gt;": "&lt;value&gt;" } }</c>. An unreadable vault is reported,
/// never thrown, so callers that save configuration keep working; the next <see cref="Save"/> replaces it.
/// </remarks>
public sealed class LocalSecretVault
{
    private const int CurrentVersion = 1;
    private static readonly byte[] Entropy = "Foundry.SecretVault.v1"u8.ToArray();
    private readonly string path;

    /// <summary>Creates a vault stored at <paramref name="path"/>.</summary>
    public LocalSecretVault(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        this.path = Path.GetFullPath(path);
    }

    /// <summary>Reads every entry, or returns an empty set when the vault is missing or unreadable.</summary>
    public IReadOnlyDictionary<string, string> Load(out VaultStatus status)
    {
        if (!File.Exists(path))
        {
            status = VaultStatus.Empty;
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }

        byte[]? json = null;
        try
        {
            json = ProtectedData.Unprotect(File.ReadAllBytes(path), Entropy, DataProtectionScope.CurrentUser);
            Dictionary<string, string> entries = Parse(json);
            status = VaultStatus.Ok;
            return entries;
        }
        catch (Exception exception) when (exception is CryptographicException or JsonException or InvalidDataException)
        {
            status = VaultStatus.Unreadable;
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }
        finally
        {
            if (json is not null) CryptographicOperations.ZeroMemory(json);
        }
    }

    /// <summary>Replaces the vault with <paramref name="entries"/> by writing a temporary file and renaming it over the vault.</summary>
    public void Save(IReadOnlyDictionary<string, string> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        byte[] json = Serialize(entries);
        string temporary = path + ".tmp";
        try
        {
            File.WriteAllBytes(temporary, ProtectedData.Protect(json, Entropy, DataProtectionScope.CurrentUser));
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(json);
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    private static byte[] Serialize(IReadOnlyDictionary<string, string> entries)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteNumber("version", CurrentVersion);
            writer.WriteStartObject("entries");
            foreach ((string id, string value) in entries) writer.WriteString(id, value);
            writer.WriteEndObject();
            writer.WriteEndObject();
        }
        return stream.ToArray();
    }

    private static Dictionary<string, string> Parse(byte[] json)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty("version", out JsonElement version) || version.ValueKind != JsonValueKind.Number ||
            !version.TryGetInt32(out int number) || number != CurrentVersion ||
            !root.TryGetProperty("entries", out JsonElement entries) || entries.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("SecretVault.UnexpectedShape");

        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (JsonProperty entry in entries.EnumerateObject())
        {
            if (entry.Value.ValueKind != JsonValueKind.String) throw new InvalidDataException("SecretVault.UnexpectedShape");
            result[entry.Name] = entry.Value.GetString()!;
        }
        return result;
    }
}
