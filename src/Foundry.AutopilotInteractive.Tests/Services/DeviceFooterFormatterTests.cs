// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.AutopilotInteractive.Services.Device;
using Foundry.Core.Models.Autopilot;

namespace Foundry.AutopilotInteractive.Tests.Services;

public sealed class DeviceFooterFormatterTests
{
    [Fact]
    public void Format_JoinsValuesWithMiddleDot()
    {
        string footer = DeviceFooterFormatter.Format(
            new DeviceInformation("Dell Inc.", "Latitude 7440", "7XK2ABC"),
            computerName: null,
            unknownText: "Unknown");

        Assert.Equal("Dell Inc. \u00B7 Latitude 7440 \u00B7 7XK2ABC", footer);
    }

    [Fact]
    public void Format_UsesUnknownText_ForBlankValues()
    {
        string footer = DeviceFooterFormatter.Format(
            new DeviceInformation(null, "  ", "7XK2ABC"),
            computerName: null,
            unknownText: "Unknown");

        Assert.Equal("Unknown \u00B7 Unknown \u00B7 7XK2ABC", footer);
    }

    [Fact]
    public void Format_TrimsValues()
    {
        string footer = DeviceFooterFormatter.Format(
            new DeviceInformation(" Dell Inc. ", "Latitude 7440\t", " 7XK2ABC "),
            computerName: " PC-001 ",
            unknownText: "Unknown");

        Assert.Equal("Dell Inc. \u00B7 Latitude 7440 \u00B7 7XK2ABC \u00B7 PC-001", footer);
    }

    [Fact]
    public void Format_AppendsComputerName_WhenSet()
    {
        string footer = DeviceFooterFormatter.Format(
            new DeviceInformation("Dell Inc.", "Latitude 7440", "7XK2ABC"),
            computerName: "PC-001",
            unknownText: "Unknown");

        Assert.Equal("Dell Inc. \u00B7 Latitude 7440 \u00B7 7XK2ABC \u00B7 PC-001", footer);
    }

    [Theory]
    [InlineData("FINAL-PC", "UPLOAD-PC", "FINAL-PC")]
    [InlineData("FINAL-PC", null, "FINAL-PC")]
    [InlineData(null, "UPLOAD-PC", "UPLOAD-PC")]
    [InlineData("  ", "UPLOAD-PC", "UPLOAD-PC")]
    [InlineData(" FINAL-PC ", null, "FINAL-PC")]
    [InlineData(null, null, null)]
    [InlineData("  ", "  ", null)]
    public void ResolveComputerName_PrefersComputerNameThenAssignedComputerName(
        string? computerName, string? assignedComputerName, string? expected)
    {
        var configuration = new AutopilotInteractiveRuntimeConfiguration
        {
            ComputerName = computerName,
            AssignedComputerName = assignedComputerName
        };

        Assert.Equal(expected, DeviceFooterFormatter.ResolveComputerName(configuration));
    }

    [Fact]
    public void Format_OmitsComputerName_WhenBlank()
    {
        string footer = DeviceFooterFormatter.Format(
            new DeviceInformation("Dell Inc.", "Latitude 7440", "7XK2ABC"),
            computerName: "   ",
            unknownText: "Unknown");

        Assert.Equal("Dell Inc. \u00B7 Latitude 7440 \u00B7 7XK2ABC", footer);
    }
}
