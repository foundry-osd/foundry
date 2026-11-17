// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Text.Json;
using Foundry.Core.Services.Configuration;
using Foundry.Telemetry;

namespace Foundry.Core.Services.Runtime;

/// <summary>
/// Reads only the diagnostic preferences of <c>foundry.winpe.config.json</c>, without loading or decrypting any other section.
/// </summary>
public static class RuntimeTelemetryConsent
{
    private const int MaximumConfigurationBytes = 1024 * 1024;

    // Same leniency as WinPeConfigurationReader, so early consent and the mode readers accept the same documents.
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip
    };

    private static readonly JsonDocumentOptions DocumentOptions = new()
    {
        AllowTrailingCommas = true,
        CommentHandling = JsonCommentHandling.Skip
    };

    /// <summary>
    /// Requires a valid schema version and explicit usage and diagnostics preferences before any early diagnostics are
    /// authorized; an ambiguous (repeated) property fails closed.
    /// </summary>
    /// <param name="json">The content of the WinPE runtime configuration, or <see langword="null"/> when it is missing.</param>
    /// <returns>The telemetry settings, or <see langword="null"/> when nothing may be sent.</returns>
    public static TelemetrySettings? Read(string? json)
    {
        try
        {
            if (json is null) { return null; }
            using JsonDocument document = JsonDocument.Parse(json, DocumentOptions);
            JsonElement root = document.RootElement;
            if (!TryProperty(root, "schemaVersion", out var version) || version.ValueKind != JsonValueKind.Number ||
                !version.TryGetInt32(out int schema) ||
                WinPeConfigurationSchemaPolicy.Evaluate(schema) == WinPeConfigurationSchemaDecision.Invalid ||
                !TryProperty(root, "telemetry", out var settings) ||
                !TryBoolean(settings, "isEnabled", out _) || !TryBoolean(settings, "isRemoteDiagnosticsEnabled", out _))
            {
                return null;
            }
            return settings.Deserialize<TelemetrySettings>(Options);
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException) { return null; }
    }

    /// <summary>Reads the preferences of a bounded local configuration file; a missing, relative or unreadable path fails closed.</summary>
    /// <param name="configurationPath">The full path of <c>foundry.winpe.config.json</c>.</param>
    /// <returns>The telemetry settings, or <see langword="null"/> when nothing may be sent.</returns>
    public static TelemetrySettings? ReadSettings(string configurationPath) => Read(ReadConfiguration(configurationPath));

    private static string? ReadConfiguration(string path)
    {
        try
        {
            ReadOnlySpan<byte> content = RuntimeStartupFile.Read(path, MaximumConfigurationBytes);
            ReadOnlySpan<byte> preamble = System.Text.Encoding.UTF8.Preamble;
            if (content.StartsWith(preamble)) content = content[preamble.Length..];
            return System.Text.Encoding.UTF8.GetString(content);
        }
        catch (Exception exception) when (RuntimeStartupFile.IsExpectedFailure(exception))
        {
            return null;
        }
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
