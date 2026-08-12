// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Models.Configuration;

namespace Foundry.Core.Services.Configuration;

/// <summary>
/// Normalizes Windows optional feature selections for persistence and deployment.
/// </summary>
public static class WindowsOptionalFeatureSettingsNormalizer
{
    public static WindowsOptionalFeatureSettings Normalize(WindowsOptionalFeatureSettings? settings)
    {
        if (settings is null || !settings.IsEnabled)
        {
            return new WindowsOptionalFeatureSettings();
        }

        HashSet<string> enabled = NormalizeIds(settings.EnabledFeatureIds);
        HashSet<string> disabled = NormalizeIds(settings.DisabledFeatureIds);
        HashSet<string> conflicts = enabled.Intersect(disabled, StringComparer.OrdinalIgnoreCase).ToHashSet(StringComparer.OrdinalIgnoreCase);
        enabled.ExceptWith(conflicts);
        disabled.ExceptWith(conflicts);

        enabled.RemoveWhere(id => WindowsOptionalFeatureCatalog.GetAncestors(id).Any(ancestor => disabled.Contains(ancestor.Id)));

        return new WindowsOptionalFeatureSettings
        {
            IsEnabled = true,
            EnabledFeatureIds = WindowsOptionalFeatureCatalog.Entries.Where(entry => enabled.Contains(entry.Id)).Select(entry => entry.Id).ToArray(),
            DisabledFeatureIds = WindowsOptionalFeatureCatalog.Entries.Where(entry => disabled.Contains(entry.Id)).Select(entry => entry.Id).ToArray()
        };
    }

    private static HashSet<string> NormalizeIds(IEnumerable<string>? ids)
    {
        if (ids is null)
        {
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        return ids
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => WindowsOptionalFeatureCatalog.Find(id))
            .Where(entry => entry is not null)
            .Select(entry => entry!.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }
}
