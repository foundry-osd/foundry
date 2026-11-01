// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.AutopilotInteractive.Tests.Support;

namespace Foundry.AutopilotInteractive.Tests.ViewModels;

public sealed class MainWindowViewModelKeyboardTests
{
    [Fact]
    public void ApplyInheritedKeyboardLayout_WhenAvailable_AppliesItAndUpdatesTheMenu()
    {
        using AutopilotHarness harness = new(keyboardLayoutId: "0000080c");

        harness.ViewModel.ApplyInheritedKeyboardLayout();

        Assert.Equal(["0000080C"], harness.Keyboard.AppliedLayoutIds);
        Assert.Equal(harness.Localization.Format("Menu.KeyboardFormat", "BE"), harness.ViewModel.KeyboardMenuHeader);
        Assert.Contains(harness.ViewModel.KeyboardMenuEntries, entry => entry.LayoutId == "0000080C" && entry.IsSelected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("00000809")]
    [InlineData("not-a-klid")]
    public void ApplyInheritedKeyboardLayout_WhenMissingOrUnavailable_KeepsTheWindowsLayout(string? keyboardLayoutId)
    {
        using AutopilotHarness harness = new(keyboardLayoutId: keyboardLayoutId);

        harness.ViewModel.ApplyInheritedKeyboardLayout();

        Assert.Empty(harness.Keyboard.AppliedLayoutIds);
        Assert.Equal("0000040C", harness.Keyboard.GetActiveLayoutId());
    }

    [Fact]
    public void ApplyInheritedKeyboardLayout_WhenAlreadyActive_DoesNotSwitch()
    {
        using AutopilotHarness harness = new(keyboardLayoutId: "0000040C");

        harness.ViewModel.ApplyInheritedKeyboardLayout();

        Assert.Empty(harness.Keyboard.AppliedLayoutIds);
    }

    [Fact]
    public void KeyboardMenu_ListsActiveLanguageThenOtherLanguages()
    {
        using AutopilotHarness harness = new();

        Assert.Equal(harness.Localization.Format("Menu.KeyboardFormat", "FR"), harness.ViewModel.KeyboardMenuHeader);
        Assert.Equal(
            ["0000080C", "0000040C", null, null],
            harness.ViewModel.KeyboardMenuEntries.Select(entry => entry.LayoutId));
        Assert.True(harness.ViewModel.KeyboardMenuEntries[2].IsSeparator);
        Assert.Equal(harness.Text("Keyboard.OtherLanguages"), harness.ViewModel.KeyboardMenuEntries[3].Header);
    }

    [Fact]
    public void SetKeyboardLayout_AppliesAnAvailableLayoutOnly()
    {
        using AutopilotHarness harness = new();

        harness.ViewModel.SetKeyboardLayoutCommand.Execute("00000809");
        harness.ViewModel.SetKeyboardLayoutCommand.Execute("00000413");

        Assert.Equal(["00000413"], harness.Keyboard.AppliedLayoutIds);
        Assert.Equal(harness.Localization.Format("Menu.KeyboardFormat", "NL"), harness.ViewModel.KeyboardMenuHeader);
    }
}
