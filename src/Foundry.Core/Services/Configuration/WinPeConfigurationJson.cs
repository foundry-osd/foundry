// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Text.Json;
using System.Text.Json.Nodes;
using Foundry.Core.Models.Configuration;
using Foundry.Core.Models.Configuration.Deploy;

namespace Foundry.Core.Services.Configuration;

/// <summary>
/// Reads and rewrites the <c>deploy</c> node of a serialized <see cref="FoundryWinPeConfigurationDocument"/> for media
/// preparation: manifest binding and the network-cache split change that node only, so every other section keeps the exact
/// bytes the generator wrote.
/// </summary>
internal static class WinPeConfigurationJson
{
    private const string DeployPropertyName = "deploy";

    /// <summary>
    /// Reads the <c>deploy</c> node; a document without that node has the default section, like any section the generator
    /// leaves at its default.
    /// </summary>
    /// <returns>The section, or <see langword="null"/> when the document or its <c>deploy</c> node is JSON <c>null</c>.</returns>
    /// <exception cref="JsonException">The text is not JSON or its <c>deploy</c> node is not a Deploy section.</exception>
    public static DeployRuntimeSettings? ReadDeploy(string configurationJson)
    {
        JsonObject? root = ParseRoot(configurationJson);
        if (root is null)
        {
            return null;
        }

        return root.TryGetPropertyValue(DeployPropertyName, out JsonNode? deploy)
            ? deploy?.Deserialize<DeployRuntimeSettings>(ConfigurationJsonDefaults.SerializerOptions)
            : new DeployRuntimeSettings();
    }

    /// <summary>
    /// Replaces the <c>deploy</c> node with <paramref name="update"/> applied to it and leaves every other property in place.
    /// </summary>
    /// <returns>The document with its new <c>deploy</c> node, or <see langword="null"/> when there is no section to update.</returns>
    /// <exception cref="JsonException">The text is not JSON or its <c>deploy</c> node is not a Deploy section.</exception>
    public static string? UpdateDeploy(string configurationJson, Func<DeployRuntimeSettings, DeployRuntimeSettings> update)
    {
        ArgumentNullException.ThrowIfNull(update);
        JsonObject? root = ParseRoot(configurationJson);
        if (root is null)
        {
            return null;
        }

        DeployRuntimeSettings? deploy = root.TryGetPropertyValue(DeployPropertyName, out JsonNode? node)
            ? node?.Deserialize<DeployRuntimeSettings>(ConfigurationJsonDefaults.SerializerOptions)
            : new DeployRuntimeSettings();
        if (deploy is null)
        {
            return null;
        }

        // The indexer replaces an existing property in place, so the property order of the document is kept.
        root[DeployPropertyName] = JsonSerializer.SerializeToNode(update(deploy), ConfigurationJsonDefaults.SerializerOptions);
        return root.ToJsonString(ConfigurationJsonDefaults.SerializerOptions);
    }

    private static JsonObject? ParseRoot(string configurationJson)
    {
        ArgumentNullException.ThrowIfNull(configurationJson);
        JsonNode? root = JsonNode.Parse(configurationJson);
        return root switch
        {
            null => null,
            JsonObject value => value,
            _ => throw new JsonException("The WinPE runtime configuration is not a JSON object.")
        };
    }
}
