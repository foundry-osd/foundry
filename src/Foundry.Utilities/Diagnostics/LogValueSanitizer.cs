// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Text;

namespace Foundry.Utilities.Diagnostics;

/// <summary>
/// Normalizes untrusted values before they are attached to structured diagnostic events.
/// </summary>
public static class LogValueSanitizer
{
    /// <summary>
    /// Replaces control characters with a single space so a value cannot forge additional log records.
    /// </summary>
    public static string NormalizePropertyValue(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(value.Length);
        bool whitespacePending = false;
        foreach (char character in value)
        {
            if (char.IsControl(character) || char.IsWhiteSpace(character))
            {
                whitespacePending = builder.Length > 0;
                continue;
            }

            if (whitespacePending)
            {
                builder.Append(' ');
                whitespacePending = false;
            }

            builder.Append(character);
        }

        return builder.ToString();
    }

    /// <summary>
    /// Returns an absolute URI without query-string or fragment content.
    /// </summary>
    public static string SanitizeUri(Uri? uri)
    {
        if (uri is null || !uri.IsAbsoluteUri)
        {
            return "<invalid-uri>";
        }

        var sanitized = new UriBuilder(uri)
        {
            UserName = string.Empty,
            Password = string.Empty,
            Query = string.Empty,
            Fragment = string.Empty
        };

        return sanitized.Uri.GetLeftPart(UriPartial.Path);
    }
}
