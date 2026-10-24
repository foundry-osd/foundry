// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Text.Json;
using System.Text.Json.Nodes;
using Foundry.Core.Models.Configuration;

namespace Foundry.Core.Services.Configuration;

/// <summary>Compares two configurations section by section, for diagnostics that must never carry values.</summary>
public static class ConfigurationSections
{
    /// <summary>
    /// Returns the persisted names of the top-level sections (for example <c>network</c> or <c>customization</c>) whose JSON
    /// differs between <paramref name="previous"/> and <paramref name="next"/>, in ordinal name order. Only names are returned.
    /// </summary>
    public static IReadOnlyList<string> Changed(FoundryConfigurationDocument previous, FoundryConfigurationDocument next)
    {
        ArgumentNullException.ThrowIfNull(previous);
        ArgumentNullException.ThrowIfNull(next);
        JsonObject before = Serialize(previous);
        JsonObject after = Serialize(next);
        return before.Select(section => section.Key).Union(after.Select(section => section.Key))
            .Where(name => !JsonNode.DeepEquals(before[name], after[name]))
            .Order(StringComparer.Ordinal)
            .ToArray();
    }

    private static JsonObject Serialize(FoundryConfigurationDocument configuration) =>
        JsonSerializer.SerializeToNode(configuration, ConfigurationJsonDefaults.SerializerOptions)!.AsObject();
}
