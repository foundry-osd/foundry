// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Models.Configuration;
using Foundry.Core.Services.Configuration;

namespace Foundry.Core.Tests.Configuration;

public sealed class WindowsOptionalFeatureSelectionUpdaterTests
{
    [Fact]
    public void ApplyFeatureState_EnableParent_EnablesEntireSubtree()
    {
        WindowsOptionalFeatureSettings updated = WindowsOptionalFeatureSelectionUpdater.ApplySubtreeState(
            new WindowsOptionalFeatureSettings { IsEnabled = true },
            "wf:multipoint-connector",
            enable: true);

        Assert.Equal(
            ["wf:multipoint-connector", "wf:multipoint-connector-services", "wf:multipoint-tools"],
            updated.EnabledFeatureIds);
        Assert.Empty(updated.DisabledFeatureIds);
    }

    [Fact]
    public void ApplyFeatureState_DisableParent_DisablesEntireSubtree()
    {
        WindowsOptionalFeatureSettings updated = WindowsOptionalFeatureSelectionUpdater.ApplySubtreeState(
            new WindowsOptionalFeatureSettings
            {
                IsEnabled = true,
                EnabledFeatureIds = ["wf:multipoint-connector-services", "wf:multipoint-tools"]
            },
            "wf:multipoint-connector",
            enable: false);

        Assert.Empty(updated.EnabledFeatureIds);
        Assert.Equal(
            ["wf:multipoint-connector", "wf:multipoint-connector-services", "wf:multipoint-tools"],
            updated.DisabledFeatureIds);
    }

    [Fact]
    public void ApplyFeatureState_ClearParent_ClearsSubtreeAndPreservesUnrelatedSelection()
    {
        WindowsOptionalFeatureSettings updated = WindowsOptionalFeatureSelectionUpdater.ApplySubtreeState(
            new WindowsOptionalFeatureSettings
            {
                IsEnabled = true,
                EnabledFeatureIds = ["wf:multipoint-connector", "wf:multipoint-tools", "wf:telnetclient"],
                DisabledFeatureIds = ["wf:multipoint-connector-services"]
            },
            "wf:multipoint-connector",
            enable: null);

        Assert.Equal(["wf:telnetclient"], updated.EnabledFeatureIds);
        Assert.Empty(updated.DisabledFeatureIds);
    }

    [Fact]
    public void ApplyFeatureState_EnableChild_ClearsDisabledAncestorAndPreservesSibling()
    {
        WindowsOptionalFeatureSettings updated = WindowsOptionalFeatureSelectionUpdater.ApplySubtreeState(
            new WindowsOptionalFeatureSettings
            {
                IsEnabled = true,
                DisabledFeatureIds = ["wf:multipoint-connector", "wf:multipoint-tools"]
            },
            "wf:multipoint-connector-services",
            enable: true);

        Assert.Equal(["wf:multipoint-connector-services"], updated.EnabledFeatureIds);
        Assert.Equal(["wf:multipoint-tools"], updated.DisabledFeatureIds);
    }
}
