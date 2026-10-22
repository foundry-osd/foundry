// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Models.Configuration;
using Foundry.Core.Services.Packages;

namespace Foundry.Core.Services.Configuration;

/// <summary>Validates driver set identity and selection only; the content of each package is checked when it is imported.</summary>
public static class CustomDriverSetsSettingsValidator
{
    /// <summary>Maximum number of driver sets in one profile, included or not.</summary>
    public const int MaximumSets = 256;

    /// <summary>Maximum length of a driver set display name, in characters.</summary>
    public const int MaximumDisplayNameLength = 200;

    /// <summary>Validates profile settings, optionally requiring an included set for media creation.</summary>
    /// <param name="settings">The custom driver set settings to validate.</param>
    /// <param name="requireIncludedSet">Requires an included set when enabled; defaults to false so incomplete profiles can be saved.</param>
    public static IReadOnlyList<string> Validate(CustomDriverSetsSettings settings, bool requireIncludedSet = false)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (settings.Sets is null || settings.Sets.Count > MaximumSets)
            return ["The custom driver set list is invalid or exceeds the set limit."];
        var errors = new List<string>();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var includedHashes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (CustomDriverSetReference set in settings.Sets)
        {
            if (set is null || !IsValidReference(set) || !ids.Add(set.Id) || (set.IsIncluded && !includedHashes.Add(set.Package.ContentHash)))
                errors.Add("A custom driver set reference is invalid or duplicated.");
        }
        if (!settings.IsEnabled || errors.Count > 0) return errors;
        if (requireIncludedSet && !settings.Sets.Any(set => set.IsIncluded))
            errors.Add("At least one custom driver set must be included.");
        if (settings.DefaultSetId is not null && !settings.Sets.Any(set => set.IsIncluded && set.Id == settings.DefaultSetId))
            errors.Add("The default custom driver set must be included.");
        return errors;
    }

    /// <summary>Rejects invalid profile settings, optionally requiring an included set for media creation.</summary>
    /// <param name="settings">The custom driver set settings to validate.</param>
    /// <param name="requireIncludedSet">Requires an included set when enabled; defaults to false so incomplete profiles can be saved.</param>
    public static void ThrowIfInvalid(CustomDriverSetsSettings settings, bool requireIncludedSet = false)
    {
        IReadOnlyList<string> errors = Validate(settings, requireIncludedSet);
        if (errors.Count != 0) throw new InvalidDataException(string.Join(" ", errors));
    }

    /// <summary>Enabled customization requires at least one included set whose package is available; incomplete drafts remain editable.</summary>
    /// <param name="settings">The custom driver set settings to evaluate.</param>
    /// <param name="isAvailable">Tells whether a package is present in the content library.</param>
    public static bool IsReady(CustomDriverSetsSettings settings, Func<ContentPackageReference, bool> isAvailable)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(isAvailable);
        return !settings.IsEnabled || (Validate(settings).Count == 0 && settings.Sets.Any(set => set.IsIncluded) &&
            settings.Sets.Where(set => set.IsIncluded).All(set => isAvailable(set.Package)));
    }

    private static bool IsValidReference(CustomDriverSetReference set) =>
        set.Id is { Length: > 0 and <= 128 } && !set.Id.Any(char.IsControl) &&
        !string.IsNullOrWhiteSpace(set.DisplayName) && set.DisplayName.Length <= MaximumDisplayNameLength && !set.DisplayName.Any(char.IsControl) &&
        set.Package is not null && ContentPackageLibraryService.IsValidReference(set.Package, ContentPackageKind.Drivers);
}
