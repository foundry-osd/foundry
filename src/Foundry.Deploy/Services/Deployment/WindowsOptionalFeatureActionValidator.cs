// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Models.Configuration;
using Foundry.Deploy.Models.Configuration;

namespace Foundry.Deploy.Services.Deployment;

internal static class WindowsOptionalFeatureActionValidator
{
    public static bool TryNormalize(
        DeployWindowsOptionalFeatureSettings settings,
        out DeployWindowsOptionalFeatureSettings normalized,
        out string error)
    {
        ArgumentNullException.ThrowIfNull(settings);

        if (!settings.IsEnabled)
        {
            normalized = new DeployWindowsOptionalFeatureSettings();
            error = string.Empty;
            return true;
        }

        var actionsById = new Dictionary<string, DeployWindowsOptionalFeatureAction>(StringComparer.OrdinalIgnoreCase);
        foreach (DeployWindowsOptionalFeatureAction action in settings.Actions ?? [])
        {
            WindowsOptionalFeatureCatalogEntry? entry = WindowsOptionalFeatureCatalog.Find(action.Id);
            if (entry is null)
            {
                normalized = new DeployWindowsOptionalFeatureSettings();
                error = "Windows optional feature action contains an unknown or blank identifier.";
                return false;
            }

            if (!actionsById.TryAdd(
                entry.Id,
                new DeployWindowsOptionalFeatureAction { Id = entry.Id, Enable = action.Enable }))
            {
                normalized = new DeployWindowsOptionalFeatureSettings();
                error = $"Windows optional feature action '{entry.Id}' is duplicated.";
                return false;
            }
        }

        foreach (DeployWindowsOptionalFeatureAction action in actionsById.Values.Where(action => action.Enable))
        {
            if (WindowsOptionalFeatureCatalog
                .GetAncestors(action.Id)
                .Any(ancestor => actionsById.TryGetValue(ancestor.Id, out DeployWindowsOptionalFeatureAction? ancestorAction) &&
                    !ancestorAction.Enable))
            {
                normalized = new DeployWindowsOptionalFeatureSettings();
                error = $"Windows optional feature action '{action.Id}' enables a descendant of a disabled feature.";
                return false;
            }
        }

        normalized = new DeployWindowsOptionalFeatureSettings
        {
            IsEnabled = true,
            Actions = WindowsOptionalFeatureCatalog.Entries
                .Where(entry => actionsById.ContainsKey(entry.Id))
                .Select(entry => actionsById[entry.Id])
                .ToArray()
        };
        error = string.Empty;
        return true;
    }
}
