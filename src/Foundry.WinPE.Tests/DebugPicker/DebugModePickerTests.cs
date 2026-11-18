// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.WinPE.DebugPicker;
using Xunit;

namespace Foundry.WinPE.Tests.DebugPicker;

public sealed class DebugModePickerTests
{
    [Fact]
    public void ShouldShow_OnlyWithDebugSafetyAndNoArgument()
    {
        Assert.True(DebugModePicker.ShouldShow(debugSafetyEnabled: true, []));
        Assert.False(DebugModePicker.ShouldShow(debugSafetyEnabled: false, []));
    }

    [Theory]
    [InlineData("--ui")]
    [InlineData("--native-deployment-worker")]
    [InlineData("--version")]
    public void ShouldShow_NeverWithAnArgument(string argument)
    {
        Assert.False(DebugModePicker.ShouldShow(debugSafetyEnabled: true, [argument]));
        Assert.False(DebugModePicker.ShouldShow(debugSafetyEnabled: false, [argument]));
    }

    [Theory]
    [InlineData((int)DebugModeChoice.ConnectThenDeploy, "ui:connect", 7)]
    [InlineData((int)DebugModeChoice.DeployOnly, "ui:skip-connect", 7)]
    [InlineData((int)DebugModeChoice.ObsoleteMedia, "obsolete", 20)]
    [InlineData((int)DebugModeChoice.CrashScreenRelaunchAllowed, "crash:allowed", 0)]
    [InlineData((int)DebugModeChoice.CrashScreenRelaunchRefused, "crash:refused", 0)]
    [InlineData((int)DebugModeChoice.None, null, 0)]
    public void Run_StartsTheChosenMode(int choice, string? expectedCall, int expectedExitCode)
    {
        var calls = new List<string>();

        int exitCode = DebugModePicker.Run((DebugModeChoice)choice,
            skipConnect =>
            {
                calls.Add(skipConnect ? "ui:skip-connect" : "ui:connect");
                return 7;
            },
            () =>
            {
                calls.Add("obsolete");
                return 20;
            },
            relaunchAllowed => calls.Add(relaunchAllowed ? "crash:allowed" : "crash:refused"));

        Assert.Equal(expectedExitCode, exitCode);
        Assert.Equal(expectedCall is null ? [] : [expectedCall], calls);
    }
}
