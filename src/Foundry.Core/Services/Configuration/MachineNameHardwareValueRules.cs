// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Core.Services.Configuration;

/// <summary>
/// Rejects firmware placeholders that cannot identify a target machine.
/// </summary>
public static class MachineNameHardwareValueRules
{
    private static readonly HashSet<string> Placeholders = new(StringComparer.OrdinalIgnoreCase)
    {
        "Unknown",
        "Default string",
        "To Be Filled By O.E.M."
    };

    public static bool IsPlaceholder(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        string trimmed = value.Trim();
        if (Placeholders.Contains(trimmed))
        {
            return true;
        }

        string compact = trimmed.Replace("-", string.Empty, StringComparison.Ordinal);
        return compact.Length == 32 &&
               (compact.All(character => character == '0') || compact.All(character => character is 'F' or 'f'));
    }
}
