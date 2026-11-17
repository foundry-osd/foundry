// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Text.Json;
using System.Text.Json.Nodes;

namespace Foundry.Core.Services.Configuration;

/// <summary>
/// Reads <c>foundry.winpe.config.json</c> for one WinPE mode: the sections every process reads at the root and the section of
/// that mode, matched case-insensitively because the file can be written by hand.
/// </summary>
/// <remarks>
/// The view leaves out the section of the other mode, so a Connect start never depends on the Deploy settings and the reverse.
/// Media preparation edits the generated file with <see cref="WinPeConfigurationJson"/> instead.
/// </remarks>
public static class WinPeConfigurationReader
{
    /// <summary>The name of the section read by the Connect mode.</summary>
    public const string ConnectSection = "connect";

    /// <summary>The name of the section read by the Deploy mode.</summary>
    public const string DeploySection = "deploy";

    private static readonly string[] SharedSections = ["schemaVersion", "authoringVersion", "telemetry", "protection", "network"];

    private static readonly JsonDocumentOptions DocumentOptions = new()
    {
        AllowTrailingCommas = true,
        CommentHandling = JsonCommentHandling.Skip
    };

    /// <summary>
    /// Builds the view of one mode: the shared root sections (<c>schemaVersion</c>, <c>authoringVersion</c>, <c>telemetry</c>,
    /// <c>protection</c>, <c>network</c>) followed by the properties of the <paramref name="sectionName"/> section. A shared
    /// section repeated inside the mode section is ignored: the root value wins.
    /// </summary>
    /// <param name="configurationJson">The content of the configuration file.</param>
    /// <param name="sectionName">The mode section, <see cref="ConnectSection"/> or <see cref="DeploySection"/>.</param>
    /// <returns>
    /// A new JSON object: the shared sections under their camelCase names, then the mode properties under the names the file
    /// uses. Deserialize it case-insensitively into the runtime model of the mode.
    /// </returns>
    /// <exception cref="JsonException">
    /// The text is not a JSON object, the mode section is not an object, or a name of the root or of the mode section appears
    /// twice ignoring case.
    /// </exception>
    public static JsonObject ReadModeView(string configurationJson, string sectionName)
    {
        ArgumentNullException.ThrowIfNull(configurationJson);
        ArgumentException.ThrowIfNullOrWhiteSpace(sectionName);
        if (JsonNode.Parse(configurationJson, documentOptions: DocumentOptions) is not JsonObject root)
        {
            throw new JsonException("The WinPE runtime configuration is not a JSON object.");
        }

        var view = new JsonObject();
        foreach (string name in SharedSections)
        {
            if (TryGetProperty(root, name, out JsonNode? value))
            {
                view[name] = value?.DeepClone();
            }
        }

        if (!TryGetProperty(root, sectionName, out JsonNode? section) || section is null)
        {
            return view;
        }

        if (section is not JsonObject properties)
        {
            throw new JsonException($"The '{sectionName}' section of the WinPE runtime configuration is not a JSON object.");
        }

        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach ((string name, JsonNode? value) in properties)
        {
            if (!names.Add(name))
            {
                throw new JsonException($"The '{sectionName}' section of the WinPE runtime configuration repeats the '{name}' property.");
            }

            if (!SharedSections.Contains(name, StringComparer.OrdinalIgnoreCase))
            {
                view[name] = value?.DeepClone();
            }
        }

        return view;
    }

    private static bool TryGetProperty(JsonObject element, string name, out JsonNode? value)
    {
        value = null;
        bool found = false;
        foreach ((string candidate, JsonNode? candidateValue) in element)
        {
            if (!candidate.Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (found)
            {
                throw new JsonException($"The WinPE runtime configuration repeats the '{name}' property.");
            }

            value = candidateValue;
            found = true;
        }

        return found;
    }
}
