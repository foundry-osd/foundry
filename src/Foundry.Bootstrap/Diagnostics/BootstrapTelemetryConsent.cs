// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Text.Json;
using Foundry.Telemetry;

namespace Foundry.Bootstrap.Diagnostics;

/// <summary>Reads only diagnostic preferences, without loading or decrypting child secrets.</summary>
internal static class BootstrapTelemetryConsent
{
    private static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true };

    internal static TelemetrySettings? ReadBootstrap(string? json)
    {
        try
        {
            if (json is null) { return null; }
            using JsonDocument document = JsonDocument.Parse(json);
            JsonElement root = document.RootElement;
            if (!TryProperty(root, "schemaVersion", out var version) || !version.TryGetInt32(out int schema) || schema != 1 ||
                !TryProperty(root, "telemetry", out var settings) ||
                !TryBoolean(settings, "isEnabled", out _) || !TryBoolean(settings, "isRemoteDiagnosticsEnabled", out _))
            {
                return null;
            }
            return settings.Deserialize<TelemetrySettings>(Options);
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException) { return null; }
    }

    internal static (bool Usage, bool Diagnostics) RestrictChild(string? json, bool required,
        bool usage, bool diagnostics)
    {
        if (json is null) { return required ? (false, false) : (usage, diagnostics); }
        try
        {
            using JsonDocument document = JsonDocument.Parse(json, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip });
            if (document.RootElement.ValueKind != JsonValueKind.Object) { return (false, false); }
            if (!TryProperty(document.RootElement, "telemetry", out var settings)) { return (usage, diagnostics); }
            if (settings.ValueKind != JsonValueKind.Object) { return (false, false); }
            if (TryProperty(settings, "isEnabled", out var enabled))
            {
                usage &= enabled.ValueKind == JsonValueKind.True;
            }
            if (TryProperty(settings, "isRemoteDiagnosticsEnabled", out var remote))
            {
                diagnostics &= remote.ValueKind == JsonValueKind.True;
            }
            return (usage, diagnostics);
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException) { return (false, false); }
    }

    private static bool TryBoolean(JsonElement element, string name, out bool value)
    {
        value = false;
        if (!TryProperty(element, name, out var property) || property.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) { return false; }
        value = property.GetBoolean();
        return true;
    }

    private static bool TryProperty(JsonElement element, string name, out JsonElement property)
    {
        property = default;
        bool found = false;
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var candidate in element.EnumerateObject())
            {
                if (!candidate.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) { continue; }
                if (found) { throw new JsonException("Duplicate telemetry configuration property."); }
                property = candidate.Value;
                found = true;
            }
        }
        return found;
    }
}
