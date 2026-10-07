// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Core.Services.WinPe;

namespace Foundry.Core.Tests.WinPe;

public sealed class WinPeUsbDiskCandidateSelectorTests
{
    private static readonly WinPeUsbDiskCandidate Selected = new()
    {
        DiskNumber = 3,
        FriendlyName = "Safe USB",
        SerialNumber = "SERIAL-3",
        UniqueId = "UNIQUE-3",
        BusType = "USB",
        SizeBytes = 64_000_000_000
    };

    private static readonly WinPeUsbDiskCandidate Other = Selected with
    {
        DiskNumber = 4,
        SerialNumber = "SERIAL-4",
        UniqueId = "UNIQUE-4"
    };

    [Fact]
    public void Reselect_WhenDiskWasRenumbered_KeepsSelectionByUniqueId()
    {
        WinPeUsbDiskCandidate refreshed = Selected with { DiskNumber = 5, IsFoundryMedia = true };

        WinPeUsbDiskCandidate? result = WinPeUsbDiskCandidateSelector.Reselect([Other, refreshed], Selected);

        Assert.Same(refreshed, result);
    }

    [Fact]
    public void Reselect_WhenUniqueIdChanged_FallsBackToSerialNumber()
    {
        WinPeUsbDiskCandidate refreshed = Selected with { UniqueId = "UNIQUE-NEW", SerialNumber = " serial-3 " };

        WinPeUsbDiskCandidate? result = WinPeUsbDiskCandidateSelector.Reselect([Other, refreshed], Selected);

        Assert.Same(refreshed, result);
    }

    [Fact]
    public void Reselect_WhenIdentifiersAreMissing_UsesDiskNumber()
    {
        WinPeUsbDiskCandidate anonymous = Selected with { UniqueId = "", SerialNumber = "" };
        WinPeUsbDiskCandidate refreshed = anonymous with { IsFoundryMedia = true };

        WinPeUsbDiskCandidate? result = WinPeUsbDiskCandidateSelector.Reselect([Other, refreshed], anonymous);

        Assert.Same(refreshed, result);
    }

    [Fact]
    public void Reselect_WhenIdentifiersDoNotMatch_DoesNotFallBackToDiskNumber()
    {
        WinPeUsbDiskCandidate replacement = Other with { DiskNumber = Selected.DiskNumber };
        WinPeUsbDiskCandidate unrelated = Other with { DiskNumber = 6, UniqueId = "UNIQUE-6", SerialNumber = "SERIAL-6" };

        WinPeUsbDiskCandidate? result = WinPeUsbDiskCandidateSelector.Reselect([replacement, unrelated], Selected);

        Assert.Null(result);
    }

    [Fact]
    public void Reselect_WhenUniqueIdIsAmbiguous_DoesNotSelectEither()
    {
        WinPeUsbDiskCandidate duplicate = Selected with { DiskNumber = 4, SerialNumber = "SERIAL-4" };
        WinPeUsbDiskCandidate original = Selected with { SerialNumber = "SERIAL-5" };

        WinPeUsbDiskCandidate? result = WinPeUsbDiskCandidateSelector.Reselect([original, duplicate], Selected);

        Assert.Null(result);
    }

    [Fact]
    public void Reselect_WhenSelectionCannotBeFoundAndOneCandidateRemains_SelectsIt()
    {
        WinPeUsbDiskCandidate? result = WinPeUsbDiskCandidateSelector.Reselect([Other], Selected);

        Assert.Same(Other, result);
    }

    [Fact]
    public void Reselect_WithoutPreviousSelection_SelectsFirstCandidate()
    {
        WinPeUsbDiskCandidate? result = WinPeUsbDiskCandidateSelector.Reselect([Other, Selected], previous: null);

        Assert.Same(Other, result);
    }
}
