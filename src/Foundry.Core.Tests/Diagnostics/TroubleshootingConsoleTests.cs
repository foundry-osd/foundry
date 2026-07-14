// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Models.Configuration;
using Foundry.Core.Services.Diagnostics;

namespace Foundry.Core.Tests.Diagnostics;

public sealed class TroubleshootingConsoleTests
{
    [Fact]
    public void IsShortcut_WhenDisabled_ReturnsFalse()
    {
        TroubleshootingConsoleSettings settings = new()
        {
            IsEnabled = false,
            Key = TroubleshootingConsoleKey.F8
        };

        Assert.False(TroubleshootingConsole.IsShortcut(settings, "F8", false, false, false));
    }

    [Fact]
    public void IsShortcut_WhenTheConfiguredKeyIsPressed_ReturnsTrue()
    {
        TroubleshootingConsoleSettings settings = new()
        {
            IsEnabled = true,
            Key = TroubleshootingConsoleKey.F8
        };

        Assert.True(TroubleshootingConsole.IsShortcut(settings, "F8", false, false, false));
        Assert.False(TroubleshootingConsole.IsShortcut(settings, "F9", false, false, false));
    }

    [Fact]
    public void IsShortcut_WhenNoModifierIsConfigured_RejectsHeldModifiers()
    {
        TroubleshootingConsoleSettings settings = new()
        {
            IsEnabled = true,
            Key = TroubleshootingConsoleKey.F8,
            Modifier = TroubleshootingConsoleModifier.None
        };

        Assert.False(TroubleshootingConsole.IsShortcut(settings, "F8", isControlDown: true, isAltDown: false, isShiftDown: false));
    }

    [Theory]
    [InlineData(TroubleshootingConsoleModifier.Control, true, false, false, true)]
    [InlineData(TroubleshootingConsoleModifier.Control, true, false, true, false)]
    [InlineData(TroubleshootingConsoleModifier.Alt, false, true, false, true)]
    [InlineData(TroubleshootingConsoleModifier.Shift, false, false, true, true)]
    [InlineData(TroubleshootingConsoleModifier.ControlShift, true, false, true, true)]
    [InlineData(TroubleshootingConsoleModifier.ControlShift, true, false, false, false)]
    [InlineData(TroubleshootingConsoleModifier.ControlAlt, true, true, false, true)]
    public void IsShortcut_RequiresAnExactModifierMatch(
        TroubleshootingConsoleModifier modifier,
        bool isControlDown,
        bool isAltDown,
        bool isShiftDown,
        bool expected)
    {
        TroubleshootingConsoleSettings settings = new()
        {
            IsEnabled = true,
            Key = TroubleshootingConsoleKey.F12,
            Modifier = modifier
        };

        Assert.Equal(
            expected,
            TroubleshootingConsole.IsShortcut(settings, "F12", isControlDown, isAltDown, isShiftDown));
    }

    [Fact]
    public void FormatShortcut_IncludesTheModifier()
    {
        Assert.Equal("F8", TroubleshootingConsole.FormatShortcut(new TroubleshootingConsoleSettings()));
        Assert.Equal(
            "Ctrl+Shift+F4",
            TroubleshootingConsole.FormatShortcut(new TroubleshootingConsoleSettings
            {
                Key = TroubleshootingConsoleKey.F4,
                Modifier = TroubleshootingConsoleModifier.ControlShift
            }));
    }
}
