// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Utilities.Globalization;

/// <summary>
/// Outcome of a keyboard layout switch. Callers log it; the switcher itself never throws for switching failures.
/// </summary>
/// <param name="Succeeded">Whether the layout is now active in this process.</param>
/// <param name="LayoutId">KLID that was requested, when known.</param>
/// <param name="Error">Failure description; on success, a warning that later processes may not inherit the layout.</param>
public sealed record KeyboardLayoutSwitchResult(bool Succeeded, string? LayoutId, string? Error);

/// <summary>
/// Switches the active keyboard layout of a Foundry runtime application (WinPE, or the installed Windows during setup).
/// </summary>
public interface IKeyboardLayoutSwitcher
{
    /// <summary>
    /// Gets the keyboard layouts installed on the running system.
    /// </summary>
    IReadOnlyList<KeyboardLayout> GetLayouts();

    /// <summary>
    /// Gets the KLID of the keyboard layout active for the calling thread.
    /// </summary>
    string GetActiveLayoutId();

    /// <summary>
    /// Activates a layout for this process and, in Windows PE, for processes started afterwards.
    /// </summary>
    KeyboardLayoutSwitchResult Apply(KeyboardLayout layout);

    /// <summary>
    /// Re-applies the layout chosen by a previous WinPE application.
    /// </summary>
    /// <returns>The switch result, or <see langword="null"/> when no hand-off file exists.</returns>
    KeyboardLayoutSwitchResult? RestoreHandoff();
}
