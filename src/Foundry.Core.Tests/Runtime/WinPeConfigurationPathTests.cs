// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Services.Runtime;

namespace Foundry.Core.Tests.Runtime;

public sealed class WinPeConfigurationPathTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void Resolve_WithoutOverride_IsTheDefaultPath(string? overridePath) =>
        Assert.Equal(new WinPeConfigurationLocation(@"X:\Foundry\Config\foundry.winpe.config.json", false),
            WinPeConfigurationPath.Resolve(WinPeWorkspaceLayout.ConfigurationPath, overridePath));

    [Fact]
    public void Resolve_WithOverride_IsTheOverride() =>
        Assert.Equal(new WinPeConfigurationLocation(@"D:\dev\foundry.winpe.config.json", true),
            WinPeConfigurationPath.Resolve(WinPeWorkspaceLayout.ConfigurationPath, @"D:\dev\foundry.winpe.config.json"));
}
