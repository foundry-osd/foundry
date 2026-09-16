// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Deploy.Services.Deployment;

namespace Foundry.Deploy.Tests;

public sealed class DeploymentCapacityPolicyTests
{
    [Theory]
    [InlineData(false, 1073741884L)]
    [InlineData(true, 1073741984L)]
    public void RequiredWindowsBytes_IncludesKnownSimultaneousResidents(bool needsSetup, long expected)
    {
        var image = new WindowsImageMetadata(1, "Professional", 10, 100);
        Assert.Equal(expected, DeploymentCapacityPolicy.RequiredWindowsBytes(image, 20, 30, needsSetup));
    }

    [Fact]
    public void RequiredWindowsBytes_UnknownExpansion_DoesNotInventAnInstalledSize()
    {
        Assert.Equal(1073741874L, DeploymentCapacityPolicy.RequiredWindowsBytes(null, 20, 30, true));
    }

    [Fact]
    public void RequiredWindowsBytes_Overflow_RejectsMetadata()
    {
        Assert.Throws<OverflowException>(() => DeploymentCapacityPolicy.RequiredWindowsBytes(new WindowsImageMetadata(1, "Professional", long.MaxValue), 1, 0, false));
    }
}
