using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace Foundry.Deploy.Services.Deployment;

/// <summary>
/// Updates SetupComplete.cmd by appending or removing marked Foundry command blocks.
/// </summary>
public sealed class SetupCompleteScriptService : ISetupCompleteScriptService
{
    /// <inheritdoc />
    public string EnsureBlock(string setupCompletePath, string markerKey, string scriptBody)
    {
        if (string.IsNullOrWhiteSpace(setupCompletePath))
        {
            throw new ArgumentException("SetupComplete path is required.", nameof(setupCompletePath));
        }

        if (string.IsNullOrWhiteSpace(markerKey))
        {
            throw new ArgumentException("Marker key is required.", nameof(markerKey));
        }

        if (string.IsNullOrWhiteSpace(scriptBody))
        {
            throw new ArgumentException("Script body is required.", nameof(scriptBody));
        }

        string directory = Path.GetDirectoryName(setupCompletePath)
            ?? throw new InvalidOperationException("Unable to resolve SetupComplete directory.");
        Directory.CreateDirectory(directory);

        string normalizedKey = NormalizeMarkerKey(markerKey);
        string beginMarker = $"REM >>> {normalizedKey} BEGIN";
        string endMarker = $"REM <<< {normalizedKey} END";
        string snippet = BuildSnippet(beginMarker, endMarker, scriptBody);

        if (!File.Exists(setupCompletePath))
        {
            File.WriteAllText(setupCompletePath, "@echo off" + Environment.NewLine + snippet, Encoding.ASCII);
            return setupCompletePath;
        }

        string existing = File.ReadAllText(setupCompletePath);
        if (existing.Contains(beginMarker, StringComparison.OrdinalIgnoreCase))
        {
            return setupCompletePath;
        }

        string separator = existing.EndsWith(Environment.NewLine, StringComparison.Ordinal)
            ? string.Empty
            : Environment.NewLine;
        File.WriteAllText(setupCompletePath, existing + separator + snippet, Encoding.ASCII);
        return setupCompletePath;
    }

    /// <inheritdoc />
    public string RemoveBlock(string setupCompletePath, string markerKey)
    {
        if (string.IsNullOrWhiteSpace(setupCompletePath))
        {
            throw new ArgumentException("SetupComplete path is required.", nameof(setupCompletePath));
        }

        if (string.IsNullOrWhiteSpace(markerKey))
        {
            throw new ArgumentException("Marker key is required.", nameof(markerKey));
        }

        if (!File.Exists(setupCompletePath))
        {
            return setupCompletePath;
        }

        string normalizedKey = NormalizeMarkerKey(markerKey);
        string beginMarker = $"REM >>> {normalizedKey} BEGIN";
        string endMarker = $"REM <<< {normalizedKey} END";
        string existing = File.ReadAllText(setupCompletePath);
        string pattern =
            $@"(?im)^[ \t]*{Regex.Escape(beginMarker)}[ \t]*\r?\n" +
            $@"[\s\S]*?" +
            $@"^[ \t]*{Regex.Escape(endMarker)}[ \t]*(?:\r?\n)?";
        string updated = Regex.Replace(existing, pattern, string.Empty);

        if (!string.Equals(existing, updated, StringComparison.Ordinal))
        {
            File.WriteAllText(setupCompletePath, updated, Encoding.ASCII);
        }

        return setupCompletePath;
    }

    private static string NormalizeMarkerKey(string markerKey)
    {
        return markerKey.Trim();
    }

    private static string BuildSnippet(string beginMarker, string endMarker, string scriptBody)
    {
        string normalizedBody = scriptBody.TrimEnd();
        return
            $"{beginMarker}{Environment.NewLine}" +
            normalizedBody + Environment.NewLine +
            $"{endMarker}{Environment.NewLine}";
    }
}
