// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Models.Configuration;
using Foundry.Core.Models.Configuration.Deploy;

namespace Foundry.Core.Tests.Configuration;

public sealed class ConfigurationSchemaVersionsTests
{
    [Fact]
    public void CurrentVersions_MatchConfigurationDocumentContracts()
    {
        Assert.Equal(FoundryConfigurationDocument.CurrentSchemaVersion, ConfigurationSchemaVersions.FoundryCurrent);
        Assert.Equal(FoundryConnectConfigurationDocument.CurrentSchemaVersion, ConfigurationSchemaVersions.ConnectCurrent);
        Assert.Equal(FoundryDeployConfigurationDocument.CurrentSchemaVersion, ConfigurationSchemaVersions.DeployCurrent);
    }

    [Fact]
    public void IsBootMediaUpdateRecommended_UsesCurrentSchemaVersion()
    {
        Assert.False(ConfigurationSchemaVersions.IsBootMediaUpdateRecommended(4, 3));
        Assert.False(ConfigurationSchemaVersions.IsBootMediaUpdateRecommended(3, 3));
        Assert.True(ConfigurationSchemaVersions.IsBootMediaUpdateRecommended(2, 3));
    }
}
