// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Connect.Network;

namespace Foundry.Connect.Tests.Network;

public sealed class WifiSsidTests
{
    [Fact]
    public void Equality_ComparesBytesNotText()
    {
        Assert.NotEqual(WifiSsid.FromText("Lab"), WifiSsid.FromText("LAB"));
        Assert.Equal(WifiSsid.FromText("Lab"), WifiSsid.FromHex("4C6162"));
        Assert.Equal(WifiSsid.FromText("Lab").GetHashCode(), WifiSsid.FromHex("4c6162").GetHashCode());
    }

    [Fact]
    public void FromText_KeepsSurroundingSpaces()
    {
        WifiSsid ssid = WifiSsid.FromText(" Lab ");

        Assert.Equal(" Lab ", ssid.DisplayName);
        Assert.Equal("204C616220", ssid.Hex);
    }

    [Fact]
    public void DisplayName_ToleratesInvalidUtf8()
    {
        var ssid = new WifiSsid([0xFF, 0x41]);

        Assert.EndsWith("A", ssid.DisplayName, StringComparison.Ordinal);
        Assert.Equal("FF41", ssid.Hex);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(33)]
    public void Constructor_RejectsInvalidLength(int length)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new WifiSsid(new byte[length]));
        Assert.False(WifiSsid.TryFromBytes(new byte[length], out _));
    }

    [Fact]
    public void ToString_DoesNotExposeTheName()
    {
        Assert.DoesNotContain("Lab", WifiSsid.FromText("Lab").ToString(), StringComparison.Ordinal);
    }
}
