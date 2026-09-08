// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Text;

namespace Foundry.Core.Services.Configuration;

public static class ComputerNameRules
{
    public const int MaxLength = 15;
    public const string FallbackName = "PC";

    public static string Normalize(string? value)
    {
        string sanitized = Sanitize(value);
        return sanitized.Length > MaxLength
            ? sanitized[..MaxLength]
            : sanitized;
    }

    /// <summary>
    /// Removes unsupported computer-name characters without applying the Windows length limit.
    /// </summary>
    /// <param name="value">Value to sanitize.</param>
    /// <returns>The value containing only supported computer-name characters.</returns>
    public static string Sanitize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(value.Length);
        foreach (char character in value)
        {
            if (!IsAllowedCharacter(character))
            {
                continue;
            }

            builder.Append(character);
        }

        return builder.ToString();
    }

    /// <summary>
    /// Validates a complete computer name, including Windows Setup's restriction on numeric-only names.
    /// </summary>
    public static bool IsValid(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > MaxLength)
        {
            return false;
        }

        bool hasNonDigit = false;
        foreach (char character in value)
        {
            if (!IsAllowedCharacter(character))
            {
                return false;
            }

            hasNonDigit |= character is < '0' or > '9';
        }

        return hasNonDigit;
    }

    public static bool IsAllowedText(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return false;
        }

        foreach (char character in value)
        {
            if (!IsAllowedCharacter(character))
            {
                return false;
            }
        }

        return true;
    }

    public static bool IsAllowedCharacter(char character)
    {
        return (character >= 'A' && character <= 'Z') ||
               (character >= 'a' && character <= 'z') ||
               (character >= '0' && character <= '9') ||
               character == '-';
    }
}
