// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Text;
using System.Text.RegularExpressions;

namespace Foundry.Core.Services.Configuration;

/// <summary>
/// Supports computer-name prefixes that contain <c>$VARIABLE</c> tokens (such as <c>$SERIALNUMBER</c>) which are
/// expanded from detected hardware at deployment time. The final expanded name is still normalized to the
/// 15-character NetBIOS limit by <see cref="ComputerNameRules"/>.
/// </summary>
public static partial class ComputerNameTemplate
{
    /// <summary>
    /// Gets the variable names that can be used in a computer-name prefix template (case-insensitive).
    /// </summary>
    public static readonly IReadOnlyList<string> SupportedVariables =
    [
        "SERIALNUMBER",
        "MODEL",
        "MANUFACTURER",
        "PRODUCT"
    ];

    /// <summary>
    /// Gets the maximum length of an unexpanded template. Templates may exceed the 15-character name limit because
    /// variables are expanded (and the result re-truncated) at deployment time.
    /// </summary>
    public const int MaxTemplateLength = 63;

    [GeneratedRegex(@"\$([A-Za-z][A-Za-z0-9]*)")]
    private static partial Regex VariableTokenRegex();

    /// <summary>
    /// Gets whether the supplied prefix contains a <c>$VARIABLE</c> token.
    /// </summary>
    public static bool ContainsVariable(string? value)
    {
        return !string.IsNullOrEmpty(value) && value.Contains('$', StringComparison.Ordinal);
    }

    /// <summary>
    /// Normalizes a prefix for authoring. Plain prefixes are normalized by <see cref="ComputerNameRules"/>
    /// (valid characters, truncated to 15). Prefixes containing a variable are normalized as templates, keeping
    /// <c>$</c> and the token characters and allowing the longer template length.
    /// </summary>
    public static string NormalizePrefix(string? value)
    {
        return ContainsVariable(value)
            ? NormalizeTemplate(value)
            : ComputerNameRules.Normalize(value);
    }

    /// <summary>
    /// Gets whether the supplied prefix is valid for authoring (as a plain name or as a variable template).
    /// </summary>
    public static bool IsValidPrefix(string? value)
    {
        return ContainsVariable(value)
            ? IsValidTemplate(value)
            : ComputerNameRules.IsValid(value);
    }

    /// <summary>
    /// Normalizes a template, keeping computer-name characters plus <c>$</c> and truncating to
    /// <see cref="MaxTemplateLength"/>.
    /// </summary>
    public static string NormalizeTemplate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(MaxTemplateLength);
        foreach (char character in value)
        {
            if (!ComputerNameRules.IsAllowedCharacter(character) && character != '$')
            {
                continue;
            }

            builder.Append(character);
            if (builder.Length >= MaxTemplateLength)
            {
                break;
            }
        }

        return builder.ToString();
    }

    /// <summary>
    /// Gets whether the supplied template contains only valid characters and is within the template length limit.
    /// </summary>
    public static bool IsValidTemplate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > MaxTemplateLength)
        {
            return false;
        }

        foreach (char character in value)
        {
            if (!ComputerNameRules.IsAllowedCharacter(character) && character != '$')
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Expands <c>$VARIABLE</c> tokens using the supplied values (case-insensitive). Unknown tokens are removed.
    /// The result is not normalized; callers apply <see cref="ComputerNameRules.Normalize(string)"/> to enforce the
    /// 15-character limit and character rules.
    /// </summary>
    public static string Expand(string? template, IReadOnlyDictionary<string, string?> variables)
    {
        if (string.IsNullOrEmpty(template))
        {
            return string.Empty;
        }

        return VariableTokenRegex().Replace(template, match =>
        {
            string name = match.Groups[1].Value;
            foreach (KeyValuePair<string, string?> variable in variables)
            {
                if (string.Equals(variable.Key, name, StringComparison.OrdinalIgnoreCase))
                {
                    return variable.Value ?? string.Empty;
                }
            }

            return string.Empty;
        });
    }

    /// <summary>
    /// Expands the template with the supplied variables and normalizes the result to a valid 15-character name.
    /// </summary>
    public static string ExpandAndNormalize(string? template, IReadOnlyDictionary<string, string?> variables)
    {
        return ComputerNameRules.Normalize(Expand(template, variables));
    }
}
