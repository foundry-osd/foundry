// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Connect.Models.Network;
using Foundry.Connect.Services.Network;

namespace Foundry.Connect.Tests;

public sealed class WifiNetworkIdentityTests
{
    [Fact]
    public void SelectStrongest_PreservesCaseDistinctAndRawDistinctNetworks()
    {
        WifiNetworkSummary[] networks =
        [
            new() { Ssid = "Lab", SsidHex = "4c6162", SignalStrengthPercent = 10 },
            new() { Ssid = "Lab", SsidHex = "4C6162", SignalStrengthPercent = 90 },
            new() { Ssid = "lab", SsidHex = "6C6162", SignalStrengthPercent = 80 },
            new() { Ssid = "�", SsidHex = "FF", SignalStrengthPercent = 70 },
            new() { Ssid = "�", SsidHex = "FE", SignalStrengthPercent = 60 }
        ];
        var result = WifiNetworkIdentity.SelectStrongest(networks);
        Assert.Equal(4, result.Count);
        Assert.Same(networks[1], result[0]);
        Assert.Contains(networks[2], result);
        Assert.Contains(networks[3], result);
        Assert.Contains(networks[4], result);
    }

    [Theory]
    [InlineData("Lab", null, "lab", null, false)]
    [InlineData(" Lab ", null, "Lab", null, false)]
    [InlineData("   ", null, "   ", "202020", true)]
    [InlineData("�", "FF", "�", "FE", false)]
    [InlineData("unreliable display", "ff", "other display", "FF", true)]
    [InlineData("Lab", "invalid", "Lab", null, false)]
    public void Matches_UsesByteIdentity(string left, string? leftHex, string right, string? rightHex, bool expected)
    {
        Assert.Equal(expected, WifiNetworkIdentity.Matches(left, leftHex, right, rightHex));
    }

    [Fact]
    public void DecodeSsid_PreservesSpacesAndOnlyReportedBytes()
    {
        Assert.Equal(" Lab ", WifiNetworkIdentity.DecodeSsid(" Lab unwanted"u8.ToArray(), 5));
        Assert.Equal("   ", WifiNetworkIdentity.DecodeSsid("   "u8.ToArray(), 3));
        Assert.Equal("Hidden network", WifiNetworkIdentity.DecodeSsid([65], 2));
    }
}
