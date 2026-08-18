// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Models.Configuration;

namespace Foundry.Core.Services.Configuration;

public static class WindowsOptionalFeatureSelectionUpdater
{
    public static WindowsOptionalFeatureSettings ApplySubtreeState(
        WindowsOptionalFeatureSettings settings,
        string featureId,
        bool? enable)
    {
        ArgumentNullException.ThrowIfNull(settings);
        WindowsOptionalFeatureCatalogEntry entry = WindowsOptionalFeatureCatalog.Find(featureId)
            ?? throw new ArgumentException("The Windows optional feature is not part of the curated catalog.", nameof(featureId));
        WindowsOptionalFeatureSettings normalized = WindowsOptionalFeatureSettingsNormalizer.Normalize(settings);
        HashSet<string> enabledIds = normalized.EnabledFeatureIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
        HashSet<string> disabledIds = normalized.DisabledFeatureIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
        WindowsOptionalFeatureCatalogEntry[] subtree =
        [
            entry,
            .. WindowsOptionalFeatureCatalog.GetDescendants(entry.Id)
        ];

        foreach (WindowsOptionalFeatureCatalogEntry subtreeEntry in subtree)
        {
            enabledIds.Remove(subtreeEntry.Id);
            disabledIds.Remove(subtreeEntry.Id);
            if (enable is true)
            {
                enabledIds.Add(subtreeEntry.Id);
            }
            else if (enable is false)
            {
                disabledIds.Add(subtreeEntry.Id);
            }
        }

        if (enable is true)
        {
            disabledIds.ExceptWith(WindowsOptionalFeatureCatalog.GetAncestors(entry.Id).Select(ancestor => ancestor.Id));
        }

        return WindowsOptionalFeatureSettingsNormalizer.Normalize(new WindowsOptionalFeatureSettings
        {
            IsEnabled = normalized.IsEnabled,
            EnabledFeatureIds = enabledIds.ToArray(),
            DisabledFeatureIds = disabledIds.ToArray()
        });
    }
}
