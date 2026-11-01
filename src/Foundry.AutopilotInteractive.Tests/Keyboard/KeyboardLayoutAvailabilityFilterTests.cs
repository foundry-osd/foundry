// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.AutopilotInteractive.Services.Keyboard;
using Foundry.Utilities.Globalization;

namespace Foundry.AutopilotInteractive.Tests.Keyboard;

public sealed class KeyboardLayoutAvailabilityFilterTests
{
    private static readonly KeyboardLayout BelgianFrench = new("0000080C", "fr", 0x080C, "KBDBE.DLL", "Belgian French");
    private static readonly KeyboardLayout Dutch = new("00000413", "nl", 0x0413, "KBDNE.DLL", "Dutch");

    [Fact]
    public void Filter_KeepsLayoutWhoseRegistryKeyAndFileExist()
    {
        var availability = new FakeAvailability { ["0000080C"] = "KBDBE.DLL" };
        availability.Files.Add("KBDBE.DLL");

        IReadOnlyList<KeyboardLayout> result = KeyboardLayoutAvailabilityFilter.Filter([BelgianFrench], availability);

        Assert.Equal([BelgianFrench], result);
    }

    [Fact]
    public void Filter_HidesLayoutWhoseRegistryKeyIsMissing()
    {
        var availability = new FakeAvailability { ["0000080C"] = "KBDBE.DLL" };
        availability.Files.Add("KBDBE.DLL");
        availability.Files.Add("KBDNE.DLL");

        IReadOnlyList<KeyboardLayout> result = KeyboardLayoutAvailabilityFilter.Filter([BelgianFrench, Dutch], availability);

        Assert.Equal([BelgianFrench], result);
    }

    [Fact]
    public void Filter_HidesLayoutWhoseLayoutFileIsMissing()
    {
        var availability = new FakeAvailability { ["0000080C"] = "KBDBE.DLL", ["00000413"] = "KBDNE.DLL" };
        availability.Files.Add("KBDBE.DLL");

        IReadOnlyList<KeyboardLayout> result = KeyboardLayoutAvailabilityFilter.Filter([BelgianFrench, Dutch], availability);

        Assert.Equal([BelgianFrench], result);
    }

    [Fact]
    public void Filter_HidesLayoutWithoutLayoutFileValue()
    {
        var availability = new FakeAvailability { ["0000080C"] = " " };

        IReadOnlyList<KeyboardLayout> result = KeyboardLayoutAvailabilityFilter.Filter([BelgianFrench], availability);

        Assert.Empty(result);
    }

    private sealed class FakeAvailability : Dictionary<string, string>, IKeyboardLayoutAvailability
    {
        public HashSet<string> Files { get; } = new(StringComparer.OrdinalIgnoreCase);

        public string? GetRegisteredLayoutFile(string layoutId) => TryGetValue(layoutId, out string? file) ? file : null;

        public bool SystemFileExists(string layoutFile) => Files.Contains(layoutFile);
    }
}
