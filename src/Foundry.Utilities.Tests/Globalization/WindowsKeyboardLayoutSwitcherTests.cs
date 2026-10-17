// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Utilities.Globalization;

namespace Foundry.Utilities.Tests.Globalization;

public sealed class WindowsKeyboardLayoutSwitcherTests
{
    private static readonly KeyboardLayout BelgianFrench = new("0000080C", "fr", 0x080C, "KBDBE.DLL", "Belgian French");

    [Fact]
    public void Apply_WhenWpeUtilFails_StillHandsOffAndReportsWarning()
    {
        string path = CreateHandoffPath();
        string activeLayoutId = "0000040C";
        var switcher = new WindowsKeyboardLayoutSwitcher(
            path,
            loadLayouts: () => [BelgianFrench],
            activateLayout: id => { activeLayoutId = id; return true; },
            getActiveLayoutId: () => activeLayoutId,
            isWinPe: () => true,
            setWinPeSessionLayout: _ => "wpeutil.exe SetKeyboardLayout failed with exit code 5.");

        try
        {
            KeyboardLayoutSwitchResult result = switcher.Apply(BelgianFrench);

            Assert.True(result.Succeeded);
            Assert.Contains("exit code 5", result.Error);
            Assert.True(KeyboardLayoutHandoff.TryRead(path, out string handedOff));
            Assert.Equal("0000080C", handedOff);
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(path)!, recursive: true);
        }
    }

    [Fact]
    public void Apply_WhenLayoutDoesNotBecomeActive_FailsWithoutHandoff()
    {
        string path = CreateHandoffPath();
        var switcher = new WindowsKeyboardLayoutSwitcher(
            path,
            loadLayouts: () => [BelgianFrench],
            activateLayout: _ => true,
            getActiveLayoutId: () => "0000040C",
            isWinPe: () => true,
            setWinPeSessionLayout: _ => null);

        try
        {
            KeyboardLayoutSwitchResult result = switcher.Apply(BelgianFrench);

            Assert.False(result.Succeeded);
            Assert.False(File.Exists(path));
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(path)!, recursive: true);
        }
    }

    [Fact]
    public void RestoreHandoff_WhenLayoutIsAlreadyActive_DoesNotRunWpeUtil()
    {
        string path = CreateHandoffPath();
        KeyboardLayoutHandoff.Write(path, BelgianFrench.Id);
        int wpeUtilCalls = 0;
        var switcher = new WindowsKeyboardLayoutSwitcher(
            path,
            loadLayouts: () => [BelgianFrench],
            activateLayout: _ => true,
            getActiveLayoutId: () => BelgianFrench.Id,
            isWinPe: () => true,
            setWinPeSessionLayout: _ =>
            {
                wpeUtilCalls++;
                return null;
            });

        try
        {
            KeyboardLayoutSwitchResult? result = switcher.RestoreHandoff();

            Assert.NotNull(result);
            Assert.True(result.Succeeded);
            Assert.Equal(0, wpeUtilCalls);
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(path)!, recursive: true);
        }
    }

    private static string CreateHandoffPath()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"foundry-keyboard-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        return Path.Combine(directory, "keyboard-layout.txt");
    }
}
