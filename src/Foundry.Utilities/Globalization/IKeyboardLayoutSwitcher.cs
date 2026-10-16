// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Utilities.Globalization;

/// <summary>
/// Outcome of a keyboard layout switch. Callers log it; the switcher itself never throws for switching failures.
/// </summary>
/// <param name="Succeeded">Whether the layout is now active.</param>
/// <param name="LayoutId">KLID that was requested, when known.</param>
/// <param name="Error">Failure description, or <see langword="null"/> on success.</param>
public sealed record KeyboardLayoutSwitchResult(bool Succeeded, string? LayoutId, string? Error);

/// <summary>
/// Switches the active keyboard layout of a WinPE application at runtime.
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
