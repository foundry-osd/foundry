// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

namespace Foundry.Core.Services.Configuration;

public enum CustomizationCatalog
{
    None,
    OperatingSystemSelection,
    WindowsOptionalFeatures,
    AppxRemoval
}

public static class CustomizationCatalogResolver
{
    public static CustomizationCatalog Resolve(ConfigurationNavigationTarget target) => target switch
    {
        ConfigurationNavigationTarget.OperatingSystemSelection => CustomizationCatalog.OperatingSystemSelection,
        ConfigurationNavigationTarget.WindowsOptionalFeatures => CustomizationCatalog.WindowsOptionalFeatures,
        ConfigurationNavigationTarget.AppxRemoval => CustomizationCatalog.AppxRemoval,
        _ => CustomizationCatalog.None
    };
}
