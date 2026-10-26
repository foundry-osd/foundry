// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Models.Configuration;
using Foundry.Core.Services.Configuration;

namespace Foundry.Core.Tests.Configuration;

public sealed class ConfigurationSectionsTests
{
    [Fact]
    public void Changed_EqualConfigurations_ReturnsNone()
    {
        Assert.Empty(ConfigurationSections.Changed(new FoundryConfigurationDocument(), new FoundryConfigurationDocument()));
    }

    [Fact]
    public void Changed_ReturnsTheTopLevelSectionsThatDiffer_InNameOrder()
    {
        var previous = new FoundryConfigurationDocument();
        FoundryConfigurationDocument next = previous with
        {
            Network = new NetworkSettings { Wifi = new WifiSettings { Ssid = "Office" } },
            Customization = new CustomizationSettings { Oobe = new OobeSettings { IsEnabled = true } }
        };

        Assert.Equal(["customization", "network"], ConfigurationSections.Changed(previous, next));
    }
}
