// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace Foundry.Connect.Services.Runtime;

/// <summary>A same-run selection hint; bootstrap independently verifies media and content before offline handoff.</summary>
public sealed record OfflineReadinessHint(bool CanBrowse)
{
    public static OfflineReadinessHint Read(string[] args)
    {
        string? path = Argument(args, "--offline-readiness");
        string? nonce = Argument(args, "--offline-nonce");
        return Read(path, nonce, "win-" + RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant());
    }

    internal static OfflineReadinessHint Read(string? path, string? nonce, string runtimeIdentifier)
    {
        if (!Guid.TryParse(nonce, out Guid expectedNonce) || expectedNonce == Guid.Empty || string.IsNullOrWhiteSpace(path)) return new(false);
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (stream.Length is <= 0 or > 65536) return new(false);
            byte[] bytes = new byte[checked((int)stream.Length)];
            stream.ReadExactly(bytes);
            if (stream.ReadByte() != -1) return new(false);
            using JsonDocument document = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 8 });
            JsonElement root = document.RootElement;
            if (root.GetProperty("Version").GetInt32() != 1 || root.GetProperty("Nonce").GetGuid() != expectedNonce ||
                root.GetProperty("RuntimeIdentifier").GetString() != runtimeIdentifier) return new(false);
            JsonElement result = root.GetProperty("Result");
            string? digest = result.GetProperty("ConfigurationDigest").GetString();
            return new(root.GetProperty("CanBrowse").GetBoolean() && result.GetProperty("MediaId").GetGuid() != Guid.Empty &&
                digest is { Length: 64 } && digest.All(Uri.IsHexDigit) &&
                result.GetProperty("CatalogRevisions").GetArrayLength() > 0);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException or KeyNotFoundException or FormatException)
        {
            return new(false);
        }
    }

    private static string? Argument(string[] args, string name)
    {
        int index = Array.FindIndex(args, value => value.Equals(name, StringComparison.OrdinalIgnoreCase));
        return index >= 0 && index + 1 < args.Length && args.Count(value => value.Equals(name, StringComparison.OrdinalIgnoreCase)) == 1
            ? args[index + 1] : null;
    }
}
