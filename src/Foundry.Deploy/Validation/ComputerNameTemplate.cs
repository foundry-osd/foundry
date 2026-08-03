// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Text.RegularExpressions;

namespace Foundry.Deploy.Validation;

/// <summary>
/// Expands <c>$VARIABLE</c> tokens (such as <c>$SERIALNUMBER</c>) in a computer-name prefix using detected
/// hardware values, then normalizes the result to the 15-character NetBIOS limit via <see cref="ComputerNameRules"/>.
/// </summary>
public static partial class ComputerNameTemplate
{
    [GeneratedRegex(@"\$\{([A-Za-z][A-Za-z0-9]*)\}")]
    private static partial Regex VariableTokenRegex();

    /// <summary>
    /// Gets whether the supplied prefix contains a <c>${VARIABLE}</c> token.
    /// </summary>
    public static bool ContainsVariable(string? value)
    {
        return !string.IsNullOrEmpty(value) && value.Contains("${", StringComparison.Ordinal);
    }

    /// <summary>
    /// Expands <c>$VARIABLE</c> tokens using the supplied values (case-insensitive); unknown tokens are removed.
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
    /// Expands the template and normalizes the result to a valid 15-character computer name.
    /// </summary>
    public static string ExpandAndNormalize(string? template, IReadOnlyDictionary<string, string?> variables)
    {
        return ComputerNameRules.Normalize(Expand(template, variables));
    }
}
