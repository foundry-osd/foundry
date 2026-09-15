// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Text.Json;
using Foundry.Core.Models.Runtime;

namespace Foundry.Core.Services.Runtime;

/// <summary>
/// Reads boot-owned trust anchors for original runtime archives. Mutable cache metadata cannot establish trust.
/// </summary>
public static class RuntimePayloadTrust
{
    /// <summary>Names the manifest beneath the boot-owned Config directory.</summary>
    public const string ManifestFileName = "foundry.runtime-trust.json";

    /// <summary>Identifies the supported manifest contract.</summary>
    public const int SchemaVersion = 1;

    /// <summary>Names the Bootstrap publication marker required by hardened media authoring.</summary>
    public const string CapabilityFileName = "foundry.runtime-trust-capability.json";

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    /// <summary>
    /// Reads a matching archive hash, validating the entire manifest before trusting any entry.
    /// </summary>
    /// <param name="winPeRoot">The boot-owned Foundry directory.</param>
    /// <param name="applicationName">The Connect or Deploy application name.</param>
    /// <param name="runtimeIdentifier">The target Windows runtime identifier.</param>
    /// <returns>The normalized SHA256, or null when the manifest or entry is absent.</returns>
    /// <exception cref="InvalidDataException">The identity or manifest is malformed, duplicated, or unsupported.</exception>
    public static string? ReadArchiveHash(string winPeRoot, string applicationName, string runtimeIdentifier)
    {
        ValidateIdentity(applicationName, runtimeIdentifier);
        string path = Path.Combine(winPeRoot, "Config", ManifestFileName);
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path));
            JsonElement root = document.RootElement;
            ValidateObject(root);
            if (!root.TryGetProperty("schemaVersion", out JsonElement schema) ||
                !schema.TryGetInt32(out int version) || version != SchemaVersion ||
                !root.TryGetProperty("archives", out JsonElement archives) || archives.ValueKind != JsonValueKind.Array)
            {
                throw new InvalidDataException("Runtime trust manifest schema is unsupported or malformed.");
            }

            var identities = new HashSet<(string, string)>();
            string? matchingHash = null;
            foreach (JsonElement archive in archives.EnumerateArray())
            {
                ValidateObject(archive);
                string application = ReadString(archive, "application");
                string runtime = ReadString(archive, "runtimeIdentifier");
                string hash = ReadString(archive, "sha256");
                ValidateIdentity(application, runtime);
                ValidateSha256(hash);
                if (!identities.Add((application, runtime)))
                {
                    throw new InvalidDataException("Runtime trust manifest contains duplicate archive identities.");
                }

                if (application == applicationName && runtime == runtimeIdentifier)
                {
                    matchingHash = hash.ToLowerInvariant();
                }
            }

            return matchingHash;
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException)
        {
            throw new InvalidDataException("Runtime trust manifest is malformed.", ex);
        }
    }

    /// <summary>Resolves the original archive beneath an image or USB runtime root.</summary>
    /// <param name="runtimeRoot">The Runtime directory.</param>
    /// <param name="applicationName">The Connect or Deploy application name.</param>
    /// <param name="runtimeIdentifier">The target Windows runtime identifier.</param>
    /// <returns>The complete original archive path.</returns>
    public static string GetBaselineArchivePath(string runtimeRoot, string applicationName, string runtimeIdentifier)
    {
        ValidateIdentity(applicationName, runtimeIdentifier);
        return Path.Combine(runtimeRoot, applicationName, runtimeIdentifier, "original.zip");
    }

    /// <summary>Writes validated trust anchors into the mounted image after archive provisioning succeeds.</summary>
    /// <param name="winPeRoot">The boot-owned Foundry directory.</param>
    /// <param name="archives">The enabled provisioned originals, including USB-only archives.</param>
    public static void WriteManifest(string winPeRoot, IEnumerable<RuntimePayloadArchiveTrust> archives)
    {
        RuntimePayloadArchiveTrust[] entries = archives.ToArray();
        var identities = new HashSet<(string, string)>();
        foreach (RuntimePayloadArchiveTrust entry in entries)
        {
            ValidateIdentity(entry.Application, entry.RuntimeIdentifier);
            ValidateSha256(entry.Sha256);
            if (!identities.Add((entry.Application, entry.RuntimeIdentifier)))
            {
                throw new InvalidDataException("Runtime trust manifest contains duplicate archive identities.");
            }
        }

        string directory = Path.Combine(winPeRoot, "Config");
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, ManifestFileName),
            JsonSerializer.Serialize(new { SchemaVersion, Archives = entries }, SerializerOptions));
    }

    private static void ValidateIdentity(string application, string runtime)
    {
        if (application is not ("Foundry.Connect" or "Foundry.Deploy") || runtime is not ("win-x64" or "win-arm64"))
        {
            throw new InvalidDataException("Runtime trust archive application or runtime identifier is unsupported.");
        }
    }

    private static void ValidateSha256(string hash)
    {
        if (hash is null || hash.Length != 64 || hash.Any(character => !Uri.IsHexDigit(character)))
        {
            throw new InvalidDataException("Runtime trust archive SHA256 is invalid.");
        }
    }

    private static void ValidateObject(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object ||
            element.EnumerateObject().Select(property => property.Name).Distinct(StringComparer.Ordinal).Count() != element.EnumerateObject().Count())
        {
            throw new InvalidDataException("Runtime trust manifest requires objects without duplicate properties.");
        }
    }

    private static string ReadString(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out JsonElement property) || property.ValueKind != JsonValueKind.String)
        {
            throw new InvalidDataException($"Runtime trust archive '{name}' is missing or invalid.");
        }

        return property.GetString()!;
    }
}
