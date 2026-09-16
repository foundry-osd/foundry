// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Utilities.Storage;

namespace Foundry.Utilities.Tests.Storage;

public sealed class DiskIdentityTests
{
    public static TheoryData<string, DiskIdentity, DiskIdentity[], bool> ResolutionCases
    {
        get
        {
            var expected = new DiskIdentity(3, "device-3", "serial-3", "Storage device", "USB", 64_000_000_000);
            DiskIdentity serialOnly = expected with { UniqueId = "" };
            DiskIdentity unusual = expected with
            {
                UniqueId = "périphérique-'\"; $(throw 'injected')",
                SerialNumber = " série-3 ",
                FriendlyName = "Lecteur d'Émilie"
            };

            return new()
            {
                { "unchanged", expected, [expected], true },
                { "UID only", expected with { SerialNumber = "" }, [expected with { SerialNumber = "" }], true },
                { "serial only", serialOnly, [serialOnly], true },
                { "new UID does not invalidate captured serial", serialOnly, [expected], true },
                { "case and whitespace", expected, [expected with { UniqueId = " DEVICE-3 ", SerialNumber = " SERIAL-3\t", FriendlyName = " storage DEVICE ", BusType = "usb" }], true },
                { "quotes and non-ASCII are data", unusual, [unusual], true },
                { "non-ASCII casing", unusual, [unusual with { FriendlyName = "LECTEUR D'ÉMILIE", SerialNumber = "SÉRIE-3" }], true },
                { "duplicate serial with unique UID", expected, [expected, expected with { Number = 4, UniqueId = "device-4" }], true },
                { "unrelated missing identities", expected, [expected, expected with { UniqueId = "", SerialNumber = "" }], true },
                { "empty inventory", expected, [], false },
                { "missing captured identity", expected with { UniqueId = " ", SerialNumber = "\t" }, [expected], false },
                { "negative captured number", expected with { Number = -1 }, [expected with { Number = -1 }], false },
                { "zero captured capacity", expected with { SizeBytes = 0 }, [expected with { SizeBytes = 0 }], false },
                { "same-number replacement", expected, [expected with { UniqueId = "device-4", SerialNumber = "serial-4" }], false },
                { "missing current UID cannot downgrade", expected, [serialOnly], false },
                { "changed current UID cannot downgrade", expected, [expected with { UniqueId = "device-4" }], false },
                { "missing captured serial", expected, [expected with { SerialNumber = "" }], false },
                { "changed captured serial", expected, [expected with { SerialNumber = "serial-4" }], false },
                { "missing serial-only identity", serialOnly, [expected with { SerialNumber = "" }], false },
                { "duplicate UID before number filtering", expected, [expected, expected with { Number = 4 }], false },
                { "duplicate UID before fingerprint filtering", expected, [expected, expected with { Number = 4, SerialNumber = "other", SizeBytes = 1, FriendlyName = "other", BusType = "SATA" }], false },
                { "duplicate UID includes unusable candidate", expected, [expected, expected with { Number = -1, SizeBytes = 0 }], false },
                { "normalized duplicate UID", expected, [expected, expected with { Number = 4, UniqueId = " DEVICE-3 " }], false },
                { "duplicate serial-only identity", serialOnly, [serialOnly, serialOnly with { Number = 4 }], false },
                { "duplicate serial cannot upgrade to new UID", serialOnly, [expected, expected with { Number = 4, UniqueId = "device-4" }], false },
                { "identifier field collision is not a match", expected, [expected with { UniqueId = "serial-3", SerialNumber = "device-3" }], false },
                { "renumbered", expected, [expected with { Number = 4 }], false },
                { "resized", expected, [expected with { SizeBytes = 64_000_000_001 }], false },
                { "renamed", expected, [expected with { FriendlyName = "Other device" }], false },
                { "changed bus", expected, [expected with { BusType = "SATA" }], false },
                { "missing name", expected, [expected with { FriendlyName = "" }], false },
                { "missing bus", expected, [expected with { BusType = "" }], false }
            };
        }
    }

    [Theory]
    [MemberData(nameof(ResolutionCases))]
    public void Resolve_RequiresOneUnchangedIdentity(
        string scenario,
        DiskIdentity expected,
        DiskIdentity[] snapshots,
        bool shouldResolve)
    {
        DiskIdentity? resolved = expected.Resolve(snapshots);

        Assert.True(shouldResolve == (resolved is not null), scenario);
        if (resolved is not null)
        {
            Assert.Same(snapshots[0], resolved);
            Assert.True(expected.Matches(resolved), scenario);
        }
    }

    [Theory]
    [InlineData(-1, "uid", "serial", 1UL, false)]
    [InlineData(0, "uid", "serial", 0UL, false)]
    [InlineData(0, " ", "\t", 1UL, false)]
    [InlineData(0, " uid ", "", 1UL, true)]
    [InlineData(0, "", " serial ", 1UL, true)]
    public void IsUsable_RequiresNumberCapacityAndStableIdentifier(
        int number,
        string uniqueId,
        string serialNumber,
        ulong sizeBytes,
        bool usable)
    {
        var identity = new DiskIdentity(number, uniqueId, serialNumber, "", "", sizeBytes);

        Assert.Equal(usable, identity.IsUsable);
        Assert.Equal(usable, identity.Matches(identity));
    }

    [Fact]
    public void FromDiskInfo_RetainsRawIdentityAndIgnoresMutablePartitionState()
    {
        var raw = new DiskInfo(3, " Device ", " Serial ", " USB ", "RAW", 64_000_000_000,
            false, false, false, false, true)
        { UniqueId = " UID " };
        DiskInfo partitioned = raw with { PartitionStyle = "GPT", IsReadOnly = true, IsOffline = true };

        DiskIdentity captured = DiskIdentity.FromDiskInfo(raw);

        Assert.Equal(new DiskIdentity(3, " UID ", " Serial ", " Device ", " USB ", 64_000_000_000), captured);
        Assert.True(captured.Matches(DiskIdentity.FromDiskInfo(partitioned)));
    }
}
