// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Utilities.Globalization;

namespace Foundry.AutopilotInteractive.Services.Keyboard;

/// <summary>
/// Keeps the catalog layouts the installed Windows can load: the application runs during Windows setup, where an image may
/// list a layout whose file is not present.
/// </summary>
internal static class KeyboardLayoutAvailabilityFilter
{
    /// <summary>
    /// Returns the layouts whose registry key exists with a <c>Layout File</c> value naming a file present in
    /// <c>%SystemRoot%\System32</c>, in catalog order.
    /// </summary>
    /// <param name="layouts">Catalog layouts.</param>
    /// <param name="availability">Reads the registry and the system folder.</param>
    /// <returns>The available layouts.</returns>
    public static IReadOnlyList<KeyboardLayout> Filter(IReadOnlyList<KeyboardLayout> layouts, IKeyboardLayoutAvailability availability)
    {
        ArgumentNullException.ThrowIfNull(layouts);
        ArgumentNullException.ThrowIfNull(availability);

        return layouts.Where(layout => IsAvailable(layout, availability)).ToArray();
    }

    private static bool IsAvailable(KeyboardLayout layout, IKeyboardLayoutAvailability availability)
    {
        string? layoutFile = availability.GetRegisteredLayoutFile(layout.Id)?.Trim();
        return !string.IsNullOrEmpty(layoutFile) && availability.SystemFileExists(layoutFile);
    }
}
