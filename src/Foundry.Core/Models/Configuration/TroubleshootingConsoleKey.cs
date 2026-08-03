// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Core.Models.Configuration;

/// <summary>
/// Identifies the key that opens the WinPE troubleshooting console from Foundry.Connect or Foundry.Deploy.
/// </summary>
/// <remarks>
/// Only function keys are offered so the shortcut cannot collide with text entry in the runtime apps.
/// </remarks>
public enum TroubleshootingConsoleKey
{
    F1,
    F2,
    F3,
    F4,
    F5,
    F6,
    F7,
    F8,
    F9,
    F10,
    F11,
    F12
}
