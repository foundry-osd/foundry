// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Text.Json;
using Foundry.Telemetry;

namespace Foundry.Core.Services.Runtime;

/// <summary>Reads only diagnostic preferences, without loading or decrypting child secrets.</summary>
public static class RuntimeTelemetryConsent
{
    private static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true };

    /// <summary>Requires explicit versioned Bootstrap consent before any early child diagnostics are authorized.</summary>
    public static TelemetrySettings? ReadBootstrap(string? json)
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

    /// <summary>Applies child opt-outs monotonically without reading encrypted configuration fields.</summary>
    public static (bool Usage, bool Diagnostics) RestrictChild(string? json, bool required,
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

    /// <summary>Reads bounded local configuration; unresolved paths and differing child destinations fail closed.</summary>
    public static TelemetrySettings? ReadSettings(string bootstrapConfigPath, string childConfigPath, bool childRequired = true)
    {
        TelemetrySettings? settings = ReadBootstrap(ReadConfiguration(bootstrapConfigPath));
        if (settings is null) return null;
        string? childJson = ReadConfiguration(childConfigPath);
        var consent = RestrictChild(childJson, childRequired,
            settings.IsEnabled, settings.IsRemoteDiagnosticsEnabled);
        if (!MatchesChildDestination(childJson, settings)) consent = (false, false);
        return settings with { IsEnabled = consent.Usage, IsRemoteDiagnosticsEnabled = consent.Diagnostics };
    }

    private static bool MatchesChildDestination(string? json, TelemetrySettings settings)
    {
        if (json is null) return true;
        try
        {
            using JsonDocument document = JsonDocument.Parse(json, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip });
            if (!TryProperty(document.RootElement, "telemetry", out JsonElement telemetry)) return true;
            return MatchesExplicitString(telemetry, "hostUrl", settings.HostUrl, MatchesHost) &&
                MatchesExplicitString(telemetry, "projectToken", settings.ProjectToken, (left, right) => left.Equals(right, StringComparison.Ordinal)) &&
                MatchesExplicitString(telemetry, "installId", settings.InstallId, (left, right) => left.Equals(right, StringComparison.Ordinal));
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException) { return false; }
    }

    private static bool MatchesExplicitString(JsonElement settings, string name, string expected, Func<string, string, bool> matches) =>
        !TryProperty(settings, name, out JsonElement property) ||
        (property.ValueKind == JsonValueKind.String && matches(property.GetString()!, expected));

    private static bool MatchesHost(string candidate, string expected) =>
        Uri.TryCreate(candidate, UriKind.Absolute, out Uri? left) && Uri.TryCreate(expected, UriKind.Absolute, out Uri? right) &&
        left.Scheme.Equals(right.Scheme, StringComparison.OrdinalIgnoreCase) &&
        left.Authority.Equals(right.Authority, StringComparison.OrdinalIgnoreCase) &&
        left.UserInfo.Equals(right.UserInfo, StringComparison.Ordinal) &&
        left.AbsolutePath.TrimEnd('/').Equals(right.AbsolutePath.TrimEnd('/'), StringComparison.Ordinal) &&
        left.Query.Equals(right.Query, StringComparison.Ordinal) && left.Fragment.Equals(right.Fragment, StringComparison.Ordinal);

    private static string? ReadConfiguration(string path)
    {
        try
        {
            ReadOnlySpan<byte> content = RuntimeStartupFile.Read(path, 1024 * 1024);
            ReadOnlySpan<byte> preamble = System.Text.Encoding.UTF8.Preamble;
            if (content.StartsWith(preamble)) content = content[preamble.Length..];
            return System.Text.Encoding.UTF8.GetString(content);
        }
        catch (FileNotFoundException) { return null; }
        catch (DirectoryNotFoundException) { return null; }
        catch (Exception exception) when (RuntimeStartupFile.IsExpectedFailure(exception)) { return "invalid"; }
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
