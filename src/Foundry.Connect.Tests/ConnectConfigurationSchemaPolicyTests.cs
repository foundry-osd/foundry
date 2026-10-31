// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Connect.Configuration;
using Foundry.Core.Models.Configuration;

namespace Foundry.Connect.Tests;

public sealed class ConnectConfigurationSchemaPolicyTests
{
    [Theory]
    [InlineData(0, ConnectSchemaDecision.Invalid)]
    [InlineData(-1, ConnectSchemaDecision.Invalid)]
    [InlineData(FoundryConnectConfigurationDocument.CurrentSchemaVersion - 1, ConnectSchemaDecision.Older)]
    [InlineData(FoundryConnectConfigurationDocument.CurrentSchemaVersion, ConnectSchemaDecision.Current)]
    [InlineData(FoundryConnectConfigurationDocument.CurrentSchemaVersion + 1, ConnectSchemaDecision.Newer)]
    public void Evaluate_ClassifiesSchemaVersion(int version, ConnectSchemaDecision expected)
        => Assert.Equal(expected, ConnectConfigurationSchemaPolicy.Evaluate(version));
}
