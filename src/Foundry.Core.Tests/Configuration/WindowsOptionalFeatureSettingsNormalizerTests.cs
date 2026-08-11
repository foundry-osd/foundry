// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Models.Configuration;
using Foundry.Core.Services.Configuration;

namespace Foundry.Core.Tests.Configuration;

public sealed class WindowsOptionalFeatureSettingsNormalizerTests
{
    [Fact]
    public void Normalize_DisabledSettings_ClearsSelections()
    {
        WindowsOptionalFeatureSettings normalized = WindowsOptionalFeatureSettingsNormalizer.Normalize(new WindowsOptionalFeatureSettings
        {
            EnabledFeatureIds = ["wf:netfx3"],
            DisabledFeatureIds = ["wf:telnetclient"]
        });

        Assert.False(normalized.IsEnabled);
        Assert.Empty(normalized.EnabledFeatureIds);
        Assert.Empty(normalized.DisabledFeatureIds);
    }

    [Fact]
    public void Normalize_CanonicalizesDeduplicatesFiltersAndOrdersIds()
    {
        WindowsOptionalFeatureSettings normalized = WindowsOptionalFeatureSettingsNormalizer.Normalize(new WindowsOptionalFeatureSettings
        {
            IsEnabled = true,
            EnabledFeatureIds = [" WF:TELNETCLIENT ", "wf:netfx3", "wf:telnetclient", "wf:unknown"],
            DisabledFeatureIds = [" WF:TFTP "]
        });

        Assert.Equal(["wf:netfx3", "wf:telnetclient"], normalized.EnabledFeatureIds);
        Assert.Equal(["wf:tftp"], normalized.DisabledFeatureIds);
    }

    [Fact]
    public void Normalize_SameIdInBothLists_MakesFeatureUnchanged()
    {
        WindowsOptionalFeatureSettings normalized = WindowsOptionalFeatureSettingsNormalizer.Normalize(new WindowsOptionalFeatureSettings
        {
            IsEnabled = true,
            EnabledFeatureIds = ["wf:netfx3"],
            DisabledFeatureIds = ["WF:NETFX3"]
        });

        Assert.Empty(normalized.EnabledFeatureIds);
        Assert.Empty(normalized.DisabledFeatureIds);
    }

    [Fact]
    public void Normalize_DisabledAncestor_RemovesEnabledDescendant()
    {
        WindowsOptionalFeatureSettings normalized = WindowsOptionalFeatureSettingsNormalizer.Normalize(new WindowsOptionalFeatureSettings
        {
            IsEnabled = true,
            EnabledFeatureIds = ["wf:microsoft-hyper-v-hypervisor"],
            DisabledFeatureIds = ["wf:microsoft-hyper-v-all"]
        });

        Assert.Empty(normalized.EnabledFeatureIds);
        Assert.Equal(["wf:microsoft-hyper-v-all"], normalized.DisabledFeatureIds);
    }

    [Fact]
    public void Normalize_EnabledAncestorAndDisabledDescendant_PreservesBothActions()
    {
        WindowsOptionalFeatureSettings normalized = WindowsOptionalFeatureSettingsNormalizer.Normalize(new WindowsOptionalFeatureSettings
        {
            IsEnabled = true,
            EnabledFeatureIds = ["wf:microsoft-hyper-v-all"],
            DisabledFeatureIds = ["wf:microsoft-hyper-v-management-powershell"]
        });

        Assert.Equal(["wf:microsoft-hyper-v-all"], normalized.EnabledFeatureIds);
        Assert.Equal(["wf:microsoft-hyper-v-management-powershell"], normalized.DisabledFeatureIds);
    }
}
