// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Core.Models.Configuration;

/// <summary>
/// Identifies the modifier that must be held with <see cref="TroubleshootingConsoleKey"/> to open the
/// WinPE troubleshooting console.
/// </summary>
public enum TroubleshootingConsoleModifier
{
    None,
    Control,
    Alt,
    Shift,
    ControlShift,
    ControlAlt
}
