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
    public void Parse_RoutesTheUiProcess()
    {
        Assert.Equal(WinPeMode.Ui, WinPeModes.Parse(["--ui"]));
    }

    [Theory]
    [InlineData("--connect")]
    [InlineData("--deploy")]
    public void Parse_TheRemovedPhaseModesAreNoLongerModes(string argument)
    {
        Assert.Equal(WinPeMode.Guardian, WinPeModes.Parse([argument]));
    }

    [Fact]
    public void Parse_RoutesTheNativeWorkerFirst()
    {
        Assert.Equal(WinPeMode.NativeWorker, WinPeModes.Parse(["--ui", "--native-deployment-worker"]));
        Assert.Equal(WinPeMode.NativeWorker, WinPeModes.Parse(["--native-deployment-worker"]));
    }

    [Fact]
    public void Parse_IgnoresUnknownArgumentsAsGuardian()
    {
        Assert.Equal(WinPeMode.Guardian, WinPeModes.Parse(["--version"]));
    }
}
