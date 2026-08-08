// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Utilities.IO;

/// <summary>
/// Provides safe path segment formatting.
/// </summary>
public static class PathSegment
{
    /// <summary>
    /// Trims a path segment and replaces invalid filename characters and spaces with underscores.
    /// </summary>
    /// <param name="value">The path segment to sanitize.</param>
    /// <param name="fallback">The value returned when <paramref name="value"/> is blank.</param>
    /// <returns>A sanitized path segment or a sanitized fallback value.</returns>
    public static string Sanitize(string? value, string fallback = "item")
    {
        string pathSegment = string.IsNullOrWhiteSpace(value)
            ? fallback
            : value;

        if (string.IsNullOrWhiteSpace(pathSegment))
        {
            pathSegment = "item";
        }

        string result = pathSegment.Trim();
        foreach (char invalidCharacter in Path.GetInvalidFileNameChars())
        {
            result = result.Replace(invalidCharacter, '_');
        }

        return result.Replace(' ', '_');
    }
}
