// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Utilities.Runtime;

namespace Foundry.Utilities.Tests.Runtime;

public sealed class RuntimeStartupPolicyTests
{
    [Theory]
    [InlineData(true, false, true)]
    [InlineData(false, true, true)]
    [InlineData(true, true, true)]
    [InlineData(false, false, false)]
    public void CanRun_ReturnsExpectedDecision(bool isWinPe, bool debuggerBypass, bool expected)
    {
        Assert.Equal(expected, RuntimeStartupPolicy.CanRun(isWinPe, debuggerBypass));
    }
}
