// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.AutopilotInteractive.Services.Registration;

/// <summary>
/// Validation of the optional user the technician assigns to the device.
/// </summary>
public static class AssignedUserRules
{
    /// <summary>
    /// Tells whether the text is acceptable: blank (no user), or a simple user principal name.
    /// </summary>
    /// <remarks>
    /// Only the shape is checked: exactly one <c>@</c>, non-empty local and domain parts, no whitespace. Graph decides
    /// whether the account exists.
    /// </remarks>
    /// <param name="value">Text typed by the technician.</param>
    /// <returns><see langword="true"/> when the text can be sent or ignored.</returns>
    public static bool IsValid(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return true;
        }

        string trimmed = value.Trim();
        if (trimmed.Any(char.IsWhiteSpace))
        {
            return false;
        }

        string[] parts = trimmed.Split('@');
        return parts.Length == 2 && parts[0].Length > 0 && parts[1].Length > 0;
    }

    /// <summary>
    /// Converts the typed text to the value sent to Graph.
    /// </summary>
    /// <remarks>A blank value becomes <see langword="null"/>, which never clears a user already assigned in Autopilot.</remarks>
    /// <param name="value">Text typed by the technician.</param>
    /// <returns>The trimmed user principal name, or <see langword="null"/> when blank.</returns>
    public static string? ToRequestedUser(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
