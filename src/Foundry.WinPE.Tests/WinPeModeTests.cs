// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Xunit;

namespace Foundry.WinPE.Tests;

public sealed class WinPeModeTests
{
    [Fact]
    public void Parse_ReturnsGuardianWithoutArguments()
    {
        Assert.Equal(WinPeMode.Guardian, WinPeModes.Parse([]));
    }

    [Fact]
    public void Parse_RoutesConnectAndDeploy()
    {
        Assert.Equal(WinPeMode.Connect, WinPeModes.Parse(["--connect", "--config", "x"]));
        Assert.Equal(WinPeMode.Deploy, WinPeModes.Parse(["--deploy"]));
    }

    [Fact]
    public void Parse_RoutesTheNativeWorkerFirst()
    {
        Assert.Equal(WinPeMode.NativeWorker, WinPeModes.Parse(["--deploy", "--native-deployment-worker"]));
        Assert.Equal(WinPeMode.NativeWorker, WinPeModes.Parse(["--native-deployment-worker"]));
    }

    [Fact]
    public void Parse_IgnoresUnknownArgumentsAsGuardian()
    {
        Assert.Equal(WinPeMode.Guardian, WinPeModes.Parse(["--version"]));
    }

    [Fact]
    public void StripMode_RemovesOnlyTheLeadingMode()
    {
        Assert.Equal(["--config", "x"], WinPeModes.StripMode(["--connect", "--config", "x"]));
    }
}
