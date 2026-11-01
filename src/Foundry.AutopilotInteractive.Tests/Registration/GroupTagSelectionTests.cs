// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.AutopilotInteractive.Services.Registration;
using Foundry.Core.Services.Autopilot;

namespace Foundry.AutopilotInteractive.Tests.Registration;

public sealed class GroupTagSelectionTests
{
    private static readonly AutopilotDeviceLookup NotFound = new(AutopilotDeviceLookupStatus.NotFound, null);
    private static readonly AutopilotDeviceLookup Ambiguous = new(AutopilotDeviceLookupStatus.Ambiguous, null);

    private static AutopilotDeviceLookup Found(string? groupTag) =>
        new(AutopilotDeviceLookupStatus.Found, new AutopilotRegisteredDevice("id", groupTag, null, null));

    [Fact]
    public void Resolve_PreselectsListEntry_ForRegisteredDeviceTag_IgnoringCaseAndSpaces()
    {
        GroupTagInitialSelection selection = GroupTagSelection.Resolve(["Sales"], Found(" sales "), preselectCustom: false);

        Assert.Equal(new GroupTagInitialSelection(GroupTagChoice.Existing, "Sales", null, false), selection);
    }

    [Fact]
    public void Resolve_UsesCustom_WhenRegisteredTagIsNotListed()
    {
        GroupTagInitialSelection selection = GroupTagSelection.Resolve(["Sales"], Found(" Lab "), preselectCustom: false);

        Assert.Equal(new GroupTagInitialSelection(GroupTagChoice.Custom, null, "Lab", false), selection);
    }

    [Fact]
    public void Resolve_UsesNone_WhenRegisteredTagIsBlank()
    {
        GroupTagInitialSelection selection = GroupTagSelection.Resolve(["Sales"], Found("  "), preselectCustom: true);

        Assert.Equal(GroupTagChoice.None, selection.Choice);
        Assert.False(selection.FocusCustom);
    }

    [Fact]
    public void Resolve_IgnoresPreselectCustom_ForRegisteredDevice()
    {
        GroupTagInitialSelection selection = GroupTagSelection.Resolve(["Sales"], Found("Sales"), preselectCustom: true);

        Assert.Equal(GroupTagChoice.Existing, selection.Choice);
        Assert.False(selection.FocusCustom);
    }

    [Fact]
    public void Resolve_PreselectsCustomWithFocus_ForNewDevice_WhenOptionOn()
    {
        GroupTagInitialSelection selection = GroupTagSelection.Resolve(["Sales"], NotFound, preselectCustom: true);

        Assert.Equal(new GroupTagInitialSelection(GroupTagChoice.Custom, null, string.Empty, true), selection);
    }

    [Fact]
    public void Resolve_UsesNone_ForAmbiguousDevice_WhenOptionOff()
    {
        GroupTagInitialSelection selection = GroupTagSelection.Resolve(["Sales"], Ambiguous, preselectCustom: false);

        Assert.Equal(new GroupTagInitialSelection(GroupTagChoice.None, null, null, false), selection);
    }

    [Fact]
    public void ToRequestedTag_ReturnsNull_ForBlankCustom()
    {
        Assert.Null(GroupTagSelection.ToRequestedTag(GroupTagChoice.Custom, null, "   "));
        Assert.Null(GroupTagSelection.ToRequestedTag(GroupTagChoice.None, "Sales", "Lab"));
    }

    [Fact]
    public void ToRequestedTag_TrimsCustom()
    {
        Assert.Equal("Lab", GroupTagSelection.ToRequestedTag(GroupTagChoice.Custom, null, " Lab "));
        Assert.Equal("Sales", GroupTagSelection.ToRequestedTag(GroupTagChoice.Existing, "Sales", null));
    }
}
