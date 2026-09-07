// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Globalization;
using System.Text.Json;
namespace Foundry.Utilities.Diagnostics;

/// <summary>Projects the reviewed first-boot result schema onto bounded diagnostic values.</summary>
internal static class ActionResultSanitizer
{
    private static readonly HashSet<string> Ids = new(StringComparer.Ordinal) { "driver-pack", "network-profile-roaming", "remove-appx", "remove-ai-components", "cleanup" };
    private static readonly HashSet<string> Statuses = new(StringComparer.Ordinal) { "running", "succeeded", "failed", "interrupted", "skipped_dependency" };
    private static readonly HashSet<string> Errors = new(StringComparer.Ordinal)
    {
        "native_exit_failed", "driver_action_failed", "driver_registry_restore_failed", "secret_reentry_required",
        "required_input_missing", "required_script_missing", "invalid_input_ownership", "invalid_owned_path",
        "native_ownership_uncertain", "action_failed", "input_cleanup_failed", "dependency_failed"
    };

    /// <summary>Rejects malformed identity/status fields and never copies arbitrary output or exception values.</summary>
    public static string Sanitize(string content)
    {
        using var document = JsonDocument.Parse(content, new JsonDocumentOptions { MaxDepth = 16, AllowDuplicateProperties = false });
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Array || root.GetArrayLength() > Ids.Count) { throw new JsonException("Unsupported action results."); }
        var records = new List<Dictionary<string, object?>>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in root.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object || !item.TryGetProperty("id", out var id) || !item.TryGetProperty("status", out var status))
            { throw new JsonException("Unsupported action result."); }
            string safeId = KnownString(id, Ids);
            if (!seen.Add(safeId)) { throw new JsonException("Duplicate action result."); }
            var record = new Dictionary<string, object?> { ["id"] = safeId, ["status"] = KnownString(status, Statuses) };
            foreach (var property in item.EnumerateObject())
            {
                switch (property.Name)
                {
                    case "exitCode":
                        record[property.Name] = property.Value.ValueKind == JsonValueKind.Null ? null : ReadInteger(property.Value, int.MinValue, int.MaxValue);
                        break;
                    case "attempt":
                        record[property.Name] = ReadInteger(property.Value, 0, 3);
                        break;
                    case "rebootRequired":
                        if (property.Value.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) { throw new JsonException("Invalid boolean."); }
                        record[property.Name] = property.Value.GetBoolean();
                        break;
                    case "errorCode":
                    case "primaryErrorCode":
                        record[property.Name] = property.Value.ValueKind == JsonValueKind.Null ? null : KnownString(property.Value, Errors);
                        break;
                    case "startedUtc":
                    case "completedUtc":
                        record[property.Name] = ReadTimestamp(property.Value);
                        break;
                }
            }
            records.Add(record);
        }
        return JsonSerializer.Serialize(records);
    }

    private static int ReadInteger(JsonElement value, int minimum, int maximum)
    {
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out int number) || number < minimum || number > maximum) { throw new JsonException("Invalid integer."); }
        return number;
    }

    private static string KnownString(JsonElement value, HashSet<string> allowed)
    {
        if (value.ValueKind != JsonValueKind.String || value.GetString() is not { Length: <= 64 } text || !allowed.Contains(text)) { throw new JsonException("Unsupported diagnostic value."); }
        return text;
    }

    private static string? ReadTimestamp(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Null) { return null; }
        if (value.ValueKind != JsonValueKind.String || value.GetString() is not { Length: <= 40 } text ||
            !DateTimeOffset.TryParseExact(text, "O", CultureInfo.InvariantCulture, DateTimeStyles.None, out var timestamp) || timestamp.Offset != TimeSpan.Zero)
        { throw new JsonException("Invalid UTC timestamp."); }
        return timestamp.ToString("O", CultureInfo.InvariantCulture);
    }
}
