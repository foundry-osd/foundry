// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.AutopilotInteractive.Services.Keyboard;

/// <summary>
/// Reads what the installed Windows actually provides for a keyboard layout, so the Keyboard menu never offers a layout
/// Windows cannot load.
/// </summary>
internal interface IKeyboardLayoutAvailability
{
    /// <summary>
    /// Gets the <c>Layout File</c> value of <c>HKLM\SYSTEM\CurrentControlSet\Control\Keyboard Layouts\&lt;KLID&gt;</c>.
    /// </summary>
    /// <param name="layoutId">Normalized KLID.</param>
    /// <returns>The layout file name, or <see langword="null"/> when the key or the value is missing.</returns>
    string? GetRegisteredLayoutFile(string layoutId);

    /// <summary>Gets whether a layout file exists in <c>%SystemRoot%\System32</c>.</summary>
    /// <param name="layoutFile">Layout file name, for example <c>KBDBE.DLL</c>.</param>
    /// <returns><see langword="true"/> when the file exists.</returns>
    bool SystemFileExists(string layoutFile);
}
