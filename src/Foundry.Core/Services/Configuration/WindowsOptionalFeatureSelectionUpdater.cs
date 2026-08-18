// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Models.Configuration;

namespace Foundry.Core.Services.Configuration;

public static class WindowsOptionalFeatureSelectionUpdater
{
    public static IReadOnlyList<string> GetAffectedFeatureIds(IEnumerable<string> featureIds)
    {
        ArgumentNullException.ThrowIfNull(featureIds);
        WindowsOptionalFeatureCatalogEntry[] entries = featureIds
            .Select(featureId => WindowsOptionalFeatureCatalog.Find(featureId)
                ?? throw new ArgumentException(
                    "A Windows optional feature is not part of the curated catalog.",
                    nameof(featureIds)))
            .DistinctBy(entry => entry.Id, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        HashSet<string> entryIds = entries.Select(entry => entry.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        HashSet<string> affectedIds = entries
            .Where(entry => !WindowsOptionalFeatureCatalog.GetAncestors(entry.Id)
                .Any(ancestor => entryIds.Contains(ancestor.Id)))
            .SelectMany(entry => WindowsOptionalFeatureCatalog.GetDescendants(entry.Id).Prepend(entry))
            .Select(entry => entry.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return WindowsOptionalFeatureCatalog.Entries
            .Where(entry => affectedIds.Contains(entry.Id))
            .Select(entry => entry.Id)
            .ToArray();
    }

    public static WindowsOptionalFeatureSettings ApplySubtreeState(
        WindowsOptionalFeatureSettings settings,
        string featureId,
        bool? enable)
        => ApplySubtreeStates(settings, [featureId], enable);

    public static WindowsOptionalFeatureSettings ApplySubtreeStates(
        WindowsOptionalFeatureSettings settings,
        IEnumerable<string> featureIds,
        bool? enable)
    {
        ArgumentNullException.ThrowIfNull(settings);
        IReadOnlyList<string> affectedIds = GetAffectedFeatureIds(featureIds);
        WindowsOptionalFeatureSettings normalized = WindowsOptionalFeatureSettingsNormalizer.Normalize(settings);
        HashSet<string> enabledIds = normalized.EnabledFeatureIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
        HashSet<string> disabledIds = normalized.DisabledFeatureIds.ToHashSet(StringComparer.OrdinalIgnoreCase);

        enabledIds.ExceptWith(affectedIds);
        disabledIds.ExceptWith(affectedIds);
        if (enable is true)
        {
            enabledIds.UnionWith(affectedIds);
            disabledIds.ExceptWith(affectedIds.SelectMany(id => WindowsOptionalFeatureCatalog
                .GetAncestors(id)
                .Select(ancestor => ancestor.Id)));
        }
        else if (enable is false)
        {
            disabledIds.UnionWith(affectedIds);
        }

        return WindowsOptionalFeatureSettingsNormalizer.Normalize(new WindowsOptionalFeatureSettings
        {
            IsEnabled = normalized.IsEnabled,
            EnabledFeatureIds = enabledIds.ToArray(),
            DisabledFeatureIds = disabledIds.ToArray()
        });
    }
}
