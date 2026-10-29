// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Text;
using Foundry.Connect.Network;
using Foundry.Connect.Network.Native;
using Foundry.Connect.Network.Policy;

namespace Foundry.Connect.Tests.Network;

public sealed class WifiNetworkListPolicyTests
{
    private static readonly Guid First = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Second = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private static WlanAvailableNetworkInfo Network(Guid interfaceId, byte[] ssid, int signal) =>
        new(interfaceId, ssid, WlanAuthAlgorithm.RsnaPsk, true, signal);

    private static byte[] Text(string value) => Encoding.UTF8.GetBytes(value);

    [Fact]
    public void Build_ExcludesHiddenNetworks()
    {
        IReadOnlyList<VisibleWifiNetwork> result = WifiNetworkListPolicy.Build(
        [
            Network(First, [], 90),
            Network(First, [0, 0, 0], 80),
            Network(First, Text("Lab"), 10)
        ]);

        Assert.Equal("Lab", Assert.Single(result).Ssid.DisplayName);
    }

    [Fact]
    public void Build_KeepsNetworksThatDifferOnlyByCase()
    {
        IReadOnlyList<VisibleWifiNetwork> result = WifiNetworkListPolicy.Build(
        [
            Network(First, Text("Lab"), 50),
            Network(First, Text("LAB"), 40)
        ]);

        Assert.Equal(2, result.Count);
    }

    [Fact]
    public void Build_MergesSameSsidKeepingStrongestSignal()
    {
        IReadOnlyList<VisibleWifiNetwork> result = WifiNetworkListPolicy.Build(
        [
            Network(First, Text("Lab"), 40),
            Network(Second, Text("Lab"), 70)
        ]);

        VisibleWifiNetwork network = Assert.Single(result);
        Assert.Equal(70, network.SignalQuality);
        Assert.Equal(Second, network.InterfaceId);
        Assert.Equal(WifiSecurityType.Wpa2Personal, network.Security);
    }

    [Fact]
    public void Build_OrdersBySignalThenName()
    {
        IReadOnlyList<VisibleWifiNetwork> result = WifiNetworkListPolicy.Build(
        [
            Network(First, Text("beta"), 50),
            Network(First, Text("Alpha"), 50),
            Network(First, Text("Gamma"), 90)
        ]);

        Assert.Equal(["Gamma", "Alpha", "beta"], result.Select(static item => item.Ssid.DisplayName));
    }
}
