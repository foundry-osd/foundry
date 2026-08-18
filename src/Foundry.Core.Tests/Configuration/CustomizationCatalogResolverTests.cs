// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Services.Configuration;

namespace Foundry.Core.Tests.Configuration;

public sealed class CustomizationCatalogResolverTests
{
    [Theory]
    [InlineData(ConfigurationNavigationTarget.OperatingSystemSelection, CustomizationCatalog.OperatingSystemSelection)]
    [InlineData(ConfigurationNavigationTarget.WindowsOptionalFeatures, CustomizationCatalog.WindowsOptionalFeatures)]
    [InlineData(ConfigurationNavigationTarget.AppxRemoval, CustomizationCatalog.AppxRemoval)]
    [InlineData(ConfigurationNavigationTarget.MachineNaming, CustomizationCatalog.None)]
    [InlineData(ConfigurationNavigationTarget.Oobe, CustomizationCatalog.None)]
    [InlineData(ConfigurationNavigationTarget.AiComponentRemoval, CustomizationCatalog.None)]
    public void Resolve_ReturnsOnlyCatalogRequiredByPage(
        ConfigurationNavigationTarget target,
        CustomizationCatalog expected)
    {
        Assert.Equal(expected, CustomizationCatalogResolver.Resolve(target));
    }
}
