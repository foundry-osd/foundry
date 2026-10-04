// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Models.Configuration;

namespace Foundry.Core.Tests.Configuration;

public sealed class ConfigurationSchemaVersionsTests
{
    [Fact]
    public void CurrentSchemaVersions_MatchNextPublishedContractVersions()
    {
        Assert.Equal(18, ConfigurationSchemaVersions.FoundryCurrent);
        Assert.Equal(15, ConfigurationSchemaVersions.DeployCurrent);
        Assert.Equal(5, ConfigurationSchemaVersions.ConnectCurrent);
    }

}
