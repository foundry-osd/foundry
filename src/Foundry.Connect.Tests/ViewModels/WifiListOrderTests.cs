// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using Foundry.Connect.Network;
using Foundry.Connect.ViewModels;

namespace Foundry.Connect.Tests.ViewModels;

public sealed class WifiListOrderTests
{
    private static VisibleWifiNetwork Network(string ssid) => new(Guid.Empty, WifiSsid.FromText(ssid), WifiSecurityType.Wpa2Personal, 50);

    private static string[] Names(IEnumerable<WifiSsid> ssids) => ssids.Select(static ssid => ssid.DisplayName).ToArray();

    [Fact]
    public void Merge_WithoutSelection_FollowsTheIncomingOrder()
    {
        IReadOnlyList<WifiSsid> order = WifiListOrder.Merge([WifiSsid.FromText("B"), WifiSsid.FromText("A")], [Network("A"), Network("C"), Network("B")], null);

        Assert.Equal(["A", "C", "B"], Names(order));
    }

    [Fact]
    public void Merge_WithSelection_KeepsPositionsAppendsNewAndDropsVanished()
    {
        IReadOnlyList<WifiSsid> order = WifiListOrder.Merge(
            [WifiSsid.FromText("A"), WifiSsid.FromText("B"), WifiSsid.FromText("C")],
            [Network("D"), Network("C"), Network("A")],
            WifiSsid.FromText("B"));

        Assert.Equal(["A", "B", "C", "D"], Names(order));
    }

    [Fact]
    public void Merge_ComparesSsidsAsBytes()
    {
        IReadOnlyList<WifiSsid> order = WifiListOrder.Merge([WifiSsid.FromText("Lab")], [Network("LAB"), Network("Lab")], WifiSsid.FromText("Lab"));

        Assert.Equal(["Lab", "LAB"], Names(order));
    }
}
