// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Text.Json;

namespace Foundry.Utilities.Serialization;

/// <summary>
/// Reads a JSON object or an array of JSON objects as an independent sequence.
/// </summary>
public static class JsonObjectSequence
{
    /// <summary>
    /// Parses a JSON object or array of objects and clones each returned element.
    /// </summary>
    /// <param name="json">The JSON payload.</param>
    /// <exception cref="ArgumentNullException"><paramref name="json"/> is null.</exception>
    /// <exception cref="JsonException">The payload is malformed or does not contain only objects.</exception>
    public static IReadOnlyList<JsonElement> Parse(string json)
    {
        ArgumentNullException.ThrowIfNull(json);

        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement root = document.RootElement;
        if (root.ValueKind == JsonValueKind.Object)
        {
            return [root.Clone()];
        }

        if (root.ValueKind != JsonValueKind.Array)
        {
            throw new JsonException("The JSON root must be an object or an array of objects.");
        }

        var elements = new List<JsonElement>();
        foreach (JsonElement element in root.EnumerateArray())
        {
            if (element.ValueKind != JsonValueKind.Object)
            {
                throw new JsonException("The JSON array must contain only objects.");
            }

            elements.Add(element.Clone());
        }

        return elements;
    }
}
