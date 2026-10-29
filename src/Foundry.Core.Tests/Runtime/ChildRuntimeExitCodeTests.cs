// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Models.Runtime;

namespace Foundry.Core.Tests.Runtime;

public sealed class ChildRuntimeExitCodeTests
{
    [Fact]
    public void Values_AreStable()
    {
        Assert.Equal(0, (int)ChildRuntimeExitCode.Success);
        Assert.Equal(20, (int)ChildRuntimeExitCode.UserAborted);
        Assert.Equal(21, (int)ChildRuntimeExitCode.StartupFailure);
        Assert.Equal(22, (int)ChildRuntimeExitCode.ConfigurationFailure);
        Assert.Equal(23, (int)ChildRuntimeExitCode.RuntimeFailure);
    }
}
