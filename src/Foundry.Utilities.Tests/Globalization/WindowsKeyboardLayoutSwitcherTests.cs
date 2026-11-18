// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Utilities.Globalization;

namespace Foundry.Utilities.Tests.Globalization;

public sealed class WindowsKeyboardLayoutSwitcherTests
{
    private static readonly KeyboardLayout BelgianFrench = new("0000080C", "fr", 0x080C, "KBDBE.DLL", "Belgian French");

    [Fact]
    public void Apply_InWinPe_SetsTheSessionLayoutForLaterProcesses()
    {
        string activeLayoutId = "0000040C";
        var sessionLocales = new List<string>();
        var switcher = new WindowsKeyboardLayoutSwitcher(
            loadLayouts: () => [BelgianFrench],
            activateLayout: id => { activeLayoutId = id; return true; },
            getActiveLayoutId: () => activeLayoutId,
            isWinPe: () => true,
            setWinPeSessionLayout: locale =>
            {
                sessionLocales.Add(locale);
                return null;
            });

        KeyboardLayoutSwitchResult result = switcher.Apply(BelgianFrench);

        Assert.True(result.Succeeded);
        Assert.Null(result.Error);
        Assert.Equal([BelgianFrench.InputLocale], sessionLocales);
    }

    [Fact]
    public void Apply_WhenWpeUtilFails_StillSucceedsWithAWarning()
    {
        string activeLayoutId = "0000040C";
        var switcher = new WindowsKeyboardLayoutSwitcher(
            loadLayouts: () => [BelgianFrench],
            activateLayout: id => { activeLayoutId = id; return true; },
            getActiveLayoutId: () => activeLayoutId,
            isWinPe: () => true,
            setWinPeSessionLayout: _ => "wpeutil.exe SetKeyboardLayout failed with exit code 5.");

        KeyboardLayoutSwitchResult result = switcher.Apply(BelgianFrench);

        Assert.True(result.Succeeded);
        Assert.Contains("exit code 5", result.Error);
    }

    [Fact]
    public void Apply_WhenLayoutDoesNotBecomeActive_FailsWithoutChangingTheSession()
    {
        int wpeUtilCalls = 0;
        var switcher = new WindowsKeyboardLayoutSwitcher(
            loadLayouts: () => [BelgianFrench],
            activateLayout: _ => true,
            getActiveLayoutId: () => "0000040C",
            isWinPe: () => true,
            setWinPeSessionLayout: _ =>
            {
                wpeUtilCalls++;
                return null;
            });

        KeyboardLayoutSwitchResult result = switcher.Apply(BelgianFrench);

        Assert.False(result.Succeeded);
        Assert.Equal(0, wpeUtilCalls);
    }

    [Fact]
    public void Apply_OutsideWinPe_SwitchesOnlyTheProcess()
    {
        string activeLayoutId = "0000040C";
        int wpeUtilCalls = 0;
        var switcher = new WindowsKeyboardLayoutSwitcher(
            loadLayouts: () => [BelgianFrench],
            activateLayout: id => { activeLayoutId = id; return true; },
            getActiveLayoutId: () => activeLayoutId,
            isWinPe: () => false,
            setWinPeSessionLayout: _ =>
            {
                wpeUtilCalls++;
                return null;
            });

        KeyboardLayoutSwitchResult result = switcher.Apply(BelgianFrench);

        Assert.True(result.Succeeded);
        Assert.Null(result.Error);
        Assert.Equal(0, wpeUtilCalls);
    }
}
