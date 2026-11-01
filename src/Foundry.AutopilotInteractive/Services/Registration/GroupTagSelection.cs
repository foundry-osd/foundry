// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Services.Autopilot;

namespace Foundry.AutopilotInteractive.Services.Registration;

/// <summary>Which entry of the group tag picker is selected.</summary>
public enum GroupTagChoice
{
    /// <summary>No group tag.</summary>
    None,

    /// <summary>A tag from the list discovered in the tenant.</summary>
    Existing,

    /// <summary>A tag typed in the custom text box.</summary>
    Custom
}

/// <summary>
/// Initial state of the group tag picker.
/// </summary>
/// <param name="Choice">Selected entry.</param>
/// <param name="ExistingTag">The list entry, with the list's spelling, when <paramref name="Choice"/> is <see cref="GroupTagChoice.Existing"/>.</param>
/// <param name="CustomText">Text of the custom box when <paramref name="Choice"/> is <see cref="GroupTagChoice.Custom"/>.</param>
/// <param name="FocusCustom">Whether the custom text box takes the keyboard focus.</param>
public sealed record GroupTagInitialSelection(GroupTagChoice Choice, string? ExistingTag, string? CustomText, bool FocusCustom);

/// <summary>
/// Selection rules of the group tag picker.
/// </summary>
public static class GroupTagSelection
{
    /// <summary>
    /// Chooses the initial selection from the tenant's tags and the lookup of this device.
    /// </summary>
    /// <remarks>
    /// A registered device always shows its own tag (matched to the list ignoring case and surrounding spaces) and ignores
    /// <paramref name="preselectCustom"/>. A device that is new or ambiguous follows the configured preselection.
    /// </remarks>
    /// <param name="tags">Tags discovered in the tenant.</param>
    /// <param name="lookup">Result of the serial number lookup.</param>
    /// <param name="preselectCustom">Whether the configuration preselects the custom entry for a new device.</param>
    /// <returns>The initial selection.</returns>
    public static GroupTagInitialSelection Resolve(IReadOnlyList<string> tags, AutopilotDeviceLookup lookup, bool preselectCustom)
    {
        ArgumentNullException.ThrowIfNull(tags);
        ArgumentNullException.ThrowIfNull(lookup);

        if (lookup.Status == AutopilotDeviceLookupStatus.Found && lookup.Device is not null)
        {
            string? registeredTag = lookup.Device.GroupTag?.Trim();
            if (string.IsNullOrEmpty(registeredTag))
            {
                return new GroupTagInitialSelection(GroupTagChoice.None, null, null, false);
            }

            string? listed = tags.FirstOrDefault(tag => string.Equals(tag?.Trim(), registeredTag, StringComparison.OrdinalIgnoreCase));
            return listed is not null
                ? new GroupTagInitialSelection(GroupTagChoice.Existing, listed, null, false)
                : new GroupTagInitialSelection(GroupTagChoice.Custom, null, registeredTag, false);
        }

        return preselectCustom
            ? new GroupTagInitialSelection(GroupTagChoice.Custom, null, string.Empty, true)
            : new GroupTagInitialSelection(GroupTagChoice.None, null, null, false);
    }

    /// <summary>
    /// Converts the picker state to the tag sent to Graph.
    /// </summary>
    /// <param name="choice">Selected entry.</param>
    /// <param name="existingTag">Selected list entry.</param>
    /// <param name="customText">Text of the custom box.</param>
    /// <returns>The tag, or <see langword="null"/> for no tag or a blank custom text.</returns>
    public static string? ToRequestedTag(GroupTagChoice choice, string? existingTag, string? customText) => choice switch
    {
        GroupTagChoice.Existing => existingTag,
        GroupTagChoice.Custom => string.IsNullOrWhiteSpace(customText) ? null : customText.Trim(),
        _ => null
    };
}
